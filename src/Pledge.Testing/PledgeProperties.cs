using System.Globalization;
using System.Text.RegularExpressions;
using CsCheck;
using PledgeContracts.Testing.Generation;

namespace PledgeContracts.Testing;

// Contracts as property-based test oracles. For each pledged member: generate a receiver and arguments, discard
// inputs the preconditions reject, call the member through its generated probe, and fail on a broken
// postcondition, a broken EnsuresOnThrow or an exception no contract declares. CsCheck shrinks a failure to the
// smallest input and the exception prints it with the seed that replays the run.
public static partial class PledgeProperties
{
    public static IReadOnlyList<PledgedMember> Members<T>() => Members(typeof(T));

    public static IReadOnlyList<PledgedMember> Members(Type type)
    {
        var members = PledgeRegistry.For(type);
        return members.Count > 0
            ? members
            : throw new InvalidOperationException(
                $"No pledged members are registered for {type.FullName}. Its assembly must be built with the Pledge generator and <PledgeRegistry>true</PledgeRegistry>, which is the default unless every contract kind is Ignore (as in Release builds by default).");
    }

    public static void Check<T>(int iterations = 100, string? seed = null) =>
        Check<T>(new PledgePropertyOptions { Iterations = iterations, Seed = seed });

    // Blocks on CheckAsync away from the caller's synchronization context, so async members cannot deadlock it.
    public static void Check<T>(PledgePropertyOptions options) =>
        Task.Run(() => CheckAsync<T>(options)).GetAwaiter().GetResult();

    public static Task CheckAsync<T>() => CheckAsync(typeof(T), null);

    public static Task CheckAsync<T>(PledgePropertyOptions? options) => CheckAsync(typeof(T), options);

    public static Task CheckAsync(Type type) => CheckAsync(type, null);

    public static Task CheckAsync(PledgedMember member) => CheckAsync(member, null);

