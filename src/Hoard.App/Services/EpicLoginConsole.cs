using System.Globalization;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Epic.Web.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Hoard.App.Services;

/// <summary>
/// The one-time interactive Epic sign-in, run from a terminal:
/// <c>dotnet run --project src/Hoard.App -- --epic-login</c>.
///
/// <para><b>What this is now, since M4.6.</b> The embedded-browser sign-in
/// (<c>--epic-signin</c>, and eventually a button) captures the code the instant
/// Epic issues it, and it is the better flow. This one did NOT become a legacy
/// path: it is the peer that runs where a browser window cannot — a headless
/// machine, a Windows install with no WebView2 runtime, and the day Epic breaks
/// the embedded page, which <c>docs/spikes/epic-oauth.md</c> §12.3 names as the
/// realistic failure mode. Both go through the same
/// <c>IInteractiveAuthPrompt</c> seam, so the fallback is exercised by the same
/// code path rather than kept alive by good intentions.</para>
///
/// <para>Here the user signs in to Epic in their own browser, on Epic's own page,
/// and pastes back one code that is spent immediately. Hoard never sees a
/// password, and the code it does see is never logged, never written to disk, and
/// dead within minutes.</para>
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
        ConsoleAuthPrompt.AttachConsoleIfNeeded();

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

        EpicSignInResult result;
        if (!string.IsNullOrWhiteSpace(presetCode))
        {
            // Non-interactive: the code came in on the command line, so nothing
            // here reads the console or opens a browser. This is the escape
            // hatch for a terminal whose child-process handles are wired in a way
            // that makes Console.ReadLine hang, which is a real failure that
            // costs a burned code every time it happens.
            result = await client.SignInAsync(presetCode, ct);
        }
        else
        {
            // Through the same seam the embedded browser uses. The prompt chain
            // resolves to the console implementation here without anything
            // selecting it explicitly: WebView2AuthPrompt reports itself
            // unavailable when no Avalonia application is running, and none is —
            // this path deliberately runs before Avalonia starts.
            var signIn = services.GetService<EpicSignInService>();
            if (signIn is null)
            {
                Console.Error.WriteLine("Epic sign-in is not registered in this build.");
                return 1;
            }

            result = await signIn.SignInAsync(ct);
        }

        if (!result.Succeeded)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(EpicSignInService.Explain(result.Failure));
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
    ///
    /// <para>Internal because <c>--epic-signin</c> ends on the same report: the
    /// playtime unit is still unverified (<c>docs/spikes/epic-oauth.md</c> §7)
    /// and whichever sign-in route the user took, this is the table that settles
    /// it.</para>
    /// </summary>
    internal static async Task ReportLibraryAsync(
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

    /// <summary>
    /// The "nothing to sign in with" message.
    ///
    /// <para><b>Nearly unreachable, and kept for the day it is not.</b> Hoard now
    /// ships a built-in launcher client pair as the LAST credential source
    /// (<c>BuiltInEpicCredentialSource</c>), so every install is configured by
    /// default. This prints only if that pair has been removed or emptied — which
    /// is exactly what a maintainer would do the day Epic rotates it, and on that
    /// day the message needs to say what to supply.</para>
    /// </summary>
    private static void WriteCredentialInstructions()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("No Epic OAuth client credentials are available, so there is nothing to sign in with.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Hoard normally ships a built-in pair, so seeing this means it has been removed or");
        Console.Error.WriteLine("Epic has rotated it. A user-supplied pair takes precedence over the built-in one,");
        Console.Error.WriteLine("so setting your own is also the workaround. docs/spikes/epic-oauth.md explains what");
        Console.Error.WriteLine("these credentials are and what using them involves.");
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
}
