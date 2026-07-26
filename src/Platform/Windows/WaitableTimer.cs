using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Redplcs.HighResolutionTimer.Platform.Windows;

internal sealed class WaitableTimer : IWaitProvider
{
    private readonly SafeWaitableTimerHandle _handle;
    private readonly TimeProvider _timeProvider;
    private TimeSpan _period;
    private long _originTimestamp;
    private long _nextDeadline;
    
    internal WaitableTimer(TimeProvider timeProvider)
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
        _timeProvider = timeProvider;
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    public void OnPeriodChanged(TimeSpan period)
    {
        _period = period;
        _originTimestamp = _timeProvider.GetTimestamp();
        _nextDeadline = period.Ticks;
    }
    
    public WaitResult Wait(CancellationToken cancellationToken, CancellationToken disposingToken)
    {
        var period = _period.Ticks;
        var elapsed = _timeProvider.GetElapsedTime(_originTimestamp).Ticks;

        if (elapsed >= _nextDeadline)
        {
            if (disposingToken.IsCancellationRequested) return WaitResult.Disposed;
            if (cancellationToken.IsCancellationRequested) return WaitResult.Canceled;
            
            _nextDeadline = (elapsed / period + 1) * period;
            return WaitResult.Elapsed;
        }
        
        var remaining = -(_nextDeadline - elapsed);
        
        var armed = Interop.Kernel32.SetWaitableTimerEx(
            hTimer: _handle,
            lpDueTime: ref remaining,
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
            disposingToken.WaitHandle.SafeWaitHandle,
            cancellationToken.WaitHandle.SafeWaitHandle,
            _handle,
        ];

        var signaled = Interop.Kernel32.WaitForMultipleObjects(
            nCount: (uint)handles.Length,
            lpHandles: handles,
            bWaitAll: false,
            dwMilliseconds: Interop.INFINITE);

        switch (signaled)
        {
            case Interop.Kernel32.WAIT_OBJECT_0:
                return WaitResult.Disposed;
            case Interop.Kernel32.WAIT_OBJECT_0 + 1:
                return WaitResult.Canceled;
            case Interop.Kernel32.WAIT_OBJECT_0 + 2:
                _nextDeadline += period;
                return WaitResult.Elapsed;
            case Interop.Kernel32.WAIT_FAILED:
                throw new Win32Exception();
            default:
                // Should never happen: INFINITE wait without mutexes leaves only the cases above.
                throw new UnreachableException();
        }
    }
}