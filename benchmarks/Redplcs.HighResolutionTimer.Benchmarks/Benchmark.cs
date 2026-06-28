using BenchmarkDotNet.Attributes;
using JetBrains.Annotations;

namespace Redplcs.HighResolutionTimer.Benchmarks;

[MemoryDiagnoser]
public class Benchmark
{
    private PrecisionTimer _timer = null!;

    [UsedImplicitly]
    [Params(1, 10, 100)] 
    public int Interval;

    [GlobalSetup]
    public void Setup()
    {
        _timer = new PrecisionTimer(TimeSpan.FromMilliseconds(Interval));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _timer.Dispose();
    }
    
    [Benchmark]
    public void WaitForNextTick()
    {
        _timer.WaitForNextTick();
    }
}