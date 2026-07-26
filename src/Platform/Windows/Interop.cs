using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Redplcs.HighResolutionTimer.Platform.Windows;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal static partial class Interop
{
    internal const uint INFINITE = 0xFFFFFFFF;
    
    internal static partial class Kernel32
    {
        private const string LibraryName = "kernel32.dll";
        
        internal const uint CREATE_WAITABLE_TIMER_MANUAL_RESET = 0x00000001;
        internal const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;

        // https://learn.microsoft.com/en-us/windows/win32/sync/synchronization-object-security-and-access-rights
        internal const uint TIMER_ALL_ACCESS = 0x1F0003;
        internal const uint TIMER_MODIFY_STATE = 0x0002;
        internal const uint TIMER_QUERY_STATE = 0x0001;

        // https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-waitforsingleobject
        internal const uint WAIT_OBJECT_0 = 0x00000000;
        internal const uint WAIT_FAILED = 0xFFFFFFFF;
    
        [LibraryImport(LibraryName, EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
        internal static partial SafeWaitableTimerHandle CreateWaitableTimerEx(
            IntPtr lpTimerAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string? lpTimerName,
            uint dwFlags,
            uint dwDesiredAccess);

        [LibraryImport(LibraryName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetWaitableTimerEx(
            SafeWaitableTimerHandle hTimer,
            ref long lpDueTime,
            int lPeriod,
            PTIMERAPCROUTINE? pfnCompletionRoutine,
            IntPtr lpArgToCompletionRoutine,
            IntPtr WakeContext,
            uint TolerableDelay);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint WaitForMultipleObjects(
            uint nCount,
            ReadOnlySpan<SafeHandle> lpHandles,
            [MarshalAs(UnmanagedType.Bool)] bool bWaitAll,
            uint dwMilliseconds)
        {
            Debug.Assert(nCount <= 64 /* MAXIMUM_WAIT_OBJECTS */);
            
            Span<IntPtr> natives = stackalloc IntPtr[(int)nCount];
            Span<bool> references = stackalloc bool[(int)nCount];

            try
            {
                for (var i = 0; i < nCount; i++)
                {
                    lpHandles[i].DangerousAddRef(ref references[i]);
                    natives[i] = lpHandles[i].DangerousGetHandle();
                }

                return WaitForMultipleObjects(nCount, natives, bWaitAll, dwMilliseconds);
            }
            finally
            {
                for (var i = 0; i < nCount; i++)
                {
                    if (references[i])
                    {
                        lpHandles[i].DangerousRelease();
                    }
                }
            }
        }
        
        [LibraryImport(LibraryName, SetLastError = true)]
        private static partial uint WaitForMultipleObjects(
            uint nCount,
            ReadOnlySpan<IntPtr> lpHandles,
            [MarshalAs(UnmanagedType.Bool)] bool bWaitAll,
            uint dwMilliseconds);

        [LibraryImport(LibraryName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr hObject);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void PTIMERAPCROUTINE(
            IntPtr lpArgToCompletionRoutine,
            uint dwTimerLowValue,
            uint dwTimerHighValue);
    }
}