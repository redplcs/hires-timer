using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Redplcs.HighResolutionTimer.Platform.Unix;

namespace Redplcs.HighResolutionTimer.Platform.Darwin;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal static partial class Interop
{
    private const string LibraryName = "libSystem.dylib";
    
    // https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/event.h
    internal const short EVFILT_TIMER = -7;
    internal const short EVFILT_USER = -10;
 
    internal const ushort EV_ADD = 0x0001;
    internal const ushort EV_DELETE = 0x0002;
    internal const ushort EV_ONESHOT = 0x0010;
    internal const ushort EV_CLEAR = 0x0020;
    internal const ushort EV_ERROR = 0x4000;
 
    // EVFILT_TIMER fflags: unit of the "data" field and coalescing hints.
    internal const uint NOTE_SECONDS = 0x00000001;
    internal const uint NOTE_USECONDS = 0x00000002;
    internal const uint NOTE_NSECONDS = 0x00000004;
    internal const uint NOTE_ABSOLUTE = 0x00000008;
    internal const uint NOTE_LEEWAY = 0x00000010;
    internal const uint NOTE_CRITICAL = 0x00000020;
    internal const uint NOTE_BACKGROUND = 0x00000040;
    internal const uint NOTE_MACH_CONTINUOUS_TIME = 0x00000080;
    internal const uint NOTE_MACHTIME = 0x00000100;
 
    // EVFILT_USER fflags.
    internal const uint NOTE_TRIGGER = 0x01000000;
 
    [StructLayout(LayoutKind.Sequential)]
    internal struct kevent_t
    {
        internal nuint ident;
        internal short filter;
        internal ushort flags;
        internal uint fflags;
        internal nint data;
        internal IntPtr udata;
    }
 
    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial SafeFileDescriptorHandle kqueue();
 
    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int kevent(
        SafeFileDescriptorHandle kq,
        ReadOnlySpan<kevent_t> changelist,
        int nchanges,
        Span<kevent_t> eventlist,
        int nevents,
        nint timeout);
}