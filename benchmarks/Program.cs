using BenchmarkDotNet.Running;
using Redplcs.HighResolutionTimer.Benchmarks;

BenchmarkRunner.Run<Benchmark>(args: args);