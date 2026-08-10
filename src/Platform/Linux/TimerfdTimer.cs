using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Redplcs.HighResolutionTimer.Platform.Unix;

namespace Redplcs.HighResolutionTimer.Platform.Linux;

internal sealed class TimerfdTimer : IWaitProvider
{
    private const ulong TimerIdent = 0;
    private const ulong CancelIdent = 1;
    private const ulong DisposingIdent = 2;

    private readonly SafeFileDescriptorHandle _epollHandle;
    private readonly SafeFileDescriptorHandle _timerHandle;
    private readonly SafeFileDescriptorHandle _cancelHandle;
    private readonly SafeFileDescriptorHandle _disposingHandle;

    public TimerfdTimer()
    {
        var epollHandle = Interop.epoll_create1(Interop.EPOLL_CLOEXEC);
        if (epollHandle.IsInvalid)
        {
            throw new Win32Exception();
        }
        
        var timerHandle = Interop.timerfd_create(Interop.CLOCK_MONOTONIC, Interop.TFD_CLOEXEC | Interop.TFD_NONBLOCK);
        if (timerHandle.IsInvalid)
        {
            throw new Win32Exception();
        }
        
        var cancelHandle = Interop.eventfd(initval: 0, Interop.EFD_CLOEXEC | Interop.EFD_NONBLOCK);
        if (cancelHandle.IsInvalid)
        {
            throw new Win32Exception();
        }
        
        var disposingHandle = Interop.eventfd(initval: 0, Interop.EFD_CLOEXEC | Interop.EFD_NONBLOCK);
        if (disposingHandle.IsInvalid)
        {
            throw new Win32Exception();
        }

        Register(epollHandle, timerHandle, TimerIdent);
        Register(epollHandle, cancelHandle, CancelIdent);
        Register(epollHandle, disposingHandle, DisposingIdent);
        
        (_epollHandle, _timerHandle, _cancelHandle, _disposingHandle) = (epollHandle, timerHandle, cancelHandle, disposingHandle);
    }

    private static void Register(SafeFileDescriptorHandle epollHandle, SafeFileDescriptorHandle fd, ulong ident)
    {
        var @event = new Interop.epoll_event
        {
            events = Interop.EPOLLIN | Interop.EPOLLET,
            data = ident
        };

        var rc = Interop.epoll_ctl(
            epfd: epollHandle,
            op: Interop.EPOLL_CTL_ADD,
            fd,
            @event: @event);

        if (rc < 0)
        {
            throw new Win32Exception();
        }
    }

    public void Dispose()
    {
        _timerHandle.Dispose();
        _cancelHandle.Dispose();
        _disposingHandle.Dispose();
        _epollHandle.Dispose();
    }

    public void OnPeriodChanged(TimeSpan period)
    {
        var timespec = new Interop.timespec
        {
            tv_sec = (nint)period.TotalSeconds,
            tv_nsec = (nint)(period.Ticks % TimeSpan.TicksPerSecond * TimeSpan.NanosecondsPerTick)
        };
            
        var itimerspec = new Interop.itimerspec
        {
            it_interval = timespec,
            it_value = timespec
        };
        
        // timerfd_settime() may be called while another thread is blocked in epoll_wait(),
        // and rearming resets the expiration counter, so an unconsumed expiration from the
        // previous period cannot surface as a stale wakeup afterwards.
        var armed = Interop.timerfd_settime(
            fd: _timerHandle,
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
            Span<Interop.epoll_event> events = stackalloc Interop.epoll_event[3];

            int received;
            while ((received = Interop.epoll_wait(_epollHandle, events, events.Length, timeout: -1)) < 0)
            {
                if (Marshal.GetLastPInvokeError() != Unix.Interop.EINTR)
                {
                    throw new Win32Exception();
                }
            }

            // A single epoll_wait() call can report several ready events at once, and the
            // kernel does not order them by our priority. The idents are numbered in
            // ascending priority order (Timer=0 < Cancel=1 < Disposing=2), so Math.Max
            // selects the highest-priority pending event: Disposed > Canceled > Elapsed.
            var best = -1L;
            foreach (var e in events[..received])
            {
                Drain(e.data switch
                {
                    TimerIdent => _timerHandle,
                    CancelIdent => _cancelHandle,
                    DisposingIdent => _disposingHandle,
                    _ => throw new UnreachableException()
                });
                best = Math.Max(best, (long)e.data);
            }
            
            // ReSharper disable once IntVariableOverflowInUncheckedContext
            return (ulong)best switch
            {
                TimerIdent => WaitResult.Elapsed,
                CancelIdent => WaitResult.Canceled,
                DisposingIdent => WaitResult.Disposed,
                _ => throw new UnreachableException()
            };
        }
    }

    private static void Signal(SafeFileDescriptorHandle handle)
    {
        // write() adds the value to the eventfd counter, and only a nonzero counter
        // makes the descriptor readable, so the value must be at least 1.
        var counter = 1UL;
        var buffer = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref counter, 1));
        while (Interop.write(handle, buffer, buffer.Length) < 0)
        {
            if (Marshal.GetLastPInvokeError() != Unix.Interop.EINTR)
            {
                throw new Win32Exception();
            }
        }
    }

    private static void Drain(SafeFileDescriptorHandle handle)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        while (Interop.read(handle, buffer, buffer.Length) < 0)
        {
            switch (Marshal.GetLastPInvokeError())
            {
                case Unix.Interop.EINTR:
                    continue;
                case Interop.EAGAIN:
                    return;
                default:
                    throw new Win32Exception();
            }
        }
    }
}