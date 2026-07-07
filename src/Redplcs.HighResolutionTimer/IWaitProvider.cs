namespace Redplcs.HighResolutionTimer;

internal interface IWaitProvider
{
    WaitResult Wait(TimeSpan timeout, CancellationToken ct, CancellationToken dt);
}