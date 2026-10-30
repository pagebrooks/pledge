using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PledgeContracts.Roslyn.Generator;

// A source position that compares by value, so models holding it stay cacheable. Location itself is fine to
// rebuild from these parts when a diagnostic needs one.
internal readonly record struct SourceSite(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public int Line => LineSpan.Start.Line + 1;

    public static SourceSite Of(SyntaxNode node)
    {
        var location = node.GetLocation();
        var lineSpan = location.GetLineSpan();
        return new SourceSite(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

internal sealed record ParameterModel(string Name, string Type, RefKind RefKind)
{
    public string Declaration => Modifier(forArgument: false) + Type + " " + Name;

    public string Argument => Modifier(forArgument: true) + Name;

    private string Modifier(bool forArgument) => RefKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => forArgument ? "in " : "ref readonly ",
        _ => string.Empty,
    };
}

internal enum ContractKind
{
    Precondition,
    Postcondition,
    PostconditionOnThrow,
}

internal enum AsyncKind
{
    None,
    Task,
    TaskOfResult,
    ValueTask,
    ValueTaskOfResult,
}

// One lifted contract: the author's text for messages and metadata, the rewritten expression for the generated
// check. ExceptionType and ExceptionDepth are set for EnsuresOnThrow; depth orders catch clauses derived-first.
internal sealed record ContractModel(
    int Index,
    ContractKind Kind,
    string Text,
    string Expression,
    string? Message,
    SourceSite Site,
    string? ExceptionType,
    int ExceptionDepth)
{
    public string PledgeKind => Kind == ContractKind.Precondition ? "Precondition" : "Postcondition";
}

// One Old(...) occurrence, evaluated after the preconditions and before the body into a local named Name.
internal sealed record SnapshotModel(string Name, string Type, string Expression, string Text, SourceSite Site);

internal sealed record ContractedMethod(
    string Key,
    string HintName,
    string ClassName,
    string ContainingType,
    string MemberName,
    string MethodName,
    string Signature,
    bool IsStatic,
    bool ReturnsVoid,
    string ReturnType,
    AsyncKind AsyncKind,
    string? ResultType,
    bool OnThrowCoversCancellation,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<string> Usings,
    EquatableArray<ContractModel> Contracts,
    EquatableArray<SnapshotModel> Snapshots,
    EquatableArray<string> Problems,
    SourceSite Site)
{
    public bool HasResult => ResultType is not null;

    // Postconditions and snapshots need the original call inside a try block; preconditions alone just forward.
    public bool WrapsBody => Snapshots.Count > 0 || Contracts.Any(contract => contract.Kind != ContractKind.Precondition);
}

// One interceptable invocation of a contracted method. Version and Data are what [InterceptsLocation] needs.
internal readonly record struct CallSite(string MethodKey, int Version, string Data, string FilePath, int Line);

// A Pledge call somewhere the generator cannot act on it, reported so it never silently compiles to nothing.
internal readonly record struct MisplacedContract(SourceSite Site, string Member, string Problem);
