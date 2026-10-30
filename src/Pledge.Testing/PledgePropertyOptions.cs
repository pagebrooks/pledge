using CsCheck;
using PledgeContracts.Testing.Generation;

namespace PledgeContracts.Testing;

// How hard and how reproducibly to test. Iterations counts generated inputs, including the ones the
// preconditions reject. Threads defaults to 1 so a seed reproduces the same run.
public sealed class PledgePropertyOptions
{
    private readonly Dictionary<Type, Gen<Recipe>> _generators = [];

    public int Iterations { get; set; } = 100;

    // The CsCheck seed a failure report prints; set it to replay that run.
    public string? Seed { get; set; }

    public int Threads { get; set; } = 1;

    // By default an exception no EnsuresOnThrow declares is a failure: the contracts did not say the member may
    // throw it. Set this for members whose exceptions are documented elsewhere.
    public bool AllowUndeclaredExceptions { get; set; }

    // Receives a one-line summary per member and CsCheck's own output. The xUnit and TUnit adapters route it to
    // the test's output.
    public Action<string>? Output { get; set; }

    internal IReadOnlyDictionary<Type, Gen<Recipe>> Generators => _generators;

    // Generates every T, receivers and nested constructor arguments included, with this generator instead of the
    // built-in choice.
    public PledgePropertyOptions Use<T>(Gen<T> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generators[typeof(T)] = RecipeGenerators.Literal(generator);
        return this;
    }

    internal PledgePropertyOptions Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(Iterations, 1, nameof(Iterations));
        ArgumentOutOfRangeException.ThrowIfLessThan(Threads, 1, nameof(Threads));
        return this;
    }
}
