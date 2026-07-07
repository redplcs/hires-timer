using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Redplcs.HighResolutionTimer.Platform.Linux;

internal sealed class SafeFileDescriptorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeFileDescriptorHandle() : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        var rc = Interop.close((int)handle);
        return rc == 0 || Marshal.GetLastPInvokeError() == Interop.EINTR;
    }
}