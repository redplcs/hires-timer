using System.Diagnostics;

namespace Redplcs.HighResolutionTimer;

/// <summary>
/// Provides a high-resolution periodic timer that enables waiting synchronously for timer ticks.
/// </summary>
/// <remarks>
/// <para>
/// Ticks are scheduled on a fixed timeline that starts when the timer is created or when <see cref="Period"/>
/// is changed. If a tick is missed because the caller was busy, the next call to
/// <see cref="WaitForNextTick(CancellationToken)"/> returns immediately and subsequent ticks realign to the
/// timeline; missed ticks are not buffered.
/// </para>
/// <para>
/// This timer is intended to be used by a single consumer at a time: only one call to
/// <see cref="WaitForNextTick(CancellationToken)"/> may be in flight at any given moment.
/// <see cref="Dispose"/> may be used concurrently with an active <see cref="WaitForNextTick(CancellationToken)"/>
/// to interrupt it and cause it to return <see langword="false"/>.
/// </para>
/// </remarks>
public sealed class PrecisionTimer : IDisposable
{
    private readonly CancellationTokenSource _disposingTokenSource = new();
    private readonly IWaitProvider _waitProvider;
    private readonly TimeProvider _timeProvider;
    private int _isDisposed;

    /// <summary>Initializes a new instance of the <see cref="PrecisionTimer"/> class with the specified period.</summary>
    /// <param name="period">The period between ticks.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="PlatformNotSupportedException">The current platform does not provide a supported high-resolution waiting mechanism.</exception>
    public PrecisionTimer(TimeSpan period) : this(period, TimeProvider.System)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PrecisionTimer"/> class with the specified period and time provider.</summary>
    /// <param name="period">The period between ticks.</param>
    /// <param name="timeProvider">The <see cref="TimeProvider"/> used to measure elapsed time.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="PlatformNotSupportedException">The current platform does not provide a supported high-resolution waiting mechanism.</exception>
    public PrecisionTimer(TimeSpan period, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _waitProvider = WaitProviderFactory.Build(timeProvider);
        _timeProvider = timeProvider;
        
        Period = period;
    }

    /// <summary>Gets or sets the period between ticks.</summary>
    /// <value>The interval at which the timer ticks.</value>
    /// <remarks>Setting this property restarts the tick timeline: the next tick occurs one full period after the change.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than or equal to <see cref="TimeSpan.Zero"/>.</exception>
    /// <exception cref="ObjectDisposedException">The timer has been disposed.</exception>
    public TimeSpan Period
    {
        get;
        set
        {
            ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
            _waitProvider.OnPeriodChanged(value);
        }
    }

    /// <summary>Stops the timer and releases all resources used by it.</summary>
    /// <remarks>
    /// Disposing the timer causes an active call to <see cref="WaitForNextTick(CancellationToken)"/> to return
    /// <see langword="false"/>. All subsequent calls on the timer throw <see cref="ObjectDisposedException"/>.
    /// This method is thread-safe and may be called multiple times.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
        {
            _disposingTokenSource.Cancel();
            
            _waitProvider.Dispose();
            _disposingTokenSource.Dispose();
        }
    }

    /// <summary>Wait for the next tick of the timer, or for the timer to be stopped.</summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for the next tick.</param>
    /// <returns><see langword="true"/> if the timer's period elapsed; <see langword="false"/> if the timer was disposed while waiting.</returns>
    /// <remarks>
    /// If the next tick on the timeline has already passed when this method is called, it returns immediately
    /// and the following tick is realigned to the next multiple of <see cref="Period"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Cancellation was requested via <paramref name="cancellationToken"/>.</exception>
    /// <exception cref="ObjectDisposedException">The timer has been disposed before this call.</exception>
    public bool WaitForNextTick(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
        
        return _waitProvider.Wait(cancellationToken, _disposingTokenSource.Token) switch
        {
            WaitResult.Elapsed => true,
            WaitResult.Disposed => false,
            WaitResult.Canceled => throw new OperationCanceledException(cancellationToken),
            _ => throw new UnreachableException()
        };
    }
}