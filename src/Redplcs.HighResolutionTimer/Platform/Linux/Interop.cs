#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Redplcs.HighResolutionTimer.Platform.Linux;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal static partial class Interop
{
    private const string LibraryName = "libc";
    
    internal const int CLOCK_REALTIME = 0;
    internal const int CLOCK_MONOTONIC = 1;
    internal const int CLOCK_PROCESS_CPUTIME_ID = 2;
    internal const int CLOCK_THREAD_CPUTIME_ID = 3;
    internal const int CLOCK_MONOTONIC_RAW = 4;
    internal const int CLOCK_REALTIME_COARSE = 5;
    internal const int CLOCK_MONOTONIC_COARSE = 6;
    internal const int CLOCK_BOOTTIME = 7;
    internal const int CLOCK_REALTIME_ALARM = 8;
    internal const int CLOCK_BOOTTIME_ALARM = 9;
    
    internal const int TFD_CLOEXEC = 0x80000;
    internal const int TFD_NONBLOCK = 0x800;
    internal const int TFD_TIMER_ABSTIME = 1;
    
    internal const int EFD_CLOEXEC = TFD_CLOEXEC;
    internal const int EFD_NONBLOCK = TFD_NONBLOCK;
    
    internal const int POLLIN = 0x001;
    
    internal const int EINTR = 4;
    internal const int EAGAIN = 11;

    [StructLayout(LayoutKind.Sequential)]
    internal struct pollfd
    {
        internal int fd;
        internal short events;
        internal short revents;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct timespec
    {
        internal nint tv_sec;
        internal nint tv_nsec;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    internal struct itimerspec
    {
        internal timespec it_interval;
        internal timespec it_value;
    }

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial SafeFileDescriptorHandle timerfd_create(int clockid, int flags);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int timerfd_settime(SafeFileDescriptorHandle fd, int flags, in itimerspec new_value, out itimerspec old_value);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial SafeFileDescriptorHandle eventfd(uint initval, int flags);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int poll(Span<pollfd> fds, nuint nfds, int timeout);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int read(SafeFileDescriptorHandle fd, Span<byte> buf, nint count);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int write(SafeFileDescriptorHandle fd, Span<byte> buf, nint count);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int close(int fd);
}
