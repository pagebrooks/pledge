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
}

// One lifted contract: the author's text for messages, the rewritten expression for the generated check.
internal sealed record ContractModel(ContractKind Kind, string Text, string Expression, string? Message, SourceSite Site);

internal sealed record ContractedMethod(
    string Key,
    string HintName,
    string ClassName,
    string ContainingType,
    string MemberName,
    string MethodName,
    bool IsStatic,
    bool ReturnsVoid,
    string ReturnType,
    EquatableArray<ParameterModel> Parameters,
    EquatableArray<string> Usings,
    EquatableArray<ContractModel> Contracts,
    EquatableArray<string> Problems,
    SourceSite Site);

// One interceptable invocation of a contracted method. Version and Data are what [InterceptsLocation] needs.
internal readonly record struct CallSite(string MethodKey, int Version, string Data, string FilePath, int Line);