    // Checks every member of the type that can be probed, reporting the others through Output, and fails after all
    // of them have run: with the one failure, or with every failure in one message.
    public static async Task CheckAsync(Type type, PledgePropertyOptions? options)
    {
        ArgumentNullException.ThrowIfNull(type);
        options = (options ?? new PledgePropertyOptions()).Validate();

        List<PledgePropertyException> failures = [];
        foreach (var member in Members(type))
        {
            if (member.Probe is null)
            {
                options.Output?.Invoke($"{member}: skipped, {member.UnsupportedReason}");
                continue;
            }

            try
            {
                await CheckAsync(member, options).ConfigureAwait(false);
            }
            catch (PledgePropertyException exception)
            {
                failures.Add(exception);
            }
            catch (InvalidOperationException exception)
            {
                // No generator for one of its inputs: a failure of this member, not a reason to skip the rest.
                failures.Add(new PledgePropertyException(exception.Message, exception));
            }
        }

        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            var message = $"{failures.Count} members of {type.Name} broke their pledges." + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine + Environment.NewLine, failures.Select(failure => failure.Message));
            throw new PledgePropertyException(message, new AggregateException(failures));
        }
    }

    public static async Task CheckAsync(PledgedMember member, PledgePropertyOptions? options)
    {
        ArgumentNullException.ThrowIfNull(member);
        options = (options ?? new PledgePropertyOptions()).Validate();
        var probe = member.Probe ?? throw new InvalidOperationException($"{member} cannot be property-tested: {member.UnsupportedReason}.");

        var generators = new RecipeGenerators(options.Generators);
        var plan = Plan(member, generators);
        var tally = new Tally();

        try
        {
            await plan.Generator.SampleAsync(
                async sample =>
                {
                    object? receiver;
                    object?[] arguments;
                    try
                    {
                        receiver = sample.Receiver?.Build();
                        arguments = [.. sample.Arguments.Select(argument => argument.Build())];
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        // The receiver's constructor refused the input: no different from a precondition.
                        tally.Rejected();
                        return;
                    }

                    var outcome = await probe(receiver, arguments).ConfigureAwait(false);
                    switch (outcome.Kind)
                    {
                        case PledgeOutcomeKind.Kept:
                            tally.Kept();
                            break;
                        case PledgeOutcomeKind.Rejected:
                            tally.Rejected();
                            break;
                        case PledgeOutcomeKind.Threw when outcome.IsDeclared || options.AllowUndeclaredExceptions:
                            tally.Threw();
                            break;
                        default:
                            throw new FailedSample(outcome) { Sample = sample };
                    }
                },
                writeLine: options.Output,
                seed: options.Seed,
                iter: options.Iterations,
                threads: options.Threads,
                print: sample => sample.Print(plan.Names)).ConfigureAwait(false);
        }
        catch (CsCheckException exception) when (exception.InnerException is FailedSample failed)
        {
            var sample = failed.Sample ?? throw new PledgePropertyException($"{member} broke its pledge. {failed.Outcome}", exception);
            var counterexample = new PledgeCounterexample(
                member,
                failed.Outcome,
                sample.Receiver?.ToString(),
                [.. plan.Names.Zip(sample.Arguments, (name, argument) => new KeyValuePair<string, string>(name, argument.ToString()))],
                SeedPattern().Match(exception.Message) is { Success: true } match ? match.Groups["seed"].Value : null,
                SummaryPattern().Match(exception.Message) is { Success: true } summary ? summary.Groups["summary"].Value : null);
            throw new PledgePropertyException(counterexample);
        }
        catch (CsCheckException exception)
        {
            throw new PledgePropertyException($"{member} could not be property-tested: {exception.InnerException?.Message ?? exception.Message}", exception);
        }

        options.Output?.Invoke($"{member}: {tally}");
        if (tally.Accepted == 0)
        {
            throw new PledgePropertyException(
                $"{member}: the preconditions rejected all {tally.Total.ToString(CultureInfo.InvariantCulture)} generated inputs, so nothing was tested. Supply generators that meet them with PledgePropertyOptions.Use<T>().");
        }
    }

    private static MemberPlan Plan(PledgedMember member, RecipeGenerators generators)
    {
        try
        {
            var receiver = member.IsStatic
                ? Gen.Const((Recipe?)null)
                : Gen.Select(generators.For(member.DeclaringType, allowsNull: false), static recipe => (Recipe?)recipe);
            var arguments = member.Parameters
                .Select(parameter => parameter.Direction == PledgedParameterDirection.Out
                    ? Gen.Const<Recipe>(new LiteralRecipe(null))
                    : generators.For(parameter.Type, parameter.AllowsNull))
                .ToList();

            var generator = Gen.Select(receiver, RecipeGenerators.Sequence(arguments), static (receiver, arguments) => new Sample(receiver, arguments));
            return new MemberPlan(generator, [.. member.Parameters.Select(parameter => parameter.Name)]);
        }
        catch (UnsupportedTypeException exception)
        {
            throw new InvalidOperationException($"{member} cannot be property-tested: Pledge.Testing {exception.Message}. Supply a generator with PledgePropertyOptions.Use<T>().", exception);
        }
    }

    [GeneratedRegex("Set seed: \"(?<seed>[^\"]+)\"")]
    private static partial Regex SeedPattern();

    [GeneratedRegex(@"to reproduce \((?<summary>[^)]*)\)")]
    private static partial Regex SummaryPattern();

    private sealed record MemberPlan(Gen<Sample> Generator, IReadOnlyList<string> Names);

    private sealed class Sample(Recipe? receiver, Recipe[] arguments)
    {
        public Recipe? Receiver { get; } = receiver;

        public Recipe[] Arguments { get; } = arguments;

        public string Print(IReadOnlyList<string> names)
        {
            var parts = names.Zip(Arguments, (name, argument) => $"{name} = {argument}");
            return Receiver is null ? string.Join(", ", parts) : string.Join(", ", parts.Prepend($"receiver = {Receiver}"));
        }
    }

    // Thrown inside the CsCheck assertion so CsCheck shrinks it, and unwrapped afterwards. The sample is attached
    // where it is thrown, so after shrinking the inner exception carries the smallest failing sample.
    private sealed class FailedSample(PledgeOutcome outcome) : Exception(outcome.ToString())
    {
        public PledgeOutcome Outcome { get; } = outcome;

        public Sample? Sample { get; set; }
    }

    private sealed class Tally
    {
        private int _kept;
        private int _rejected;
        private int _threw;

        public int Accepted => Volatile.Read(ref _kept) + Volatile.Read(ref _threw);

        public int Total => Accepted + Volatile.Read(ref _rejected);

        public void Kept() => Interlocked.Increment(ref _kept);

        public void Rejected() => Interlocked.Increment(ref _rejected);

        public void Threw() => Interlocked.Increment(ref _threw);

        public override string ToString() => string.Create(
            CultureInfo.InvariantCulture,
            $"{Total} inputs: {_kept} kept every contract, {_threw} threw a declared exception, {_rejected} rejected by the preconditions");
    }
}
