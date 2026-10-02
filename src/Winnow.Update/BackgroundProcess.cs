using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Winnow.Update;

internal static class BackgroundProcess
{
    private static readonly object Gate = new();

    internal static Process Start(ProcessStartInfo start)
    {
        if (!OperatingSystem.IsWindows())
            return Process.Start(start) ?? throw new IOException("The background process could not start.");
        lock (Gate)
        {
            var inherited = new List<IntPtr>();
            try
            {
                // Redirected child pipes do not exclude other inheritable Win32 handles.
                // A worker must not keep its short-lived caller's protocol pipes alive.
                foreach (var handle in new[] { GetStdHandle(-10), GetStdHandle(-11), GetStdHandle(-12) }.Distinct())
                {
                    if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
                    if (!GetHandleInformation(handle, out var flags)) throw new Win32Exception();
                    if ((flags & 1) == 0) continue;
                    if (!SetHandleInformation(handle, 1, 0)) throw new Win32Exception();
                    inherited.Add(handle);
                }
                return Process.Start(start) ?? throw new IOException("The background process could not start.");
            }
            finally
            {
                foreach (var handle in inherited)
                {
                    if (SetHandleInformation(handle, 1, 1)) continue;
                    // A concurrent close can prevent restoration. Keep attempting the remaining
                    // handles and preserve ownership of any process that has already started.
                    // The failed handle remains noninheritable, rather than exposing a pipe.
                    var error = Marshal.GetLastWin32Error();
                    try { Trace.TraceWarning("Background process handle restoration failed (Win32 {0}).", error); }
                    catch { /* A diagnostic listener must not replace the launch result. */ }
                }
            }
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int value);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
