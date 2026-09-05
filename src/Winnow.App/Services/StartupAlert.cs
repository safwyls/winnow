using System.Runtime.InteropServices;

namespace Winnow.App.Services;

/// <summary>
/// The one channel a startup refusal or a startup fault reaches the user on,
/// before there is a window to put anything in.
///
/// <para>Winnow is a <c>WinExe</c>: a GUI-subsystem binary with no console of
/// its own. Launched from a terminal it can attach to the parent's console and
/// a written line is read; launched from Explorer there is no console to
/// attach to and a written line goes nowhere at all. The second-copy refusal
/// and the startup error boundary each need both halves, so they share
/// one.</para>
/// </summary>
internal static class StartupAlert
{
    /// <summary>A fault: something went wrong and the run is over.</summary>
    internal static void Error(string title, string text)
        => Show(title, text, MbIconError);

    /// <summary>A refusal that is not a fault, such as a second copy declining
    /// to start against a data directory the first one already holds.</summary>
    internal static void Notice(string title, string text)
        => Show(title, text, MbIconInformation);

    private static void Show(string title, string text, int icon)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);

        if (WrittenLineWouldBeRead())
        {
            Console.Error.WriteLine(text);
            return;
        }

        ShowMessageBox(title, text, icon);
    }

    /// <summary>
    /// Whether a line written to stderr would actually be read by somebody —
    /// which is a different question from whether stderr exists.
    ///
    /// <para><b>This deliberately does not ask <see
    /// cref="ConsoleAuthPrompt.HasConsole"/>.</b> That helper treats a
    /// redirected stream as a place to write, and a WinExe launched from
    /// Explorer has a NULL standard handle that .NET reports as redirected —
    /// so it answers "yes, there is a console" in precisely the launch that has
    /// none, and the line goes into the void. Measured: from a console-less
    /// parent, <c>IsOutputRedirected</c> is <c>true</c> while the handle is
    /// <c>0</c> and <c>GetFileType</c> is <c>FILE_TYPE_UNKNOWN</c>. TASK-57
    /// owns correcting the shared helper and the sign-in flows that trust it;
    /// this asks the narrower question directly, because a startup fault that
    /// only prints to a handle nobody holds is the silent crash all over
    /// again.</para>
    ///
    /// <para>A handle that is a file or a pipe counts: somebody redirected it
    /// on purpose and is reading it. Only "there is no destination at all"
    /// falls through to the message box.</para>
    /// </summary>
    private static bool WrittenLineWouldBeRead()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Every other platform gives a process a real stderr, and there is
            // no message box to fall back to anyway.
            return true;
        }

        ConsoleAuthPrompt.AttachConsoleIfNeeded();

        try
        {
            if (GetConsoleWindow() != IntPtr.Zero)
            {
                return true;
            }

            var handle = GetStdHandle(StdErrorHandle);
            if (handle == IntPtr.Zero || handle == InvalidHandleValue)
            {
                return false;
            }

            return GetFileType(handle) != FileTypeUnknown;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void ShowMessageBox(string title, string text, int icon)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(text);
            return;
        }

        try
        {
            MessageBoxW(IntPtr.Zero, text, title, MbOk | icon);
        }
        catch (EntryPointNotFoundException)
        {
            // No message box on this platform; stderr is the last resort.
            Console.Error.WriteLine(text);
        }
    }

    private const int MbOk = 0x0;
    private const int MbIconError = 0x10;
    private const int MbIconInformation = 0x40;

    private const int StdErrorHandle = -12;
    private const uint FileTypeUnknown = 0x0000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string title, int type);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int which);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);
}
