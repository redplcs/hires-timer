using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Redplcs.HighResolutionTimer.Platform.Windows;

internal sealed class WaitableTimer : IWaitProvider, IDisposable
{
    private readonly SafeWaitableTimerHandle _handle;
    
    internal WaitableTimer()
    {
        var handle = Interop.Kernel32.CreateWaitableTimerEx(
            lpTimerAttributes: IntPtr.Zero,
            lpTimerName: null,
            dwFlags: Interop.Kernel32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION | Interop.Kernel32.CREATE_WAITABLE_TIMER_MANUAL_RESET,
            dwDesiredAccess: Interop.Kernel32.TIMER_ALL_ACCESS);
        
        if (handle.IsInvalid)
        {
            throw new Win32Exception();
        }
        
        _handle = handle;
    }

    public void Dispose()
    {
        _handle.Dispose();
    }
    
    public WaitResult Wait(TimeSpan timeout, CancellationToken ct, CancellationToken dt)
    {
        var dueTime = -timeout.Ticks;

        var armed = Interop.Kernel32.SetWaitableTimerEx(
            hTimer: _handle,
            lpDueTime: ref dueTime,
            lPeriod: 0,
            pfnCompletionRoutine: null,
            lpArgToCompletionRoutine: IntPtr.Zero,
            WakeContext: IntPtr.Zero,
            TolerableDelay: 0);
        
        if (!armed)
        {
            throw new Win32Exception();
        }

        ReadOnlySpan<SafeHandle> handles =
        [
            _handle,
            ct.WaitHandle.SafeWaitHandle,
            dt.WaitHandle.SafeWaitHandle,
        ];

        var signaled = Interop.Kernel32.WaitForMultipleObjects(
            nCount: (uint)handles.Length,
            lpHandles: handles,
            bWaitAll: false,
            dwMilliseconds: Interop.INFINITE);

        return signaled switch
        {
            Interop.Kernel32.WAIT_OBJECT_0 => WaitResult.Elapsed,
            Interop.Kernel32.WAIT_OBJECT_0 + 1 => WaitResult.Canceled,
            Interop.Kernel32.WAIT_OBJECT_0 + 2 => WaitResult.Disposed,
            Interop.Kernel32.WAIT_FAILED => throw new Win32Exception(),
            // Should never happen: INFINITE wait without mutexes leaves only the cases above.
            _ => throw new UnreachableException(),
        };
    }
}