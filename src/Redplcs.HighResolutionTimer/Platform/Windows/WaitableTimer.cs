using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Redplcs.HighResolutionTimer.Platform.Windows;

internal sealed class WaitableTimer : IWaitProvider, IDisposable
{
    private readonly CancellationTokenSource _disposingTokenSource = new();
    private readonly SafeWaitableTimerHandle _handle;
    private readonly SafeWaitHandle _disposingHandle;
    private int _isDisposed;
    
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
        _disposingHandle = _disposingTokenSource.Token.WaitHandle.SafeWaitHandle;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 0)
        {
            _disposingTokenSource.Cancel();
            
            _disposingTokenSource.Dispose();
            _handle.Dispose();
        }
    }
    
    public bool Wait(TimeSpan timeout, CancellationToken ct)
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
            _disposingHandle,
            ct.WaitHandle.SafeWaitHandle,
        ];

        var signaled = Interop.Kernel32.WaitForMultipleObjects(
            nCount: (uint)handles.Length,
            lpHandles: handles,
            bWaitAll: false,
            dwMilliseconds: Interop.INFINITE);

        return signaled switch
        {
            Interop.Kernel32.WAIT_OBJECT_0 => true,
            Interop.Kernel32.WAIT_OBJECT_0 + 1 => false,
            Interop.Kernel32.WAIT_OBJECT_0 + 2 => throw new OperationCanceledException(ct),
            Interop.Kernel32.WAIT_FAILED => throw new Win32Exception(),
            // Should never happen: INFINITE wait without mutexes leaves only the cases above.
            _ => throw new UnreachableException($"Unexpected wait result: 0x{signaled:X8}"),
        };
    }
}