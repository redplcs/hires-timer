using Redplcs.HighResolutionTimer.Platform.Darwin;
using Redplcs.HighResolutionTimer.Platform.Linux;
using Redplcs.HighResolutionTimer.Platform.Windows;

namespace Redplcs.HighResolutionTimer;

internal static class WaitProviderFactory
{
    internal static IWaitProvider Build(TimeProvider timeProvider)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
        {
            return new WaitableTimer(timeProvider);
        }

        if (OperatingSystem.IsLinux())
        {
            return new WakeableFdTimer();
        }

        if (OperatingSystem.IsMacOS() ||
            OperatingSystem.IsMacCatalyst() ||
            OperatingSystem.IsIOS() ||
            OperatingSystem.IsTvOS() ||
            OperatingSystem.IsWatchOS())
        {
            return new KqueueTimer();
        }
        
        throw new PlatformNotSupportedException();
    }
}