using BenchmarkDotNet.Attributes;

namespace PledgeContracts.Benchmarks;

// One category per row of the performance budget in DESIGN.md. The amounts always satisfy the contracts: the budget
// is about the cost of a check that passes, since a broken contract is a bug, not a hot path.
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ContractBenchmarks
{
    private readonly Counter _counter = new();

    [Benchmark(Baseline = true), BenchmarkCategory("Precondition")]
    public void PreconditionHandWritten() => _counter.AddHandWritten(1);

    [Benchmark, BenchmarkCategory("Precondition")]
    public void PreconditionPledge() => _counter.Add(1);

    [Benchmark(Baseline = true), BenchmarkCategory("Postcondition with Old")]
    public void PostconditionHandWritten() => _counter.AddCheckedHandWritten(1);

    [Benchmark, BenchmarkCategory("Postcondition with Old")]
    public void PostconditionPledge() => _counter.AddChecked(1);

    [Benchmark(Baseline = true), BenchmarkCategory("Async")]
    public Task<int> AsyncHandWritten() => _counter.AddAsyncHandWritten(1);

    [Benchmark, BenchmarkCategory("Async")]
    public Task<int> AsyncPledge() => _counter.AddAsync(1);
}
