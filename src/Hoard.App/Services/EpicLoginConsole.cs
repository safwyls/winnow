using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Epic.Web.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Hoard.App.Services;

/// <summary>
/// The one-time interactive Epic sign-in, run from a terminal:
/// <c>dotnet run --project src/Hoard.App -- --epic-login</c>.
///
/// <para><b>Why this exists as a console step rather than a settings screen.</b>
/// The OAuth flow needs the user to authenticate to Epic and hand back a short
/// single-use code, and there is no way to do that inside Hoard without either
/// an embedded browser — Avalonia has none, so it would mean hosting WebView2 on
/// Windows and something else everywhere else — or asking for the user's Epic
/// password, which Hoard must never do and never does. So the user signs in to
/// Epic in their own browser, on Epic's own page, and pastes back one code that
/// is spent immediately. Hoard never sees a password, and the code it does see is
/// never logged, never written to disk, and dead within minutes.</para>
///
/// <para><b>It doubles as the verification step</b>, which is the other reason it
/// prints rather than silently succeeding. Two things about the Epic API could
/// not be settled without a real token — whether the playtime endpoint returns
/// data for this account, and what unit its <c>totalTime</c> is in — so this
/// prints the raw figures for the account's most-played titles, next to the
/// number Hoard would derive from them. Comparing that against the launcher's own
/// "You've Played" display settles
/// <see cref="EpicWebOptions.PlaytimeUnit"/> in one look.</para>
///
/// <para><b>Nothing here prints a secret.</b> Not the client secret, not the
/// pasted code, not the access or refresh token, not the account id. The success
/// line names the display name only, because the user has just proved they own
/// the account and needs to see which one they connected.</para>
/// </summary>
public static class EpicLoginConsole
{
    /// <summary>The argument that selects this path instead of the UI.</summary>
    public const string Argument = "--epic-login";

    /// <summary>
    /// Optional companion argument carrying the authorization code, as
    /// <c>--epic-login --code &lt;code&gt;</c>.
    ///
    /// <para>Exists because the interactive prompts cannot be relied on. This is
    /// a <c>WinExe</c> — a GUI-subsystem binary — so it owns no console of its
    /// own, and whether <see cref="Console.ReadLine"/> ever returns depends on
    /// how the host terminal wired up the child's handles. When that goes wrong
    /// it does not fail, it HANGS, with a prompt that may not even have been
    /// rendered; the user sees a browser open and then nothing, and has no way to
    /// tell a stuck process from one that is working. Passing the code as an
    /// argument removes console input from the flow entirely, which is why the
    /// instructions print this route before the prompt that might swallow
    /// them.</para>
    /// </summary>
    public const string CodeArgument = "--code";

