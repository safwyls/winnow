using System.Globalization;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Winnow.App.Services;

/// <summary>
/// Console-based Epic sign-in flow (<c>--epic-login</c>). Peer to the
/// embedded-browser flow for headless/no-WebView2 environments. Also prints
/// a playtime verification table to confirm <see cref="EpicWebOptions.PlaytimeUnit"/>.
/// Never prints secrets.
/// </summary>
public static class EpicLoginConsole
{
    /// <summary>The argument that selects this path instead of the UI.</summary>
    public const string Argument = "--epic-login";

    /// <summary>
    /// Optional companion argument (<c>--code &lt;code&gt;</c>) to bypass console
    /// input, which can hang in this WinExe process.
    /// </summary>
    public const string CodeArgument = "--code";

    /// <summary>Parses <c>--code &lt;value&gt;</c> or <c>--code=value</c> from the command line. Null when absent.</summary>
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

    /// <summary>Runs the sign-in flow. Returns 0 on success, 1 on failure.</summary>
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

    /// <summary>Fetches the library and prints the playtime verification table. Used by both sign-in routes.</summary>
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
                    + "which is 'unknown' and never overwrites a figure Winnow already has.");
            return;
        }

        // THE unit check. Print the raw integer beside what Winnow would store,
        // so the user can compare against the launcher's own display.
        Console.WriteLine();
        Console.WriteLine("Playtime unit check - compare these against the Epic launcher's \"You've Played\":");
        Console.WriteLine();
        Console.WriteLine("  {0,-34} {1,>12} {2,>14}", "artifactId (Epic codename)", "raw totalTime", "Winnow reads as");

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
            "Current setting: totalTime is read as {0}. If the \"Winnow reads as\" column is about 60x",
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

    /// <summary>Prints instructions when no Epic OAuth client credentials are available.</summary>
    private static void WriteCredentialInstructions()
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine("No Epic OAuth client credentials are available, so there is nothing to sign in with.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Winnow normally ships a built-in pair, so seeing this means it has been removed or");
        Console.Error.WriteLine("Epic has rotated it. A user-supplied pair takes precedence over the built-in one,");
        Console.Error.WriteLine("so setting your own is also the workaround. docs/spikes/epic-oauth.md explains what");
        Console.Error.WriteLine("these credentials are and what using them involves.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Set them for one run:");
        Console.Error.WriteLine();
        Console.Error.WriteLine("    $env:Epic__ClientId = \"<client id>\"");
        Console.Error.WriteLine("    $env:Epic__ClientSecret = \"<client secret>\"");
        Console.Error.WriteLine("    dotnet run --project src/Winnow.App -- --epic-login");
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
