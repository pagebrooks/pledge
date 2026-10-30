using Microsoft.CodeAnalysis;
using PledgeContracts.Roslyn.CodeFixes;

namespace PledgeContracts.Roslyn.Tests;

public sealed class MutableOldAnalyzerTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Linq;
        using PledgeContracts;

        public sealed record Point(int X, int Y);
        public sealed record Cursor { public int X { get; set; } }
        public sealed class Bag { public int Count; }
        public sealed class Frozen { public int Count { get; } public Frozen(int c) => Count = c; }

        """;

    [Theory]
    [InlineData("List<int> Items", "Pledge.Ensures(Pledge.Old(Items).Count == Items.Count);", "List<int>", "ToArray")]
    [InlineData("int[] Items", "Pledge.Ensures(Pledge.Old(Items).Length == Items.Length);", "int[]", "ToArray")]
    [InlineData("IReadOnlyList<int> Items", "Pledge.Ensures(Pledge.Old(Items).Count == Items.Count);", "IReadOnlyList<int>", "ToArray")]
    [InlineData("IEnumerable<int> Items", "Pledge.Ensures(Pledge.Old(Items).Count() == Items.Count());", "IEnumerable<int>", "ToArray")]
    [InlineData("Cursor Items", "Pledge.Ensures(Pledge.Old(Items).X == Items.X);", "Cursor", "With")]
    [InlineData("Bag Items", "Pledge.Ensures(Pledge.Old(Items).Count == Items.Count);", "Bag", "")]
    [InlineData("Bag Items", "Pledge.Ensures(Pledge.Old(Items).Equals(Items));", "Bag", "")]
    public async Task Old_of_a_mutable_reference_is_PLEDGE007(string member, string contract, string expectedType, string expectedCopy)
    {
        var diagnostic = await AnalyzerHarness.SingleAsync(Preamble + $$"""
            public sealed class Holder
            {
                public {{member}} = default!;

                public void M()
                {
                    {{contract}}
                }
            }
            """, "PLEDGE007");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains($"Pledge.Old(Items) snapshots a reference to the mutable type '{expectedType}'", diagnostic.Message(), StringComparison.Ordinal);
        Assert.Equal(expectedCopy, diagnostic.Properties["Copy"]);
    }

    [Theory]
    [InlineData("int Items", "Pledge.Ensures(Pledge.Old(Items) == Items);")]
    [InlineData("string Items", "Pledge.Ensures(Pledge.Old(Items) == Items);")]
    [InlineData("Point Items", "Pledge.Ensures(Pledge.Old(Items) == Items);")]
    [InlineData("Frozen Items", "Pledge.Ensures(Pledge.Old(Items).Count == Items.Count);")]
    [InlineData("ImmutableArray<int> Items", "Pledge.Ensures(Pledge.Old(Items).Length == Items.Length);")]
    [InlineData("ImmutableList<int> Items", "Pledge.Ensures(Pledge.Old(Items).Count == Items.Count);")]
    [InlineData("List<int> Items", "Pledge.Ensures(Pledge.Old(Items.ToArray()).Length == Items.Count);")]
    [InlineData("List<int> Items", "Pledge.Ensures(Pledge.Old(Items.Count) == Items.Count);")]
    [InlineData("List<int> Items", "Pledge.Ensures(Pledge.Old(new List<int>(Items)).Count == Items.Count);")]
    [InlineData("Cursor Items", "Pledge.Ensures(Pledge.Old(Items with { }).X == Items.X);")]
    [InlineData("Bag Items", "Pledge.Ensures(Pledge.Old(Items) == Items);")]
    [InlineData("Bag Items", "Pledge.Ensures(ReferenceEquals(Pledge.Old(Items), Items));")]
    [InlineData("Bag Items", "Pledge.Ensures(Pledge.Old(Items) is not null);")]
    [InlineData("Func<int> Items", "Pledge.Ensures(Pledge.Old(Items) == Items);")]
    public async Task Value_types_immutable_types_copies_and_identity_comparisons_are_silent(string member, string contract)
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(Preamble + $$"""
            public sealed class Holder
            {
                public {{member}} = default!;

                public void M()
                {
                    {{contract}}
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_fix_copies_a_sequence_with_ToArray()
    {
        var fixedSource = await AnalyzerHarness.ApplyFixToSingleFileAsync("""
            using System.Collections.Generic;
            using PledgeContracts;

            public sealed class Holder
            {
                public IReadOnlyList<int> Items = [];

                public void M()
                {
                    Pledge.Ensures(Pledge.Old(Items).Count == Items.Count);
                }
            }
            """, new CopyOldValueCodeFixProvider(), "PLEDGE007");

        Assert.Contains("Pledge.Ensures(Pledge.Old(Items.ToArray()).Count == Items.Count);", fixedSource, StringComparison.Ordinal);
        Assert.Contains("using System.Linq;", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fix_copies_a_record_with_a_with_expression()
    {
        var fixedSource = await AnalyzerHarness.ApplyFixToSingleFileAsync("""
            using PledgeContracts;

            public sealed record Cursor { public int X { get; set; } }

            public sealed class Holder
            {
                public Cursor Position = new();

                public void M()
                {
                    Pledge.Ensures(Pledge.Old(Position).X == Position.X);
                }
            }
            """, new CopyOldValueCodeFixProvider(), "PLEDGE007");

        Assert.Contains("Pledge.Ensures(Pledge.Old(Position with { }).X == Position.X);", fixedSource, StringComparison.Ordinal);
    }
}
