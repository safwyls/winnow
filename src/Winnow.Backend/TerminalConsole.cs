using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Winnow.Backend;

internal static class TerminalConsole
{
    public static Process StartBackground(ProcessStartInfo start)
    {
        if (!OperatingSystem.IsWindows())
            return Process.Start(start) ?? throw new IOException("The Winnow backend could not start.");
        var inherited = new List<IntPtr>();
        try
        {
            // Process.Start creates new redirected child pipes, but Win32 also inherits any other
            // inheritable handles. The independent backend must not keep this terminal's caller
            // waiting for EOF after the short-lived command exits. Never close/rebind those handles.
            foreach (var handle in new[] { GetStdHandle(-10), GetStdHandle(-11), GetStdHandle(-12) }.Distinct())
            {
                if (!GetHandleInformation(handle, out var flags) || (flags & 1) == 0) continue;
                if (!SetHandleInformation(handle, 1, 0)) throw new System.ComponentModel.Win32Exception();
                inherited.Add(handle);
            }
            return Process.Start(start) ?? throw new IOException("The Winnow backend could not start.");
        }
        finally
        {
            foreach (var handle in inherited)
                if (!SetHandleInformation(handle, 1, 1)) throw new System.ComponentModel.Win32Exception();
        }
    }

    public static void Attach(int? terminalParent)
    {
        if (!OperatingSystem.IsWindows()) return;
        var input = GetStdHandle(-10);
        var output = GetStdHandle(-11);
        var error = GetStdHandle(-12);
        if (Usable(input) && Usable(output) && Usable(error)) return;
        if (!AttachConsole(terminalParent ?? -1)) return;
        // AttachConsole can replace all three handles. Preserve each individually redirected pipe
        // or file; only absent handles should acquire the terminal inherited through the GUI parent.
        if (Usable(input)) SetStdHandle(-10, input);
        if (Usable(output)) SetStdHandle(-11, output);
        if (Usable(error)) SetStdHandle(-12, error);
        Console.SetIn(new StreamReader(Console.OpenStandardInput()));
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }

    private static bool Usable(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
        var kind = GetFileType(handle);
        return kind is 1 or 3 || kind == 2 && GetConsoleMode(handle, out _);
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int value);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int value, IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
