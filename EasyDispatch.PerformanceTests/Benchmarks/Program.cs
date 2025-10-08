using BenchmarkDotNet.Running;

// Run all benchmarks
var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// Or run specific benchmark:
// BenchmarkRunner.Run<HandlerExecutionBenchmarks>();
// BenchmarkRunner.Run<NotificationStrategyBenchmarks>();
// BenchmarkRunner.Run<StreamingQueryBenchmarks>();
// BenchmarkRunner.Run<BehaviorOverheadBenchmarks>();
// BenchmarkRunner.Run<ColdStartBenchmarks>();