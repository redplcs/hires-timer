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

        if (OperatingSystem.IsLinux() &&
            OperatingSystem.IsOSPlatformVersionAtLeast("Linux", 2, 6, 27))
        {
            return new WakeableFdTimer();
        }

        // The kqueue features used (newest: NOTE_CRITICAL, macOS 10.9 / iOS 7) predate every
        // Apple OS capable of running .NET, so no version check is needed.
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