#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Redplcs.HighResolutionTimer.Platform.Linux;

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static partial class Interop
{
    private const string LibraryName = "libc";
    
    internal const int EFD_CLOEXEC = 0x80000;
    internal const int EFD_NONBLOCK = 0x800;
    
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

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial SafeFileDescriptorHandle eventfd(uint initval, int flags);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int ppoll(Span<pollfd> fds, nuint nfds, ref timespec tmo_p, nint sigmask);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int read(SafeFileDescriptorHandle fd, Span<byte> buf, nint count);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int write(SafeFileDescriptorHandle fd, Span<byte> buf, nint count);

    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int close(int fd);
}
