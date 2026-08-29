using System.Diagnostics;
using System.Runtime.InteropServices;
using Winnow.Core.Auth;

namespace Winnow.App.Services;

/// <summary>
/// Console-based <see cref="IInteractiveAuthPrompt"/>: prints the sign-in URL,
/// lets the user authenticate in their own browser, and reads back the pasted
/// authorization code. Peer to the embedded-browser flow for headless machines,
/// missing WebView2, or when Epic breaks the embedded page. Never prints secrets.
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

    /// <summary>Writes a prompt, flushes stdout (required for WinExe piped output), and reads one line.</summary>
    private static string? ReadLine(string prompt)
    {
        Console.Write(prompt);
        Console.Out.Flush();
        return Console.ReadLine();
    }

    /// <summary>Best-effort browser open via shell execute. The URL is already printed above as fallback.</summary>
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

    /// <summary>Whether there is anywhere to prompt. Redirected streams count as available.</summary>
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
    /// Attaches this WinExe process to its parent terminal's console via
    /// <c>ATTACH_PARENT_PROCESS</c>. Skipped when streams are already redirected,
    /// because attaching would rebind Console away from the pipe and hang.
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
