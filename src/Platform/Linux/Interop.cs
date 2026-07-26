#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Redplcs.HighResolutionTimer.Platform.Unix;

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

    internal const int EPOLL_CLOEXEC = TFD_CLOEXEC;

    internal const int EPOLL_CTL_ADD = 1;

    internal const uint EPOLLIN = 0x001;
    internal const uint EPOLLET = 0x80000000;

    // The kernel ABI declares struct epoll_event __attribute__((packed)) on x86/x86-64
    // (12 bytes, data at offset 4) but uses natural alignment everywhere else (16 bytes,
    // data at offset 8). epoll_event below is the natural layout; the epoll_ctl and
    // epoll_wait wrappers translate to the packed twin at runtime.
    private static readonly bool EpollEventPacked =
        RuntimeInformation.ProcessArchitecture is Architecture.X86 or Architecture.X64;
    
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct epoll_event
    {
        internal uint events;
        internal ulong data;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct epoll_event_packed
    {
        internal uint events;
        internal ulong data;
    }

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial SafeFileDescriptorHandle timerfd_create(int clockid, int flags);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int timerfd_settime(SafeFileDescriptorHandle fd, int flags, in itimerspec new_value, out itimerspec old_value);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial SafeFileDescriptorHandle eventfd(uint initval, int flags);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial SafeFileDescriptorHandle epoll_create1(int flags);

    internal static int epoll_ctl(SafeFileDescriptorHandle epfd, int op, SafeFileDescriptorHandle fd, in epoll_event @event)
    {
        return EpollEventPacked
            ? epoll_ctl_packed(epfd, op, fd, new epoll_event_packed { events = @event.events, data = @event.data })
            : epoll_ctl_aligned(epfd, op, fd, in @event);
    }

    [LibraryImport(LibraryName, EntryPoint = "epoll_ctl", SetLastError = true)]
    private static partial int epoll_ctl_packed(SafeFileDescriptorHandle epfd, int op, SafeFileDescriptorHandle fd, in epoll_event_packed @event);

    [LibraryImport(LibraryName, EntryPoint = "epoll_ctl", SetLastError = true)]
    private static partial int epoll_ctl_aligned(SafeFileDescriptorHandle epfd, int op, SafeFileDescriptorHandle fd, in epoll_event @event);

    internal static int epoll_wait(SafeFileDescriptorHandle epfd, Span<epoll_event> events, int n, int timeout)
    {
        if (!EpollEventPacked)
        {
            return epoll_wait_aligned(epfd, events, n, timeout);
        }

        var packed = MemoryMarshal.Cast<epoll_event, epoll_event_packed>(events);
        var received = epoll_wait_packed(epfd, packed, n, timeout);

        // The kernel filled the buffer with 12-byte entries; widen them to the 16-byte
        // layout in place. Iterating backwards keeps not-yet-copied packed entries from
        // being overwritten by the wider destination slots.
        for (var i = received - 1; i >= 0; i--)
        {
            var entry = packed[i];
            events[i] = new epoll_event { events = entry.events, data = entry.data };
        }

        return received;
    }

    [LibraryImport(LibraryName, EntryPoint = "epoll_wait", SetLastError = true)]
    private static partial int epoll_wait_packed(SafeFileDescriptorHandle epfd, Span<epoll_event_packed> events, int maxevents, int timeout);

    [LibraryImport(LibraryName, EntryPoint = "epoll_wait", SetLastError = true)]
    private static partial int epoll_wait_aligned(SafeFileDescriptorHandle epfd, Span<epoll_event> events, int maxevents, int timeout);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int write(SafeFileDescriptorHandle fd, Span<byte> buf, nint count);
}
