using System.Diagnostics;

namespace Redplcs.HighResolutionTimer;

public sealed class PrecisionTimer : IDisposable
{
    private readonly CancellationTokenSource _disposingTokenSource = new();
    private readonly IWaitProvider _waitProvider;
    private readonly TimeProvider _timeProvider;
    private int _isDisposed;

    public PrecisionTimer(TimeSpan period) : this(period, TimeProvider.System)
    {
    }

    public PrecisionTimer(TimeSpan period, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _waitProvider = WaitProviderFactory.Build(timeProvider);
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
            field = value;
            _waitProvider.OnPeriodChanged(value);
        }
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
        {
            _disposingTokenSource.Cancel();
            
            _waitProvider.Dispose();
            _disposingTokenSource.Dispose();
        }
    }

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