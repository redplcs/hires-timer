namespace Redplcs.HighResolutionTimer;

internal interface IWaitProvider : IDisposable
{
    WaitResult Wait(CancellationToken cancellationToken, CancellationToken disposingToken);
    void OnPeriodChanged(TimeSpan period);
}