    /// <summary>
    /// Pulls the authorization code out of the command line, accepting both
    /// <c>--code &lt;value&gt;</c> and <c>--code=&lt;value&gt;</c>. Null when absent.
    /// </summary>
    public static string? CodeFrom(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], CodeArgument, StringComparison.Ordinal))
            {
                return i + 1 < args.Count && !args[i + 1].StartsWith('-') ? args[i + 1] : null;
            }

            if (args[i].StartsWith(CodeArgument + "=", StringComparison.Ordinal))
            {
                var value = args[i][(CodeArgument.Length + 1)..];
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the flow. Returns a process exit code: 0 on success, 1 on anything
    /// the user needs to act on.
    /// </summary>
    public static async Task<int> RunAsync(
        IServiceProvider services, string? presetCode = null, CancellationToken ct = default)
    {
        AttachConsoleIfNeeded();

        var client = services.GetService<IEpicAccountClient>();
        if (client is null)
        {
            Console.Error.WriteLine("Epic OAuth is not registered in this build.");
            return 1;
        }

        if (!await client.IsConfiguredAsync(ct))
        {
            WriteCredentialInstructions();
            return 1;
        }

        var url = await client.AuthorizationCodeUrl(ct);
        if (url is null)
        {
            WriteCredentialInstructions();
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Epic sign-in");
        Console.WriteLine("============");
        Console.WriteLine();
        Console.WriteLine("1. Open this URL, signing in to Epic if you are not already:");
        Console.WriteLine();
        Console.WriteLine("   " + url);
        Console.WriteLine();
        Console.WriteLine("2. The page returns a small block of JSON. Copy the value of");
        Console.WriteLine("   \"authorizationCode\" - the 32-character string, without quotes.");
        Console.WriteLine();
        Console.WriteLine("   Epic prints a warning on that page telling you not to share the code with a");
        Console.WriteLine("   third-party service. Hoard is a third-party service. What the code does is");
        Console.WriteLine("   explained in docs/spikes/epic-oauth.md; read it before continuing if you have");
        Console.WriteLine("   not. The code is single-use, expires within minutes, and is exchanged for a");
        Console.WriteLine("   session that is stored encrypted on this machine and never leaves it.");
        Console.WriteLine();

        // Deliberately gated on a keystroke rather than opened immediately. The
        // page this navigates to issues a credential Epic describes as full
        // access to the user's account, and the warning explaining that is three
        // lines above. Opening it the instant the command runs would put the
        // browser in front of the user before they had read why they should think
        // about it. One Enter makes the consent explicit and costs nothing.
        // Printed BEFORE the prompt, deliberately. If console input is broken the
        // prompt below never returns and never renders, so an escape hatch
        // described after it would be invisible exactly when it is needed.
        Console.WriteLine("   If the prompt below does not respond, press Ctrl+C, open the URL");
        Console.WriteLine("   above yourself, and run this instead — it needs no keyboard input:");
        Console.WriteLine();
        Console.WriteLine("       dotnet run --project src/Hoard.App -- --epic-login --code <code>");
        Console.WriteLine();

        string? code;
        if (!string.IsNullOrWhiteSpace(presetCode))
        {
            // Non-interactive: the code came in on the command line, so nothing
            // here reads the console or opens a browser.
            code = presetCode;
        }
        else if (string.IsNullOrWhiteSpace(ReadLineOrNull(
            "3. Press Enter to open that URL in your browser, or paste the code directly: ")))
        {
            TryOpenBrowser(url);
            Console.WriteLine();
            code = ReadLineOrNull("4. Paste the authorizationCode here and press Enter: ");
        }
        else
        {
            // The user already had a code in hand — from a previous run, or from
            // opening the URL themselves — and pasted it at the first prompt.
            code = _lastLine;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("No code entered. Nothing was changed.");
            return 1;
        }

        var result = await client.SignInAsync(code, ct);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(Explain(result.Failure));
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine(
            "Signed in as {0}.",
            string.IsNullOrWhiteSpace(result.DisplayName) ? "(no display name)" : result.DisplayName);
        Console.WriteLine(
            result.Persisted
                ? "The session is stored encrypted with DPAPI and will survive a restart."
                : "This host cannot encrypt the session at rest, so it is held in memory for this run only.");

        await ReportLibraryAsync(client, services, ct);
        return 0;
    }

    /// <summary>
    /// Fetches the library once and prints what it found — the actual
    /// verification.
    /// </summary>
    private static async Task ReportLibraryAsync(
        IEpicAccountClient client, IServiceProvider services, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("Fetching the Epic library...");

        // TimeSpan.Zero forces a refetch: a cached answer would verify nothing.
        var library = await client.GetOwnedLibraryAsync(TimeSpan.Zero, ct);
        if (!library.Succeeded)
        {
            Console.Error.WriteLine(
                "The library request did not succeed. The sign-in itself worked and is stored; "
                + "check the log for the status code.");
            return;
        }

        var options = services.GetService<EpicWebOptions>() ?? new EpicWebOptions();

        Console.WriteLine();
        Console.WriteLine(
            "  {0} owned titles",
            library.Items.Count.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(
            "  {0} with an acquisition date",
            library.Items.Count(i => i.AcquiredAt is not null).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(
            "  playtime endpoint: {0}",
            library.PlaytimeAnswered ? "answered" : "did NOT answer");
        Console.WriteLine(
            "  {0} titles carry a playtime figure",
            library.WithPlaytime.ToString(CultureInfo.InvariantCulture));

        if (library.WithPlaytime == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                library.PlaytimeAnswered
                    ? "Epic answered but has no playtime recorded for this account. That is a real answer, "
                    + "not a failure: Epic only accrues time for sessions its own launcher started."
                    : "No playtime was returned. Ownership still works; every title's playtime stays null, "
                    + "which is 'unknown' and never overwrites a figure Hoard already has.");
            return;
        }

        // THE unit check. Print the raw integer beside what Hoard would store,
        // so the user can compare against the launcher's own display.
        Console.WriteLine();
        Console.WriteLine("Playtime unit check - compare these against the Epic launcher's \"You've Played\":");
        Console.WriteLine();
        Console.WriteLine("  {0,-34} {1,>12} {2,>14}", "artifactId (Epic codename)", "raw totalTime", "Hoard reads as");

        foreach (var item in library.Items
            .Where(i => i.TotalPlaytime is > 0)
            .OrderByDescending(i => i.TotalPlaytime)
            .Take(8))
        {
            var minutes = item.PlaytimeMinutes(options.PlaytimeUnit) ?? 0;
            Console.WriteLine(
                "  {0,-34} {1,>12} {2,>14}",
                Truncate(item.AppName, 34),
                item.TotalPlaytime!.Value.ToString(CultureInfo.InvariantCulture),
                FormatHours(minutes));
        }

        Console.WriteLine();
        Console.WriteLine(
            "Current setting: totalTime is read as {0}. If the \"Hoard reads as\" column is about 60x",
            options.PlaytimeUnit);
        Console.WriteLine(
            "off what the launcher shows, set EpicWebOptions.PlaytimeUnit to the other value.");
        Console.WriteLine();
        Console.WriteLine(
            "Note: Epic reports no last-played date for any title, through any endpoint. Epic games");
        Console.WriteLine(
            "therefore still cannot enter a recency-based bucket from API data alone.");
    }

    private static string FormatHours(long minutes)
        => string.Create(CultureInfo.InvariantCulture, $"{minutes / 60.0:n1} h");

    private static string Truncate(string value, int length)
        => value.Length <= length ? value : value[..(length - 1)] + "~";

    private static void WriteCredentialInstructions()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("No Epic OAuth client credentials are configured, so there is nothing to sign in with.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Hoard does not ship Epic's client credentials. Reading a storefront library needs a");
        Console.Error.WriteLine("client Epic only issues to its own launcher, and baking that into this repository");
        Console.Error.WriteLine("would put a credential Hoard has no right to into every checkout. Supplying it is");
        Console.Error.WriteLine("your decision, not Hoard's default. docs/spikes/epic-oauth.md sets out what that");
        Console.Error.WriteLine("choice involves.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Set them for one run:");
        Console.Error.WriteLine();
        Console.Error.WriteLine("    $env:Epic__ClientId = \"<client id>\"");
        Console.Error.WriteLine("    $env:Epic__ClientSecret = \"<client secret>\"");
        Console.Error.WriteLine("    dotnet run --project src/Hoard.App -- --epic-login");
        Console.Error.WriteLine();

        // The failure this paragraph exists for: setx or the System Properties
        // dialog writes the User scope, but a shell reads that scope once, when
        // it starts. Set the variables in an already-open terminal and every
        // child it launches -- this process included -- still sees nothing, so
        // the credentials look correct everywhere the user thinks to check and
        // this message keeps printing. It costs a genuinely confusing round of
        // debugging, so it is called out here rather than left to be deduced.
        Console.Error.WriteLine("Already set them and still seeing this? A terminal reads user-scope");
        Console.Error.WriteLine("variables only when it starts, so one that was open beforehand cannot");
        Console.Error.WriteLine("see them. Open a new terminal, or pull them into this one:");
        Console.Error.WriteLine();
        Console.Error.WriteLine("    $env:Epic__ClientId = "
            + "[Environment]::GetEnvironmentVariable('Epic__ClientId','User')");
        Console.Error.WriteLine("    $env:Epic__ClientSecret = "
            + "[Environment]::GetEnvironmentVariable('Epic__ClientSecret','User')");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Or put them in a git-ignored appsettings.local.json beside the executable:");
        Console.Error.WriteLine();
        Console.Error.WriteLine("    { \"Epic\": { \"ClientId\": \"...\", \"ClientSecret\": \"...\" } }");
    }

    private static string Explain(EpicSignInFailure failure) => failure switch
    {
        EpicSignInFailure.InvalidAuthorizationCode =>
            "Epic rejected the code. Authorization codes are single-use and expire within minutes, so the "
            + "usual cause is that it was already spent or is stale. Reload the URL for a fresh one and "
            + "try again.",
        EpicSignInFailure.InvalidClientCredentials =>
            "Epic rejected the client credentials themselves, not the code. Check the client id and secret.",
        EpicSignInFailure.Unreachable =>
            "Could not reach Epic. Nothing was changed; try again.",
        EpicSignInFailure.NotConfigured =>
            "No client credentials are configured.",
        _ =>
            "Epic answered with something this client did not understand. Nothing was changed.",
    };

    /// <summary>
    /// Opens the user's default browser at the sign-in URL. Best effort — the URL
    /// is printed above regardless, so a headless or locked-down machine loses
    /// nothing.
    /// </summary>
    private static void TryOpenBrowser(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or PlatformNotSupportedException or System.IO.FileNotFoundException)
        {
            // No browser, no shell association, or a sandbox. The printed URL is
            // the fallback and it is always printed first.
        }
    }

    /// <summary>
    /// Attaches this process to the terminal that launched it.
    ///
    /// <para><b>Necessary because <c>Hoard.App</c> is a <c>WinExe</c></b>, which
    /// tells Windows not to allocate a console. Without this, every
    /// <c>Console.WriteLine</c> below goes nowhere and <c>Console.ReadLine</c>
    /// returns null immediately — the flow would appear to do nothing at all.
    /// <c>ATTACH_PARENT_PROCESS</c> borrows the console of whatever launched it,
    /// which is the terminal the user typed <c>dotnet run</c> into.</para>
    ///
    /// <para><b>Skipped when the standard streams are already redirected</b>, and
    /// that guard is not theoretical. Attaching rebinds <see cref="Console"/> to
    /// the real console handles, which for a piped invocation
    /// (<c>… --epic-login &lt;&lt;&lt; "code" | tee log</c>) means output stops
    /// reaching the pipe and <c>Console.ReadLine</c> stops reading it — the flow
    /// hangs forever waiting on a console nobody is typing into. Measured, not
    /// guessed. When a caller has redirected the streams they have supplied
    /// somewhere to read and write, so the attach is unnecessary as well as
    /// harmful.</para>
    ///
    /// <para>Failure is otherwise ignored: launched with no parent console there
    /// is simply nowhere to print, and the caller has already decided this is the
    /// console path.</para>
    /// </summary>
    /// <summary>Last line <see cref="ReadLineOrNull"/> returned.</summary>
    private static string? _lastLine;

    /// <summary>
    /// Writes a prompt and reads one line, flushing first.
    ///
    /// <para>The flush is the point. <see cref="Console.Write(string)"/> leaves a
    /// prompt with no trailing newline sitting in the buffer, and a
    /// GUI-subsystem process whose stdout is a pipe rather than a console does
    /// not necessarily push it out before blocking on input — so the user waits
    /// at an invisible prompt for a process that looks hung.</para>
    /// </summary>
    private static string? ReadLineOrNull(string prompt)
    {
        Console.Write(prompt);
        Console.Out.Flush();
        _lastLine = Console.ReadLine();
        return _lastLine;
    }

    private static void AttachConsoleIfNeeded()
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
}
