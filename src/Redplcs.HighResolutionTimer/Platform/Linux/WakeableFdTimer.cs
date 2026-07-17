using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Redplcs.HighResolutionTimer.Platform.Linux;

internal sealed class WakeableFdTimer : IWaitProvider
{
    private readonly SafeFileDescriptorHandle _handle;
    private readonly SafeFileDescriptorHandle _cancelHandle;
    private readonly SafeFileDescriptorHandle _disposingHandle;
    
    public WakeableFdTimer()
    {
        var handle = Interop.timerfd_create(clockid: Interop.CLOCK_MONOTONIC, flags: Interop.TFD_CLOEXEC);
        if (handle.IsInvalid)
        {
            throw new Win32Exception();
        }
        
        var cancelHandle = Interop.eventfd(initval: 0, flags: Interop.EFD_CLOEXEC | Interop.EFD_NONBLOCK);
        if (cancelHandle.IsInvalid)
        {
            throw new Win32Exception();
        }
        
        var disposingHandle = Interop.eventfd(initval: 0, flags: Interop.EFD_CLOEXEC | Interop.EFD_NONBLOCK);
        if (disposingHandle.IsInvalid)
        {
            throw new Win32Exception();
        }
        
        (_handle, _cancelHandle, _disposingHandle) = (handle, cancelHandle, disposingHandle);
    }

    public void Dispose()
    {
        _handle.Dispose();
        _cancelHandle.Dispose();
        _disposingHandle.Dispose();
    }

    public void OnPeriodChanged(TimeSpan period)
    {
        var timespec = new Interop.timespec
        {
            tv_sec = (nint)(period.Ticks / TimeSpan.TicksPerSecond),
            tv_nsec = (nint)(period.Ticks % TimeSpan.TicksPerSecond * 100)
        };
            
        var itimerspec = new Interop.itimerspec
        {
            it_interval = timespec,
            it_value = timespec,
        };
        
        var armed = Interop.timerfd_settime(
            fd: _handle,
            flags: 0,
            new_value: itimerspec,
            old_value: out _);

        if (armed < 0)
        {
            throw new Win32Exception();
        }
    }

    public WaitResult Wait(CancellationToken cancellationToken, CancellationToken disposingToken)
    {
        using (cancellationToken.Register(static s => Signal((SafeFileDescriptorHandle)s!), _cancelHandle))
        using (disposingToken.Register(static s => Signal((SafeFileDescriptorHandle)s!), _disposingHandle))
        {
            bool handleRef = false, cancelRef = false, disposingRef = false;
            try
            {
                _handle.DangerousAddRef(ref handleRef);
                _cancelHandle.DangerousAddRef(ref cancelRef);
                _disposingHandle.DangerousAddRef(ref disposingRef);
                
                Span<Interop.pollfd> fileDescriptors =
                [
                    new() { fd = (int)_handle.DangerousGetHandle(), events = Interop.POLLIN },
                    new() { fd = (int)_cancelHandle.DangerousGetHandle(), events = Interop.POLLIN },
                    new() { fd = (int)_disposingHandle.DangerousGetHandle(), events = Interop.POLLIN },
                ];

                while (true)
                {
                    var rc = Interop.poll(
                        fds: fileDescriptors,
                        nfds: (nuint)fileDescriptors.Length,
                        timeout: -1);
                    
                    switch (rc)
                    {
                        case > 0 when (fileDescriptors[2].revents & Interop.POLLIN) != 0:
                            Drain(_disposingHandle);
                            return WaitResult.Disposed;
                        case > 0 when (fileDescriptors[1].revents & Interop.POLLIN) != 0:
                            Drain(_cancelHandle);
                            return WaitResult.Canceled;
                        case > 0 when (fileDescriptors[0].revents & Interop.POLLIN) != 0:
                            Drain(_handle);
                            return WaitResult.Elapsed;
                        case < 0 when Marshal.GetLastPInvokeError() == Interop.EINTR:
                            continue;
                        case < 0:
                            throw new Win32Exception();
                        default:
                            throw new UnreachableException();
                    }
                }
            }
            finally
            {
                if (handleRef) _handle.DangerousRelease();
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