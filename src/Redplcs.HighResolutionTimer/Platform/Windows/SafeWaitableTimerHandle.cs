using Microsoft.Win32.SafeHandles;

namespace Redplcs.HighResolutionTimer.Platform.Windows;

internal sealed class SafeWaitableTimerHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeWaitableTimerHandle() : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return Interop.Kernel32.CloseHandle(handle);
    }
}