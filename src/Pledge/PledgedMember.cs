namespace PledgeContracts;

public enum PledgedParameterDirection
{
    // Passed by value, in or ref readonly: the caller supplies it.
    Value,

    // Passed by ref: the caller supplies it and the member may change it.
    Ref,

    // An out parameter: the member supplies it, nothing is generated for it.
    Out,
}

// One parameter of a contracted member as the generator saw it. AllowsNull reflects the nullable annotation, so a
// test generator knows whether null is a legal input.
public sealed class PledgedParameter(string name, Type type, PledgedParameterDirection direction, bool allowsNull)
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public Type Type { get; } = type ?? throw new ArgumentNullException(nameof(type));

    public PledgedParameterDirection Direction { get; } = direction;

    public bool AllowsNull { get; } = allowsNull;

    public override string ToString() => $"{Type.Name} {Name}";
}

// One contract of a member, with its normalized text. ExceptionType is set for EnsuresOnThrow.
public sealed class PledgedContract(PledgeKind kind, string expression, Type? exceptionType)
{
    public PledgeKind Kind { get; } = kind;

    public string Expression { get; } = expression ?? throw new ArgumentNullException(nameof(expression));

    public Type? ExceptionType { get; } = exceptionType;

    public override string ToString() => ExceptionType is null ? $"{Kind}: {Expression}" : $"{Kind} on {ExceptionType.Name}: {Expression}";
}

// Runs a contracted member once with the given receiver (null for static members) and arguments (one slot per
// parameter, in order; out slots are ignored) and reports what happened instead of throwing.
public delegate ValueTask<PledgeOutcome> PledgeProbe(object? receiver, object?[] arguments);

// A contracted member registered by the generator. Probe is null when the member cannot be probed generically,
// and UnsupportedReason says why (for instance a Span<T> parameter, which cannot be boxed).
public sealed class PledgedMember
{
    public PledgedMember(
        Type declaringType,
        string name,
        string signature,
        bool isStatic,
        Type? resultType,
        IReadOnlyList<PledgedParameter> parameters,
        IReadOnlyList<PledgedContract> contracts,
        PledgeProbe? probe,
        string? unsupportedReason = null)
    {
        DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
        IsStatic = isStatic;
        ResultType = resultType;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
        Probe = probe;
        UnsupportedReason = probe is null ? unsupportedReason ?? "the member cannot be probed" : null;
    }

    public Type DeclaringType { get; }

    public string Name { get; }

    // The member's name and parameter types, as in the metadata attributes: "Withdraw(decimal)".
    public string Signature { get; }

    public bool IsStatic { get; }

    // The value Result<T>() stands for: the return type, or T for Task<T> and ValueTask<T>. Null for void.
    public Type? ResultType { get; }

    public IReadOnlyList<PledgedParameter> Parameters { get; }

    public IReadOnlyList<PledgedContract> Contracts { get; }

    public PledgeProbe? Probe { get; }

    public string? UnsupportedReason { get; }

    public override string ToString() => DeclaringType.Name + "." + Signature;
}
