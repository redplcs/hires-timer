using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Redplcs.HighResolutionTimer.Platform.Unix;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "IdentifierTypo")]
internal static partial class Interop
{
    private const string LibraryName = "libc";
    
    internal const int EINTR = 4;
    
    [LibraryImport(LibraryName, SetLastError = true)]
    internal static partial int close(int fd);
}