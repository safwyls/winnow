using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Winnow.Update;

internal static partial class DurableFiles
{
    internal static void Move(string source, string target, bool replace = false, bool directory = false)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!MoveFileEx(source, target, 8u | (replace ? 1u : 0u))) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        else
        {
            if (directory) Directory.Move(source, target); else File.Move(source, target, replace);
            FlushDirectory(Path.GetDirectoryName(source)!);
            FlushDirectory(Path.GetDirectoryName(target)!);
        }
    }
    internal static void FlushDirectory(string directory)
    {
        if (OperatingSystem.IsWindows()) return;
        var fd = Open(directory, 0);
        if (fd < 0) throw new IOException("Could not open the update directory for durability.");
        try { if (Fsync(fd) != 0) throw new IOException("Could not flush the update directory."); }
        finally { Close(fd); }
    }
    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string source, string target, uint flags);
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);
    [LibraryImport("libc", EntryPoint = "fsync")]
    private static partial int Fsync(int fd);
    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int fd);
}
