using System.Diagnostics;
using System.Runtime.InteropServices;
using Winnow.Core.Auth;

namespace Winnow.App.Services;

/// <summary>
/// The manual sign-in, behind the same seam as the embedded one: print the URL,
/// let the user authenticate in their own browser, take the code they paste
/// back.
///
/// <para><b>This is a peer, not a legacy path.</b> Three concrete situations
/// need it, and none of them is hypothetical:</para>
/// <list type="number">
///   <item><description><b>Headless machines.</b> WebView2 needs a window; an
///   SSH session or a service account has none.</description></item>
///   <item><description><b>A missing WebView2 runtime.</b> It ships with
///   Windows 11 and was found preinstalled during the spike — on one machine.
///   Server SKUs, LTSC, stripped images and fleets that block Edge updates are
///   all real, and <c>GetAvailableBrowserVersionString()</c> throwing is the
///   detection point.</description></item>
///   <item><description><b>Epic breaking the embedded flow.</b> Legendary ships
///   a remote <c>webview_killswitch</c> because this happens periodically. When
///   the automatic route stops working, the manual one has to still be
///   there.</description></item>
/// </list>
///
/// <para>Implementing <see cref="IInteractiveAuthPrompt"/> is what makes that
/// structural rather than a maintenance promise: the fallback is the same
/// interface, resolved from the same chain, exercised by the same code path.</para>
///
/// <para><b>Nothing here prints a secret.</b> Not the client pair, not the
/// pasted code, not any token. The code is read straight into the result and
/// never echoed.</para>
/// </summary>
public sealed class ConsoleAuthPrompt : IInteractiveAuthPrompt
{
    /// <inheritdoc/>
    public string Name => "console";

    /// <inheritdoc/>
    public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default)
        => ValueTask.FromResult(HasConsole());

    /// <inheritdoc/>
    public Task<AuthCodeResult> RequestCodeAsync(AuthPromptRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AttachConsoleIfNeeded();

        if (!HasConsole())
        {
            return Task.FromResult(AuthCodeResult.Unavailable(
                "this process has no console to prompt on"));
        }

        Console.WriteLine();
        Console.WriteLine("Sign in to " + request.ProviderName);
        Console.WriteLine(new string('=', "Sign in to ".Length + request.ProviderName.Length));
        Console.WriteLine();

        // THE CONSENT MOMENT, and it comes before the URL rather than after it.
        // The point is to be read while the user still has the option of doing
        // nothing; printed underneath a clickable link it would be scenery.
        Console.WriteLine(request.ConsentNotice);
        Console.WriteLine();
        Console.WriteLine("1. Open this URL and sign in:");
        Console.WriteLine();
        Console.WriteLine("   " + request.StartUrl);
        Console.WriteLine();
        Console.WriteLine("2. The page returns a small block of JSON. Copy the value of");
        Console.WriteLine("   \"authorizationCode\" - the 32-character string, without quotes.");
        Console.WriteLine();

        // Printed BEFORE the prompt, deliberately. This is a WinExe — a
        // GUI-subsystem binary with no console of its own — so whether
        // Console.ReadLine ever returns depends on how the parent terminal wired
        // up the child's handles. When that goes wrong it does not fail, it
        // HANGS, at a prompt that may never have rendered. An escape hatch
        // described after the prompt would be invisible exactly when it is
        // needed.
        Console.WriteLine("   If the prompt below does not respond, press Ctrl+C, open the URL");
        Console.WriteLine("   above yourself, and run this instead — it needs no keyboard input:");
        Console.WriteLine();
        Console.WriteLine("       dotnet run --project src/Winnow.App -- --epic-login --code <code>");
        Console.WriteLine();

        // Gated on a keystroke rather than opening immediately: the page issues a
        // credential the provider itself describes as full account access, and
        // the notice explaining that is a few lines above. Opening the browser
        // the instant the command runs would put it in front of the user before
        // they had the chance to read why they should think about it.
        var first = ReadLine("3. Press Enter to open that URL in your browser, or paste the code directly: ");

        string? code;
        if (string.IsNullOrWhiteSpace(first))
        {
            TryOpenBrowser(request.StartUrl);
            Console.WriteLine();
            code = ReadLine("4. Paste the code here and press Enter: ");
        }
        else
        {
            // The user already had a code in hand — from a previous run, or from
            // opening the URL themselves — and pasted it at the first prompt.
            code = first;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("No code entered. Nothing was changed.");
            return Task.FromResult(AuthCodeResult.Cancelled("no code was entered"));
        }

        // Always an authorization code: this is the page that prints one, and the
        // exchange-code grant only ever arrives through the launcher's JavaScript
        // bridge, which a console cannot host.
        return Task.FromResult(
            AuthCodeResult.Captured(AuthCodeKind.AuthorizationCode, code.Trim(), "pasted by the user"));
    }

    /// <summary>
    /// Writes a prompt and reads one line, flushing first.
    ///
    /// <para>The flush is the point. <see cref="Console.Write(string)"/> leaves a
    /// prompt with no trailing newline sitting in the buffer, and a
    /// GUI-subsystem process whose stdout is a pipe rather than a console does
    /// not necessarily push it out before blocking on input — so the user waits
    /// at an invisible prompt for a process that looks hung.</para>
    /// </summary>
    private static string? ReadLine(string prompt)
    {
        Console.Write(prompt);
        Console.Out.Flush();
        return Console.ReadLine();
    }

    /// <summary>
    /// Opens the user's own browser. Best effort — the URL is printed above
    /// regardless, so a locked-down or headless machine loses nothing.
    /// </summary>
    private static void TryOpenBrowser(Uri url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException
            or FileNotFoundException)
        {
            // No browser, no shell association, or a sandbox.
        }
    }

    /// <summary>
    /// Whether there is anywhere to prompt.
    ///
    /// <para>Redirected streams count: a caller who piped input has supplied
    /// somewhere to read from, which is exactly the non-interactive case the
    /// <c>--code</c> argument exists for and which must not be treated as "no
    /// console".</para>
    /// </summary>
    private static bool HasConsole()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return true;
        }

        if (!OperatingSystem.IsWindows())
        {
            return Environment.UserInteractive;
        }

        try
        {
            return GetConsoleWindow() != IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Attaches this process to the terminal that launched it.
    ///
    /// <para><b>Necessary because <c>Winnow.App</c> is a <c>WinExe</c></b>, which
    /// tells Windows not to allocate a console. Without this, every
    /// <c>Console.WriteLine</c> goes nowhere and <c>Console.ReadLine</c> returns
    /// null immediately — the flow would appear to do nothing at all.
    /// <c>ATTACH_PARENT_PROCESS</c> borrows the console of whatever launched it,
    /// which is the terminal the user typed <c>dotnet run</c> into.</para>
    ///
    /// <para><b>Skipped when the standard streams are already redirected</b>, and
    /// that guard is not theoretical. Attaching rebinds <see cref="Console"/> to
    /// the real console handles, which for a piped invocation means output stops
    /// reaching the pipe and <c>Console.ReadLine</c> stops reading it — the flow
    /// hangs forever waiting on a console nobody is typing into. Measured, not
    /// guessed.</para>
    ///
    /// <para>Internal rather than private because <c>EpicLoginConsole</c> prints
    /// its verification report through the same borrowed console and would
    /// otherwise carry a second copy of this.</para>
    /// </summary>
    internal static void AttachConsoleIfNeeded()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch (EntryPointNotFoundException)
        {
            // Not a Windows build that has it. Nothing to do.
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
