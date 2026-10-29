using System.Globalization;

namespace PledgeContracts;

// Everything the violation handler and the exceptions know about one broken contract. Built by generated code,
// so the required members are the ones every wrapper has at hand and the rest default to "unknown".
public sealed record PledgeViolation
{
    public required PledgeKind Kind { get; init; }

    // The contract expression as written, after normalization by the generator (e.g. "Balance == Old(Balance) - amount").
    public required string Expression { get; init; }

    // The contracted member, "Type.Member" for methods and properties, the type name for invariants.
    public required string Member { get; init; }

    // The author's message, when one was given. Null means the expression text is the message.
    public string? Message { get; init; }

    // Call site of a precondition violation, captured at generation time. Null for callee-side kinds.
    public string? CallerFilePath { get; init; }

    public int CallerLineNumber { get; init; }

    // Set when the contract expression itself threw instead of producing a bool.
    public Exception? Cause { get; init; }

    public PledgeBlame Blame => Kind == PledgeKind.Precondition ? PledgeBlame.Caller : PledgeBlame.Callee;

    public override string ToString()
    {
        var detail = Message is null || Message == Expression ? Expression : $"{Message} [{Expression}]";
        var text = Kind switch
        {
            PledgeKind.Precondition => $"Precondition of {Member} violated by its caller: {detail}",
            PledgeKind.Postcondition => $"Postcondition of {Member} violated: {detail}",
            PledgeKind.Invariant => $"Invariant of {Member} violated: {detail}",
            _ => $"Contract of {Member} violated: {detail}",
        };

        if (CallerFilePath is not null)
        {
            text += $" (at {CallerFilePath}:{CallerLineNumber.ToString(CultureInfo.InvariantCulture)})";
        }

        if (Cause is not null)
        {
            text += $". The contract expression threw {Cause.GetType().Name}: {Cause.Message}";
        }

        return text;
    }
}
