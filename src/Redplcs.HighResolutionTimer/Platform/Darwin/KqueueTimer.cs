using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Redplcs.HighResolutionTimer.Platform.Unix;

namespace Redplcs.HighResolutionTimer.Platform.Darwin;

internal sealed class KqueueTimer : IWaitProvider
{
    private const nuint TimerIdent = 0;
    private const nuint CancelIdent = 1;
    private const nuint DisposingIdent = 2;
    
    private readonly SafeFileDescriptorHandle _handle;
    
    public KqueueTimer()
    {
        var handle = Interop.kqueue();
        if (handle.IsInvalid)
        {
            throw new Win32Exception();
        }
        
        // EV_CLEAR makes user events auto-reset once delivered, so no Drain() is needed.
        ReadOnlySpan<Interop.kevent_t> changes =
        [
            new() { ident = CancelIdent, filter = Interop.EVFILT_USER, flags = Interop.EV_ADD | Interop.EV_CLEAR },
            new() { ident = DisposingIdent, filter = Interop.EVFILT_USER, flags = Interop.EV_ADD | Interop.EV_CLEAR }
        ];

        var rc = Interop.kevent(
            kq: handle,
            changelist: changes,
            nchanges: changes.Length,
            eventlist: [],
            nevents: 0,
            timeout: 0);

        if (rc < 0)
        {
            throw new Win32Exception();
        }
        
        _handle = handle;
    }
    
    public void Dispose()
    {
        _handle.Dispose();
    }

    public void OnPeriodChanged(TimeSpan period)
    {
        // Re-adding an event with the same ident/filter updates it in place,
        // and registrations are allowed while another thread is blocked in kevent().
        ReadOnlySpan<Interop.kevent_t> changes =
        [
            new()
            {
                ident = TimerIdent,
                filter = Interop.EVFILT_TIMER,
                flags = Interop.EV_ADD,
                fflags = Interop.NOTE_NSECONDS | Interop.NOTE_CRITICAL,
                data = (nint)period.TotalNanoseconds
            }
        ];
        
        var rc = Interop.kevent(
            kq: _handle,
            changelist: changes,
            nchanges: changes.Length,
            eventlist: [],
            nevents: 0,
            timeout: 0);

        if (rc < 0)
        {
            throw new Win32Exception();
        }
    }

    public WaitResult Wait(CancellationToken cancellationToken, CancellationToken disposingToken)
    {
        using (cancellationToken.Register(static s => Signal((SafeFileDescriptorHandle)s!, CancelIdent), _handle))
        using (disposingToken.Register(static s => Signal((SafeFileDescriptorHandle)s!, DisposingIdent), _handle))
        {
            Span<Interop.kevent_t> events = stackalloc Interop.kevent_t[3];

            int received;
            while ((received = Interop.kevent(_handle, [], 0, events, events.Length, 0)) < 0)
            {
                if (Marshal.GetLastPInvokeError() != Unix.Interop.EINTR)
                {
                    throw new Win32Exception();
                }
            }

            // A single kevent() call can report several ready events at once, and the
            // kernel does not order them by our priority. The idents are numbered in
            // ascending priority order (Timer=0 < Cancel=1 < Disposing=2), so Math.Max
            // selects the highest-priority pending event: Disposed > Canceled > Elapsed.
            nint best = -1;
            foreach (var e in events[..received])
                if (e.filter is Interop.EVFILT_USER or Interop.EVFILT_TIMER)
                    best = Math.Max(best, (nint)e.ident);

            return (nuint)best switch
            {
                TimerIdent => WaitResult.Elapsed,
                CancelIdent => WaitResult.Canceled,
                DisposingIdent => WaitResult.Disposed,
                _ => throw new UnreachableException()
            };
        }
    }
    
    private static void Signal(SafeFileDescriptorHandle handle, nuint ident)
    {
        ReadOnlySpan<Interop.kevent_t> changes =
        [
            new() { ident = ident, filter = Interop.EVFILT_USER, fflags = Interop.NOTE_TRIGGER },
        ];
 
        while (Interop.kevent(handle, changes, changes.Length, [], 0, IntPtr.Zero) < 0)
        {
            if (Marshal.GetLastPInvokeError() != Unix.Interop.EINTR)
            {
                throw new Win32Exception();
            }
        }
    }
}