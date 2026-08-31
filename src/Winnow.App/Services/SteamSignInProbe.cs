using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Avalonia;
using Avalonia.Threading;
using Winnow.Auth.WebView;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Http;
using Winnow.Enrich.SteamWeb.Model;

namespace Winnow.App.Services;

/// <summary>
/// What a JWT payload said, deliberately unvalidated. Nothing decides anything
/// on the answer; it is printed.
/// </summary>
/// <param name="Readable">Whether a payload was decoded at all.</param>
/// <param name="ExpiresAt">The <c>exp</c> claim.</param>
/// <param name="Subject">The <c>sub</c> claim, a SteamID64 on a Steam token.</param>
/// <param name="Audiences">The <c>aud</c> claim, which Steam issues as an array.</param>
/// <param name="Issuer">The <c>iss</c> claim.</param>
public readonly record struct SteamProbeTokenClaims(
    bool Readable,
    DateTimeOffset? ExpiresAt,
    string? Subject,
    IReadOnlyList<string> Audiences,
    string? Issuer);

/// <summary>
/// THROWAWAY VERIFICATION SCAFFOLDING — TASK-56, spike items 1 and 5.
///
/// <para>The pure half of the probe: reading a token's claims, redacting
/// anything token-shaped, and building the three request URIs. Separated from
/// the console flow above it because these three are the only parts a test can
/// reach; the sign-in itself is a person in front of a browser.</para>
///
/// <para><b>This is diagnostics, not authentication.</b>
/// <see cref="ReadClaims"/> decodes a JWT payload and validates nothing: no
/// signature, no issuer, no audience, no expiry. It exists to print what a
/// token says about itself, and no decision rests on the answer.</para>
/// </summary>
public static partial class SteamSignInProbeFacts
{
    /// <summary>The Web API host. The same one <see cref="SteamWebOptions"/> defaults to.</summary>
    public static readonly Uri ApiBase = new("https://api.steampowered.com/");

    /// <summary>Verified live 2026-08-28 under key auth: takes no <c>steamid</c>; the credential identifies the account.</summary>
    public const string LastPlayedTimesPath = "IPlayerService/ClientGetLastPlayedTimes/v1/";

    /// <summary>The ownership endpoint, with §4.2's three mandated flags.</summary>
    public const string OwnedGamesPath = "IPlayerService/GetOwnedGames/v1/";

    /// <summary>Steam Replay.</summary>
    public const string YearInReviewPath = "ISaleFeatureService/GetUserYearInReview/v1/";

    /// <summary>
    /// The query parameter a session-minted token goes in. The whole point of
    /// the probe: Playnite's two call paths differ in this name and nothing
    /// else.
    /// </summary>
    public const string TokenParameter = "access_token";

    /// <summary>What a redacted value is replaced with.</summary>
    public const string Placeholder = "<redacted>";

    /// <summary>
    /// Reads a JWT's payload claims. Never validates, never throws.
    ///
    /// <para>The reader itself was promoted into
    /// <see cref="SteamTokenClaims"/> when S2 needed the same four claims for
    /// something other than printing them. This method survives as the shape the
    /// probe's console output and its tests already speak; it decodes nothing of
    /// its own, so there is exactly one base64url decoder in the codebase and no
    /// way for the diagnostic reader and the real one to disagree.</para>
    /// </summary>
    public static SteamProbeTokenClaims ReadClaims(string? token)
    {
        var claims = SteamTokenClaims.Read(token);

        return new SteamProbeTokenClaims(
            claims.Readable, claims.ExpiresAt, claims.Subject, claims.Audiences, claims.Issuer);
    }

