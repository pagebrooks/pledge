using Microsoft.CodeAnalysis;
using PledgeContracts.Roslyn.CodeFixes;

namespace PledgeContracts.Roslyn.Tests;

public sealed class ResultAndOldAnalyzerTests
{
    [Theory]
    [InlineData("public int M(int x) { Pledge.Ensures(Pledge.Result<string>() != null); return x; }", "must name the result type of 'M': Result<int>()")]
    [InlineData("public async System.Threading.Tasks.Task<int> M(int x) { Pledge.Ensures(Pledge.Result<System.Threading.Tasks.Task<int>>() != null); await System.Threading.Tasks.Task.Yield(); return x; }", "Result<int>()")]
    [InlineData("public void M(int x) { Pledge.Ensures(Pledge.Result<int>() > 0); }", "cannot be used in 'M', which returns nothing")]
    [InlineData("public async System.Threading.Tasks.Task M(int x) { Pledge.Ensures(Pledge.Result<int>() > 0); await System.Threading.Tasks.Task.Yield(); }", "which returns nothing")]
    [InlineData("public int M(int x) { Pledge.Requires(Pledge.Result<int>() > 0); return x; }", "only meaningful inside Pledge.Ensures")]
    [InlineData("public int M(int x) { Pledge.EnsuresOnThrow<System.Exception>(Pledge.Result<int>() > 0); return x; }", "has no value when the member throws")]
    [InlineData("public int M(int x) { Pledge.Ensures(Pledge.Old(Pledge.Result<int>()) > 0); return x; }", "cannot appear inside Pledge.Old")]
    [InlineData("public int M(int x) { var r = Pledge.Result<int>(); return x; }", "only meaningful inside Pledge.Ensures")]
    public async Task Misused_Result_is_PLEDGE002(string member, string expectedMessage)
    {
        var diagnostic = await AnalyzerHarness.SingleAsync($$"""
            using PledgeContracts;

            public sealed class Holder
            {
                {{member}}
            }
            """, "PLEDGE002");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(expectedMessage, diagnostic.Message(), StringComparison.Ordinal);
        Assert.StartsWith("Pledge.Result<", diagnostic.SourceText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public int M(int x) { Pledge.Requires(Pledge.Old(x) > 0); return x; }")]
    [InlineData("public int M(int x) { var before = Pledge.Old(x); return x; }")]
    public async Task Old_outside_a_postcondition_is_PLEDGE003(string member)
    {
        var diagnostic = await AnalyzerHarness.SingleAsync($$"""
            using PledgeContracts;

            public sealed class Holder
            {
                {{member}}
            }
            """, "PLEDGE003");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("only meaningful inside Pledge.Ensures or Pledge.EnsuresOnThrow", diagnostic.Message(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correct_Result_and_Old_are_silent()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync("""
            using System.Threading.Tasks;
            using PledgeContracts;

            public sealed class Holder
            {
                public int Count { get; private set; }

                public int M(int x)
                {
                    Pledge.Ensures(Pledge.Result<int>() > Pledge.Old(Count));
                    Pledge.EnsuresOnThrow<System.Exception>(Count == Pledge.Old(Count));
                    return ++Count;
                }

                public async ValueTask<string> N()
                {
                    Pledge.Ensures(Pledge.Result<string>().Length > 0);
                    await Task.Yield();
                    return "x";
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_fix_replaces_the_type_argument_with_the_result_type()
    {
        var fixedSource = await AnalyzerHarness.ApplyFixToSingleFileAsync("""
            using System.Collections.Generic;
            using PledgeContracts;

            public sealed class Holder
            {
                public List<int> M()
                {
                    Pledge.Ensures(Pledge.Result<int>() != null);
                    return [];
                }
            }
            """, new UseResultTypeCodeFixProvider(), "PLEDGE002");

        Assert.Contains("Pledge.Ensures(Pledge.Result<List<int>>() != null);", fixedSource, StringComparison.Ordinal);
    }
}
