using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Redplcs.HighResolutionTimer.Platform.Linux;

internal sealed class WakeableFdTimer : IWaitProvider, IDisposable
{
    private readonly SafeFileDescriptorHandle _cancelHandle;
    private readonly SafeFileDescriptorHandle _disposingHandle;
    
    public WakeableFdTimer()
    {
        var cancelHandle = Interop.eventfd(initval: 0, flags: Interop.EFD_CLOEXEC | Interop.EFD_NONBLOCK);
        if (cancelHandle.IsInvalid)
        {
            var ex = new Win32Exception();
            throw ex;
        }
        
        var disposingHandle = Interop.eventfd(initval: 0, flags: Interop.EFD_CLOEXEC | Interop.EFD_NONBLOCK);
        if (disposingHandle.IsInvalid)
        {
            var ex = new Win32Exception();
            throw ex;
        }
        
        (_cancelHandle, _disposingHandle) = (cancelHandle, disposingHandle);
    }

    public void Dispose()
    {
        _cancelHandle.Dispose();
        _disposingHandle.Dispose();
    }
    
    public WaitResult Wait(TimeSpan timeout, CancellationToken ct, CancellationToken dt)
    {
        using (ct.Register(static s => Signal((SafeFileDescriptorHandle)s!), _cancelHandle))
        using (dt.Register(static s => Signal((SafeFileDescriptorHandle)s!), _disposingHandle))
        {
            bool cancelRef = false, disposingRef = false;
            try
            {
                _cancelHandle.DangerousAddRef(ref cancelRef);
                _disposingHandle.DangerousAddRef(ref disposingRef);
                
                Span<Interop.pollfd> fileDescriptors =
                [
                    new() { fd = (int)_cancelHandle.DangerousGetHandle(), events = Interop.POLLIN },
                    new() { fd = (int)_disposingHandle.DangerousGetHandle(), events = Interop.POLLIN },
                ];
                
                var timespec = new Interop.timespec
                {
                    tv_sec  = (nint)(timeout.Ticks / TimeSpan.TicksPerSecond),
                    tv_nsec = (nint)(timeout.Ticks % TimeSpan.TicksPerSecond * 100)
                };
                
                var rc = Interop.ppoll(
                    fds: fileDescriptors,
                    nfds: (nuint)fileDescriptors.Length,
                    tmo_p: ref timespec,
                    sigmask: 0);

                switch (rc)
                {
                    case 0:
                        return WaitResult.Elapsed;
                    case > 0 when (fileDescriptors[0].revents & Interop.POLLIN) != 0:
                        Drain(_cancelHandle);
                        return WaitResult.Canceled;
                    case > 0 when (fileDescriptors[1].revents & Interop.POLLIN) != 0:
                        Drain(_disposingHandle);
                        return WaitResult.Disposed;
                    case < 0 when Marshal.GetLastPInvokeError() == Interop.EINTR:
                        return WaitResult.Interrupted;
                    case < 0:
                        throw new Win32Exception();
                    default:
                        throw new UnreachableException();
                }
            }
            finally
            {
                if (cancelRef) _cancelHandle.DangerousRelease();
                if (disposingRef) _disposingHandle.DangerousRelease();
            }
        }
    }

    private static void Signal(SafeFileDescriptorHandle handle)
    {
        Retry(handle, Interop.write);
    }

    private static void Drain(SafeFileDescriptorHandle handle)
    {
        Retry(handle, Interop.read);
    }

    private static void Retry(
        SafeFileDescriptorHandle handle,
        Func<SafeFileDescriptorHandle, Span<byte>, nint, int> operation)
    {
        var counter = 1L;
        var buffer = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref counter, 1));
        while (operation(handle, buffer, buffer.Length) < 0)
        {
            var e = Marshal.GetLastPInvokeError();
            switch (e)
            {
                case Interop.EINTR:
                    continue;
                case Interop.EAGAIN:
                    return;
                default:
                    throw new Win32Exception(e);
            }
        }
    }
}