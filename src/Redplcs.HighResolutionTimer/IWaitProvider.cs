namespace Redplcs.HighResolutionTimer;

internal interface IWaitProvider
{
    bool Wait(TimeSpan timeout, CancellationToken ct);
}