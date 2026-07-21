# Redplcs.HighResolutionTimer

[![NuGet](https://img.shields.io/nuget/v/Redplcs.HighResolutionTimer.svg)](https://www.nuget.org/packages/Redplcs.HighResolutionTimer)

A cross-platform, high-resolution periodic timer for .NET that lets a dedicated thread wait synchronously for timer ticks with microsecond-level accuracy and zero allocations on the wait path.

`PrecisionTimer` is the synchronous counterpart of [`PeriodicTimer`](https://learn.microsoft.com/dotnet/api/system.threading.periodictimer): instead of awaiting ticks on the thread pool, it blocks the calling thread on the most precise kernel waiting primitive available for the current OS. This makes it a good fit for game loops, media and audio pipelines, control loops, and other soft real-time workloads where scheduling jitter matters.

## Features

- **High resolution** — sub-millisecond periods with low jitter; not limited by the default OS timer resolution.
- **Cross-platform** — native waiting primitives on Windows, Linux, and Apple platforms.
- **Zero allocations** — the wait path does not allocate.
- **Drift-free schedule** — ticks are aligned to a fixed timeline rather than measured from the end of the previous tick, so error does not accumulate. Missed ticks are skipped, not buffered.
- **Cooperative cancellation** — `WaitForNextTick` accepts a `CancellationToken`, and `Dispose` interrupts an in-flight wait from another thread.

## Installation

```shell
dotnet add package Redplcs.HighResolutionTimer
```

Requires .NET 10 or later and a supported platform (see [Platform support](#platform-support)).

## Usage

### Basic loop

```csharp
using Redplcs.HighResolutionTimer;

using var timer = new PrecisionTimer(TimeSpan.FromMilliseconds(1));

while (timer.WaitForNextTick())
{
    // Runs once per millisecond until the timer is disposed.
}
```

`WaitForNextTick` blocks until the next tick and returns `true`. When the timer is disposed — including from another thread while a wait is in flight — it returns `false`, which makes `while (timer.WaitForNextTick())` a natural shutdown pattern. Any further use of a disposed timer throws `ObjectDisposedException`.

Ticks fire at multiples of the period from the moment the timer was created (or the period was last changed). If the consumer is late, the next call returns immediately once and subsequent ticks realign to the timeline — missed ticks are never queued up.

The timer is intended for a single consumer: only one call to `WaitForNextTick` may be in flight at a time. `Dispose` is thread-safe and may be called concurrently with an active wait.

## Platform support

`PrecisionTimer` selects a wait provider at construction time:

| Platform | Mechanism |
| --- | --- |
| Windows 10 1803+ | High-resolution waitable timer (`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`) waited on with `WaitForMultipleObjects` |
| Linux (kernel 2.6.27+) | `timerfd` on `CLOCK_MONOTONIC`, with `eventfd` wakeups for cancellation and disposal, multiplexed via `poll` |
| macOS 10.9+, iOS 7+, and any Mac Catalyst, tvOS, or watchOS version | `kqueue` with `EVFILT_TIMER` (`NOTE_CRITICAL`) and `EVFILT_USER` wakeups |

On any other platform the constructor throws `PlatformNotSupportedException`.

## Precision

Sample [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet) results from a [Sleep precision](https://github.com/redplcs/hires-timer/actions/runs/29790866041) workflow run on GitHub-hosted runners (.NET 10.0.10), shown as mean ± standard deviation of the observed tick interval:

| Period | Ubuntu 24.04 (x64) | Windows 11 (x64) | macOS 26 (arm64) |
| ---: | ---: | ---: | ---: |
| 1 ms | 997.6 μs ± 0.7 μs | 998.4 μs ± 0.4 μs | 1,009.0 μs ± 12.7 μs |
| 10 ms | 9,981.0 μs ± 5.7 μs | 9,986.1 μs ± 3.6 μs | 10,003.0 μs ± 73.2 μs |
| 100 ms | 99.72 ms ± 0.17 ms | 99.75 ms ± 0.20 ms | 99.38 ms ± 0.46 ms |

The [Sleep precision](.github/workflows/sleep-precision.yml) workflow runs the benchmark suite on Ubuntu, Windows, and macOS and publishes the results to the job summary. To run the benchmarks locally:

```shell
dotnet run -c Release --project benchmarks/Redplcs.HighResolutionTimer.Benchmarks -- --filter '*'
```

## Improving precision

The accuracy of a tick is ultimately bounded by the OS scheduler: the timer wakes the thread on time, but the thread still has to be scheduled onto a core. The following measures reduce that latency:

- **Run the loop on a dedicated thread.** `WaitForNextTick` blocks its caller, so don't run it on a thread-pool thread — a blocked pool thread degrades the rest of the application, and pool-injected threads carry no scheduling guarantees.
- **Raise the thread priority.** `Thread.Priority = ThreadPriority.Highest` makes the OS scheduler resume the loop ahead of normal-priority work. On Linux, real-time scheduling policies (`SCHED_FIFO` via `chrt` or `sched_setscheduler`) go further but require elevated privileges.
- **Reduce garbage-collector interference.** The wait path itself never allocates, so pauses come from the rest of the application. Keep the tick body allocation-free and consider `GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency` around latency-critical phases.
- **Keep tick work shorter than the period.** Missed ticks are skipped, not buffered — if the body regularly overruns the period, the loop silently runs at a lower effective rate.
- **Mind power management and virtualization.** CPU frequency scaling, deep C-states, and hypervisors all add wake-up jitter; the spread in the [Precision](#precision) table is visibly wider on the virtualized runners. For the best numbers, run on bare metal with a high-performance power plan.

There is no need to raise the system-wide timer resolution on Windows (`timeBeginPeriod`): the high-resolution waitable timer used by the library is not tied to it.
