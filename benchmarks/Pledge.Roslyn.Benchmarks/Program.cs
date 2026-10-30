using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// In process: BenchmarkDotNet's default toolchain generates and builds a project under the output folder, which
// would pick up this repository's Directory.Build.props and its warnings-as-errors.
var config = DefaultConfig.Instance.AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance));
BenchmarkSwitcher.FromAssembly(typeof(PledgeContracts.Roslyn.Benchmarks.GeneratorBenchmarks).Assembly).Run(args, config);
