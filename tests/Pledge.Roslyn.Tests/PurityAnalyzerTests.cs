using Microsoft.CodeAnalysis;
using PledgeContracts.Roslyn.CodeFixes;

namespace PledgeContracts.Roslyn.Tests;

public sealed class PurityAnalyzerTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using PledgeContracts;

        """;

    [Theory]
    [InlineData("Pledge.Requires(items.Count > 0 && items.All(i => i > 0) && Math.Abs(x) < 10);")]
    [InlineData("Pledge.Requires(name.Length > 0 && !string.IsNullOrWhiteSpace(name) && name.StartsWith(\"a\", StringComparison.Ordinal));")]
    [InlineData("Pledge.Requires(items.Contains(x) && items.IndexOf(x) >= 0 && lookup.TryGetValue(x, out var found) && found > 0);")]
    [InlineData("Pledge.Requires(IsValid(x) && this.Equals(this) && x.CompareTo(1) >= 0 && x.ToString().Length > 0);")]
    [InlineData("Pledge.Requires(Check(x) && nameof(x).Length > 0 && x is > 0 and < 10 && (x, name) != (0, null));")]
    [InlineData("Pledge.Requires(items.Select(i => { var doubled = i * 2; return doubled; }).Any(d => d > 0));")]
    [InlineData("Pledge.Requires(new Holder(x) != null && this == this && x + 1 > x);")]
    public async Task Pure_expressions_are_silent(string contract)
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(Preamble + $$"""
            public sealed class Holder
            {
                public Holder(int seed) { }

                public List<int> items = new();
                public Dictionary<int, int> lookup = new();
                public string name = "";

                [Pure]
                public bool IsValid(int v) => v > 0;

                [System.Diagnostics.Contracts.Pure]
                public static bool Check(int v) => v > 0;

                public int M(int x)
                {
                    {{contract}}
                    return x;
                }

                public void Call() => M(1);
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("Pledge.Requires(Save(x));", "calls 'Save', which is not marked [Pure]")]
    [InlineData("Pledge.Requires((Count = x) > 0);", "assigns to 'Count'")]
    [InlineData("Pledge.Requires(Count++ > 0);", "modifies 'Count'")]
    [InlineData("Pledge.Requires(--Count > 0);", "modifies 'Count'")]
    [InlineData("Pledge.Requires(Console.ReadLine() != null);", "calls 'ReadLine', which is not marked [Pure]")]
    [InlineData("Pledge.Requires(predicate(x));", "invokes a delegate, whose purity cannot be known")]
    [InlineData("Pledge.Requires(int.TryParse(name, out Count));", "passes 'Count' by reference")]
    [InlineData("Pledge.Ensures(Pledge.Old(Save(x)));", "calls 'Save', which is not marked [Pure]")]
    [InlineData("Pledge.Requires(items.All(i => Save(i)));", "calls 'Save', which is not marked [Pure]")]
    public async Task Impure_expressions_are_PLEDGE004(string contract, string expectedMessage)
    {
        var diagnostic = await AnalyzerHarness.SingleAsync(Preamble + $$"""
            public sealed class Holder
            {
                public int Count;
                public string name = "";
                public List<int> items = new();
                public Func<int, bool> predicate = _ => true;

                public bool Save(int v) => true;

                public int M(int x)
                {
                    {{contract}}
                    return x;
                }

                public void Call() => M(1);
            }
            """, "PLEDGE004");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Contract expression is not pure: it " + expectedMessage, diagnostic.Message(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Await_in_a_contract_is_PLEDGE004()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(Preamble + """
            public sealed class Holder
            {
                public async Task<int> M(int x)
                {
                    Pledge.Requires(await Task.FromResult(true));
                    return x;
                }
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "PLEDGE004" && d.Message().Contains("it awaits", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_fix_marks_the_callee_Pure_and_imports_the_namespace_where_needed()
    {
        var result = await AnalyzerHarness.ApplyFixAsync(Preamble + """
            using Policy;

            public sealed class Holder
            {
                public int M(int x)
                {
                    Pledge.Requires(Rules.Allowed(x));
                    return x;
                }
            }
            """, new MarkPureCodeFixProvider(), "PLEDGE004", """
            namespace Policy;

            public static class Rules
            {
                /// <summary>Doc comment stays attached.</summary>
                public static bool Allowed(int value) => value > 0;
            }
            """);

        Assert.Equal("""
            using PledgeContracts;

            namespace Policy;

            public static class Rules
            {
                /// <summary>Doc comment stays attached.</summary>
                [Pure]
                public static bool Allowed(int value) => value > 0;
            }
            """, result["Source1.cs"]);
        Assert.Contains("Pledge.Requires(Rules.Allowed(x));", result["Source0.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fix_reuses_an_existing_using()
    {
        var fixedSource = await AnalyzerHarness.ApplyFixToSingleFileAsync(Preamble + """
            public sealed class Holder
            {
                public bool Allowed(int value) => value > 0;

                public int M(int x)
                {
                    Pledge.Requires(Allowed(x));
                    return x;
                }
            }
            """, new MarkPureCodeFixProvider(), "PLEDGE004");

        Assert.Contains("    [Pure]\n    public bool Allowed(int value) => value > 0;", fixedSource, StringComparison.Ordinal);
        Assert.Equal(1, fixedSource.Split("using PledgeContracts;").Length - 1);
    }

    [Fact]
    public async Task No_fix_for_methods_outside_the_solution()
    {
        var offered = await AnalyzerHarness.OffersFixAsync(Preamble + """
            public sealed class Holder
            {
                public int M(int x)
                {
                    Pledge.Requires(Console.ReadLine() != null);
                    return x;
                }
            }
            """, new MarkPureCodeFixProvider(), "PLEDGE004");

        Assert.False(offered);
    }
}