    /// <summary>
    /// Replaces anything token-shaped in arbitrary text.
    ///
    /// <para>Three passes, widening as they go: named credential parameters,
    /// then anything with a JWT's three-segment shape, then any opaque run long
    /// enough to be a secret. The third pass is deliberately blunt. It runs on
    /// exception messages and response bodies, where the safe assumption is that
    /// any run of 32+ token-alphabet characters should not be printed.</para>
    /// </summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var scrubbed = NamedCredential().Replace(text, "$1=" + Placeholder);
        scrubbed = JwtShaped().Replace(scrubbed, Placeholder);
        return OpaqueRun().Replace(scrubbed, Placeholder);
    }

    /// <summary>
    /// The last-played request. No <c>steamid</c>: the credential identifies
    /// the account. Verified live under key auth 2026-08-28.
    /// </summary>
    public static Uri LastPlayedTimesUri(string accessToken) => new(
        ApiBase,
        LastPlayedTimesPath
        + "?format=json"
        + "&" + TokenParameter + "=" + Uri.EscapeDataString(Require(accessToken)));

    /// <summary>The ownership request, carrying the same three flags the key path sends.</summary>
    public static Uri OwnedGamesUri(string accessToken, ulong steamId64) => new(
        ApiBase,
        OwnedGamesPath
        + "?steamid=" + steamId64.ToString(CultureInfo.InvariantCulture)
        + "&include_appinfo=1"
        + "&include_played_free_games=1"
        + "&skip_unvetted_apps=false"
        + "&format=json"
        + "&" + TokenParameter + "=" + Uri.EscapeDataString(Require(accessToken)));

    /// <summary>The Steam Replay request for one year.</summary>
    public static Uri YearInReviewUri(string accessToken, ulong steamId64, int year) => new(
        ApiBase,
        YearInReviewPath
        + "?steamid=" + steamId64.ToString(CultureInfo.InvariantCulture)
        + "&year=" + year.ToString(CultureInfo.InvariantCulture)
        + "&format=json"
        + "&" + TokenParameter + "=" + Uri.EscapeDataString(Require(accessToken)));

    private static string Require(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return accessToken;
    }

    [GeneratedRegex(
        "(access_token|refresh_token|webapi_token|steamLoginSecure|steamRefresh_steam|sessionid|token|key)"
        + "\\s*[=:]\\s*\"?[^\\s&\"',;)}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedCredential();

    [GeneratedRegex(
        "[A-Za-z0-9_-]{6,}\\.[A-Za-z0-9_-]{6,}\\.[A-Za-z0-9_-]{6,}",
        RegexOptions.CultureInvariant)]
    private static partial Regex JwtShaped();

    [GeneratedRegex("[A-Za-z0-9_-]{32,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueRun();
}

/// <summary>
/// The probe's output, written to a file first and to a console second.
///
/// <para><b>The file is the channel that is relied on.</b> Winnow is a
/// <c>WinExe</c>, so the Windows GUI subsystem gives the process no console
/// and every <c>Console.WriteLine</c> in it goes nowhere, not to a buffer,
/// nowhere. That is code review finding F41, recorded 2026-08-28, and the
/// reason the second live run of this probe printed literally nothing.
/// Output ordering and buffering were not the explanation; there was no
/// console to buffer to. A diagnostic whose findings cannot reach the
/// person who ran it is not a diagnostic.</para>
///
/// <para>The writer flushes every line rather than at the end, so a run that
/// is killed halfway still leaves everything it had got to on disk.</para>
///
/// <para>Derived facts only, exactly as the console carried: statuses,
/// counts, an expiry, an account id. No token, no refresh token, no cookie,
/// and no query string ever reaches this.</para>
/// </summary>
internal sealed class SteamProbeLog : IDisposable
{
    private readonly StreamWriter? _file;
    private readonly Lock _gate = new();

    public SteamProbeLog(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, SteamSignInProbeConsole.ReportFileName);

            _file = new StreamWriter(Path, append: false) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            Path = null;
            Failure = ex.GetType().Name;
        }
    }

    /// <summary>Where the report is being written, or null when it could not be opened.</summary>
    public string? Path { get; }

    /// <summary>Why the file could not be opened, when it could not.</summary>
    public string Failure { get; } = "no reason recorded";

    /// <summary>The composite-format form, so the report reads as it did when it went to the console.</summary>
    public void Line(string format, params object?[] args)
        => Line(string.Format(CultureInfo.InvariantCulture, format, args));

    /// <summary>
    /// Writes one line to both channels. Never throws: this is called from
    /// the dispatcher, from the poll and from a watchdog, and none of them
    /// has anything useful to do about a failed write.
    /// </summary>
    public void Line(string text = "")
    {
        lock (_gate)
        {
            try
            {
                Console.Out.WriteLine(text);
                Console.Out.Flush();
            }
            catch (Exception ex) when (ex is IOException
                or ObjectDisposedException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                // No console, or it went away. The file is the answer, and a
                // failure to reach the second channel must never cost the first.
            }

            try
            {
                _file?.WriteLine(text);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Disk full, or the file was removed underneath the run.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                _file?.Flush();
                _file?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Nothing left to do; the lines were flushed as they were
                // written precisely so that this cannot lose anything.
            }
        }
    }
}

/// <summary>
/// THROWAWAY VERIFICATION SCAFFOLDING — TASK-56, spike items 1 and 5.
///
/// <para>A hidden command-line entry point (<c>--steam-signin-probe</c>) the
/// repository owner runs once, by hand, to answer the two questions the spike
/// could not: whether Steam's sign-in completes inside Winnow's off-the-record
/// WebView2 profile, and whether a store-minted token returns populated data
/// from the three endpoints Winnow depends on.</para>
///
/// <para>It is wired to no screen, no startup path and no scheduler; it opens
/// before the database is even initialised, so it cannot write a row. It prints
/// derived facts only: an expiry, an account id, statuses, counts. The token,
/// the refresh token and every cookie stay in memory for the life of the run
/// and are never logged, printed or persisted.</para>
///
/// <para>The report is written to a file beside the database and copied to
/// the console as a second channel. The file is the guaranteed output because
/// Winnow is a <c>WinExe</c> and may have no console at all; see
/// <see cref="SteamProbeLog"/>. The report runs inside a dispatcher-posted
/// lambda while the loop is still pumping, because running it after the loop
/// returned deadlocked: Avalonia's <c>SynchronizationContext</c> captured
/// awaits onto a dispatcher that was no longer servicing them, and the
/// blocking <c>GetResult()</c> on the main thread could never complete.</para>
/// </summary>
public static class SteamSignInProbeConsole
{
    /// <summary>The argument that selects this path.</summary>
    public const string Argument = "--steam-signin-probe";

    /// <summary>The report file's name. Fixed, so the path can be stated up front and looked at afterwards.</summary>
    public const string ReportFileName = "steam-signin-probe.txt";

    /// <summary>How long the user has to sign in before the run gives up.</summary>
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(10);

    /// <summary>How long one endpoint check may take.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the whole report may take once sign-in has finished.
    ///
    /// <para>Separate from <see cref="SignInTimeout"/> because the two are
    /// different kinds of wait: one is a person at a keyboard, the other is
    /// three HTTP requests that should take seconds.</para>
    /// </summary>
    private static readonly TimeSpan ReportBudget = TimeSpan.FromMinutes(2);

    /// <summary>
    /// When the run is abandoned whatever state it is in.
    ///
    /// <para>Sign-in plus report plus slack. Nothing about this probe is worth
    /// a process the user has to kill, which is what the second live run made
    /// them do.</para>
    /// </summary>
    private static readonly TimeSpan HardBudget = SignInTimeout + ReportBudget + TimeSpan.FromSeconds(30);

    /// <summary>Runs the probe and returns a process exit code (0 = all three endpoints populated).</summary>
    /// <param name="avalonia">The host's Avalonia builder. The browser needs a window system.</param>
    /// <param name="reportDirectory">Where <see cref="ReportFileName"/> is written.</param>
    /// <param name="ct">Cancelled when the host shuts down.</param>
    public static int Run(Func<AppBuilder> avalonia, string reportDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(avalonia);

        // Best effort, and explicitly not depended on. See TryOpenConsole.
        TryOpenConsole();

        using var log = new SteamProbeLog(reportDirectory);

        log.Line();
        log.Line("Steam sign-in probe (TASK-56) - throwaway verification scaffolding.");
        log.Line("It writes nothing to the database and persists no token, refresh token or cookie.");
        log.Line();
        log.Line("Report file: " + (log.Path ?? "COULD NOT BE CREATED - " + log.Failure));
        log.Line();
        log.Line("A private browser window is opening. Sign in to Steam there, Steam Guard included.");
        log.Line("A line a second follows, saying where the browser is and what the probe is reading.");
        log.Line("Once you are signed in it tries a short list of store pages and then stops.");
        log.Line();

        avalonia().SetupWithoutStarting();

        var exit = 1;
        using var loop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var watchdog = new CancellationTokenSource(HardBudget);

        using var watching = watchdog.Token.Register(() =>
        {
            log.Line();
            log.Line("WATCHDOG: the probe exceeded its overall budget and is being stopped.");
            Stop(loop);
        });

        ArmHardExit(log);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var session = new SteamSignInProbeSession(progress: log.Line);
                var result = await session.RunAsync(SignInTimeout, loop.Token).ConfigureAwait(false);

                // Its own budget, so a wedged request cannot spend the rest of
                // the run, and linked to the loop so a cancelled run stops it.
                using var report = CancellationTokenSource.CreateLinkedTokenSource(loop.Token);
                report.CancelAfter(ReportBudget);

                exit = await ReportAsync(log, result, report.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                log.Line();
                log.Line("The probe was stopped before it reached a conclusion.");
                exit = 2;
            }
            catch (Exception ex)
            {
                // Redacted, and the type named separately: an inner exception
                // can quote a request URI, and a request URI carries the token.
                log.Line();
                log.Line("The probe failed (" + ex.GetType().Name + "): "
                    + SteamSignInProbeFacts.Redact(ex.Message));
                exit = 2;
            }
            finally
            {
                // Every path. The whole report now happens INSIDE the pumping
                // dispatcher rather than after it, so this is the one place the
                // loop is allowed to end.
                Stop(loop);
            }
        });

        Dispatcher.UIThread.MainLoop(loop.Token);

        log.Line();
        log.Line(log.Path is { } path
            ? "Full report written to: " + path
            : "The report file could not be created (" + log.Failure + "), so the above is all there is.");

        return exit;
    }

    /// <summary>
    /// Ends the message loop from any thread.
    ///
    /// <para>Cancelling and then posting an empty job, because the two do
    /// different work: the cancellation is what <c>MainLoop</c> is watching,
    /// and the post is what wakes a pump that is otherwise blocked waiting for
    /// a message and would not notice.</para>
    /// </summary>
    private static void Stop(CancellationTokenSource loop)
    {
        try
        {
            loop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run already finished and tore its own source down.
        }

        try
        {
            Dispatcher.UIThread.Post(static () => { });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // No dispatcher left to wake, which means the loop has already
            // ended and there is nothing to do.
        }
    }

    /// <summary>
    /// The last resort, on a background thread that cannot itself hold the
    /// process open.
    ///
    /// <para>Everything above is supposed to make this unreachable. It exists
    /// because the second live run ended with the user force-closing their
    /// terminal, and for throwaway diagnostics whose one job is to terminate
    /// with an answer, an exit code they can see beats a process they have to
    /// kill.</para>
    /// </summary>
    private static void ArmHardExit(SteamProbeLog log)
    {
        var thread = new Thread(() =>
        {
            Thread.Sleep(HardBudget + TimeSpan.FromSeconds(30));

            log.Line();
            log.Line("The probe did not shut down cleanly within its budget. Exiting.");
            log.Dispose();

            Environment.Exit(3);
        })
        {
            IsBackground = true,
            Name = "steam-signin-probe-hard-exit",
        };

        thread.Start();
    }

    /// <summary>
    /// Tries to give this GUI process a console to print to, and reports
    /// whether it managed it.
    ///
    /// <para><b>Not the same as <see cref="ConsoleAuthPrompt.AttachConsoleIfNeeded"/>,
    /// and deliberately so.</b> That method returns early when
    /// <c>Console.IsOutputRedirected</c> is true, which is exactly the state a
    /// <c>WinExe</c> with no console is in: its standard output handle is null,
    /// <c>GetFileType</c> answers <c>FILE_TYPE_UNKNOWN</c>, and .NET reports
    /// that as redirected. So the guard meant to protect a piped run also
    /// skipped the attach in the one case it was written for. The probe needs
    /// its own opener rather than a change to a method three shipped sign-in
    /// flows depend on.</para>
    ///
    /// <para>Attaching is also not enough on its own: <c>AttachConsole</c>
    /// leaves .NET's cached <c>Console.Out</c> bound to the handle it already
    /// had, so the writer is rebound to <c>CONOUT$</c> by hand.</para>
    /// </summary>
    private static bool TryOpenConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            // A real pipe, file or console already: the user redirected output,
            // or launched this from somewhere that had a console to give. Either
            // way Console works and touching it would break the redirection.
            var current = GetStdHandle(StdOutputHandle);
            if (current != IntPtr.Zero && current != InvalidHandleValue && GetFileType(current) != FileTypeUnknown)
            {
                return true;
            }

            // The parent terminal first. Its own console is the one the user is
            // looking at; AllocConsole would open a second window that vanishes
            // when the process exits, which is worse than useless for a report.
            if (!AttachConsole(AttachParentProcess) && !AllocConsole())
            {
                return false;
            }

            var handle = CreateFileW(
                "CONOUT$", GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

            if (handle == InvalidHandleValue)
            {
                return false;
            }

            SetStdHandle(StdOutputHandle, handle);

            var stream = new FileStream(new SafeFileHandle(handle, ownsHandle: true), FileAccess.Write);
            Console.SetOut(new StreamWriter(stream) { AutoFlush = true });

            return true;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException
            or DllNotFoundException
            or IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const uint FileTypeUnknown = 0x0000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    private static async Task<int> ReportAsync(
        SteamProbeLog log, SteamSignInProbeResult result, CancellationToken ct)
    {
        log.Line();
        log.Line("SIGN-IN");
        log.Line("  outcome           : {0} ({1})", result.Outcome, result.Detail);
        log.Line("  password field    : {0}", result.SawLoginForm ? "rendered" : "never seen");
        log.Line("  page loads        : {0}", result.Navigations);
        log.Line("  mint route        : {0}", result.TokenSource ?? "no token minted");
        log.Line();

        if (result.Token is not { Length: > 0 } token)
        {
            log.Line("TOKEN");
            log.Line("  acquired          : no");
            log.Line();
            log.Line("Spike item 5 is answered by the sign-in line above; item 1 is not.");
            WriteLimits(log);
            return 1;
        }

        var claims = SteamSignInProbeFacts.ReadClaims(token);
        var steamId = ResolveSteamId(claims.Subject ?? result.SteamId);

        log.Line("TOKEN");
        log.Line("  acquired          : yes");
        log.Line("  payload           : {0}", claims.Readable ? "decoded" : "not a readable JWT payload");
        log.Line(
            "  expires           : {0}",
            claims.ExpiresAt is { } expiry
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"{expiry.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z ({Lifetime(expiry)} from now)")
                : "no exp claim");
        log.Line(
            "  audience          : {0}",
            claims.Audiences.Count > 0 ? string.Join(", ", claims.Audiences) : "no aud claim");
        log.Line("  issuer            : {0}", claims.Issuer ?? "no iss claim");
        log.Line(
            "  account           : {0}",
            steamId is { } id
                ? string.Create(
                    CultureInfo.InvariantCulture, $"SteamID64 {id.Value}, steam3 account id {id.AccountId}")
                : "the token carries no usable sub claim and the page reported no steamid");
        log.Line(
            "  page vs token     : {0}",
            result.SteamId is null
                ? "the page reported no steamid to compare"
                : string.Equals(result.SteamId, claims.Subject, StringComparison.Ordinal)
                    ? "the page's steamid and the token's sub agree"
                    : "THE PAGE'S STEAMID AND THE TOKEN'S SUB DISAGREE");
        log.Line();

        if (steamId is not { } account)
        {
            log.Line("Without an account id, GetOwnedGames and GetUserYearInReview cannot be asked.");
            WriteLimits(log);
            return 1;
        }

        var year = DateTime.UtcNow.Year - 1;

        log.Line("ENDPOINTS (access_token=, no API key anywhere in these requests)");

        using var http = new HttpClient { Timeout = RequestTimeout };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", new SteamWebOptions().UserAgent);

        var populated = 0;
        populated += await CheckAsync(
            log,
            http,
            SteamSignInProbeFacts.LastPlayedTimesUri(token),
            SteamSignInProbeFacts.LastPlayedTimesPath,
            DescribeLastPlayed,
            ct).ConfigureAwait(false);

        populated += await CheckAsync(
            log,
            http,
            SteamSignInProbeFacts.OwnedGamesUri(token, account.Value),
            SteamSignInProbeFacts.OwnedGamesPath,
            DescribeOwnedGames,
            ct).ConfigureAwait(false);

        populated += await CheckAsync(
            log,
            http,
            SteamSignInProbeFacts.YearInReviewUri(token, account.Value, year),
            SteamSignInProbeFacts.YearInReviewPath,
            body => DescribeYearInReview(body, year),
            ct).ConfigureAwait(false);

        log.Line();
        log.Line("{0} of 3 endpoints returned populated data under token auth.", populated);
        WriteLimits(log);

        return populated == 3 ? 0 : 1;
    }

    /// <summary>One endpoint check: status, headers, size, and a shape fact or two.</summary>
    private static async Task<int> CheckAsync(
        SteamProbeLog log, HttpClient http, Uri uri, string endpoint,
        Func<string, (bool Populated, string Shape)> describe, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(
                "application/json"));

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            var eresult = Header(response, "x-eresult") ?? "-";
            var error = Header(response, "x-error_message");

            if (!response.IsSuccessStatusCode)
            {
                // The endpoint constant and a redacted form of the URI, never
                // the raw request URI: it carries the token. SteamWebRedaction
                // works from an allowlist, so access_token is hidden by
                // construction rather than by anyone remembering to add it.
                log.Line(
                    "  {0,-44} {1}  x-eresult {2}  FAILED  {3}",
                    endpoint,
                    (int)response.StatusCode,
                    eresult,
                    SteamSignInProbeFacts.Redact(error ?? response.ReasonPhrase ?? "no reason given"));
                log.Line("  {0,-44} sent as {1}", string.Empty, SteamWebRedaction.Describe(uri));
                return 0;
            }

            var (populated, shape) = describe(body);

            log.Line(
                "  {0,-44} {1}  x-eresult {2}  {3,-9}  {4} bytes; {5}",
                endpoint,
                (int)response.StatusCode,
                eresult,
                populated ? "POPULATED" : "EMPTY",
                Encoding.UTF8.GetByteCount(body),
                shape);

            if (error is not null)
            {
                log.Line(
                    "  {0,-44} x-error_message: {1}",
                    string.Empty, SteamSignInProbeFacts.Redact(error));
            }

            return populated ? 1 : 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Type and message, and the message redacted: an inner exception can
            // quote the request URI, and the request URI carries the token.
            log.Line(
                "  {0,-44} request failed ({1}): {2}",
                endpoint, ex.GetType().Name, SteamSignInProbeFacts.Redact(ex.Message));
            return 0;
        }
    }

    private static (bool Populated, string Shape) DescribeLastPlayed(string body)
    {
        if (SteamHistoryJson.TryReadLastPlayedTimes(body) is not { } games)
        {
            return (false, "no games array (the bare envelope, or a shape the shipped parser rejects)");
        }

        var withFirstPlayed = games.Count(static g => g.FirstPlayedUtc is not null);
        return (games.Count > 0, string.Create(
            CultureInfo.InvariantCulture,
            $"{games.Count} apps, {withFirstPlayed} carrying a first-played date"));
    }

    private static (bool Populated, string Shape) DescribeOwnedGames(string body)
    {
        if (SteamWebJson.TryReadOwnedGames(body) is not { } games)
        {
            return (false, "no games array (the bare envelope, or a shape the shipped parser rejects)");
        }

        var named = games.Count(static g => !string.IsNullOrWhiteSpace(g.Title));
        return (games.Count > 0, string.Create(
            CultureInfo.InvariantCulture, $"{games.Count} games, {named} carrying a name"));
    }

    private static (bool Populated, string Shape) DescribeYearInReview(string body, int year)
    {
        if (SteamHistoryJson.TryReadYearInReview(body) is not { } payload)
        {
            return (false, string.Create(
                CultureInfo.InvariantCulture,
                $"{year}: no stats block (the bare envelope, or a year with no Steam Replay)"));
        }

        var months = payload.Games
            .SelectMany(static g => g.Months)
            .Select(static m => m.Ordinal)
            .Distinct()
            .Count();

        var points = payload.Games.Sum(static g => g.Months.Count);

        return (payload.Games.Count > 0, string.Create(
            CultureInfo.InvariantCulture,
            $"{year}: {payload.Games.Count} games, {points} monthly points across {months} distinct months, "
            + $"account id {payload.AccountId?.ToString(CultureInfo.InvariantCulture) ?? "absent"}"));
    }

    private static SteamId? ResolveSteamId(string? value)
        => SteamId.TryParse(value, out var parsed) ? parsed : null;

    private static string Lifetime(DateTimeOffset expiry)
    {
        var remaining = expiry - DateTimeOffset.UtcNow;
        return remaining <= TimeSpan.Zero
            ? "already expired"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)remaining.TotalHours}h {remaining.Minutes}m");
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? string.Join(" ", values) : null;

    private static void WriteLimits(SteamProbeLog log)
    {
        log.Line();
        log.Line("WHAT THIS RUN DOES NOT PROVE");
        log.Line("  - Whether the token still works tomorrow. It reads one exp claim, once.");
        log.Line("  - Whether steamRefresh_steam survives in this profile, or re-mints days later.");
        log.Line("    The profile was private and has already been deleted.");
        log.Line("  - Whether a scheduler could renew this token unattended. Nothing was persisted.");
        log.Line("  - Whether token auth discloses more than key auth. That needs a same-account A/B.");
        log.Line("  - Whether steamid may be omitted under token auth. Both calls sent one.");
        log.Line();
    }
}
