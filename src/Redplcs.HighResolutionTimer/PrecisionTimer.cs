using System.Diagnostics;

namespace Redplcs.HighResolutionTimer;

public sealed class PrecisionTimer : IDisposable
{
    private readonly CancellationTokenSource _disposingTokenSource = new();
    private readonly IWaitProvider _waitProvider;
    private readonly TimeProvider _timeProvider;
    private long _origin;
    private int _isDisposed;

    public PrecisionTimer(TimeSpan period) : this(period, TimeProvider.System)
    {
    }

    public PrecisionTimer(TimeSpan period, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _waitProvider = WaitProviderFactory.Build();
        _timeProvider = timeProvider;
        
        Period = period;
    }

    public TimeSpan Period
    {
        get;
        set
        {
            ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _origin = _timeProvider.GetTimestamp();
            field = value;
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
        {
            _disposingTokenSource.Cancel();
            
            if (_waitProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            _disposingTokenSource.Dispose();
        }
    }

    public bool WaitForNextTick(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);
        
        var period = Period;

        while (true)
        {
            var now = _timeProvider.GetTimestamp();
            var elapsed = _timeProvider.GetElapsedTime(_origin, now);
            var nextTickCount = elapsed.Ticks / period.Ticks + 1;
            var nextDeadline = nextTickCount * period.Ticks;
            var remaining = new TimeSpan(nextDeadline - elapsed.Ticks);
            
            var result = _waitProvider.Wait(remaining, cancellationToken, _disposingTokenSource.Token);

            switch (result)
            {
                case WaitResult.Interrupted:
                    continue;
                case WaitResult.Elapsed:
                    return true;
                case WaitResult.Disposed:
                    return false;
                case WaitResult.Canceled:
                    throw new OperationCanceledException(cancellationToken);
                default:
                    throw new UnreachableException();
            }
        }
    }
}