using Redplcs.HighResolutionTimer.Platform.Windows;

namespace Redplcs.HighResolutionTimer;

internal static class WaitProviderFactory
{
    internal static IWaitProvider Build()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
        {
            return new WaitableTimer();
        }
        
        throw new PlatformNotSupportedException();
    }
}