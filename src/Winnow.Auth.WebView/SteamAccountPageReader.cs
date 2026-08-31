using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Winnow.Core.Auth;
using Winnow.Core.Ingest;

namespace Winnow.Auth.WebView;

/// <summary>
/// Reads one Steam account page out of a live browser: exhaust the list, then
/// take the rendered document in slices.
///
/// <para><b>Extracted, not written.</b> Every method here was
/// <see cref="WebView2SteamPageHarvester"/>'s and moved unchanged when the S3
/// sign-in session needed the identical pipeline for a capture the user had
/// separately agreed to. Two copies of "click load-more until the list stops
/// growing" would be two places for a Steam redesign to be half-fixed, and the
/// counters below are exactly the truncation evidence both results report.</para>
///
/// <para>What stayed behind in each caller is everything this class deliberately
/// does not decide: the window, the navigation gate, the teardown order, and
/// whether a page may be read at all. This class runs scripts in a document
/// somebody else has already decided is in scope, and it holds no state that
/// outlives the read but the counters.</para>
/// </summary>
internal sealed class SteamAccountPageReader
{
    /// <summary>How long to wait for a load-more click to produce rows before treating it as stalled.</summary>
    private static readonly TimeSpan LoadMoreGrowthTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How often to re-count rows while waiting for a click to land.</summary>
    private static readonly TimeSpan LoadMorePollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Characters per slice when reading a captured document back out of the page.
    ///
    /// <para>A script result crosses WebView2's IPC as one JSON string. An account
    /// with a decade of purchases produces a document of several megabytes, which
    /// is enough to make a single return a gamble; 128k characters is not.</para>
    /// </summary>
    private const int CaptureChunkChars = 128 * 1024;

    private readonly SteamAccountPagePolicy _policy;
    private readonly ILogger _log;
    private readonly Action<string> _say;

    /// <param name="policy">Owns both caps and both stop rules. Never re-decided here.</param>
    /// <param name="log">Never given page content, a URL query or a credential.</param>
    /// <param name="say">Updates the caller's status line. Never given page content.</param>
    public SteamAccountPageReader(SteamAccountPagePolicy policy, ILogger log, Action<string> say)
    {
        _policy = policy;
        _log = log;
        _say = say;
    }

    /// <summary>Clicks made on the purchase-history load-more control.</summary>
    public int LoadMoreClicks { get; private set; }

    /// <summary>Why the clicking stopped, or null when the history page was never read.</summary>
    public SteamLoadMoreDecision? LoadMoreStop { get; private set; }

    /// <summary>Licences pages followed past the first and merged into the captured document.</summary>
    public int LicensesPagesWalked { get; private set; }

    /// <summary>Why the licences walk stopped, or null when the licences page was never read.</summary>
    public SteamLoadMoreDecision? LicensesStop { get; private set; }

    /// <summary>Asks the page whether it is being rendered for a signed-in account.</summary>
    public static async Task<bool> IsSignedInAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.SignedInProbe);

        return ReadObject(raw) is { } root
            && root.TryGetProperty("signedIn", out var signedIn)
            && signedIn.ValueKind == JsonValueKind.True;
    }

    /// <summary>
    /// Exhausts one page's list and returns its rendered HTML, or null when the
    /// document could not be read in full.
    /// </summary>
    /// <param name="browser">The live session. Already known to be on the page named by <paramref name="kind"/>.</param>
    /// <param name="kind">Which page this is. Decides which of the two list mechanisms is driven.</param>
    /// <param name="done">Whether the caller has already finished, checked between steps.</param>
    /// <param name="ct">Cancels the walk between steps.</param>
    public async Task<string?> ReadAsync(
        CoreWebView2 browser, SteamAccountPageKind kind, Func<bool> done, CancellationToken ct)
    {
        if (kind == SteamAccountPageKind.PurchaseHistory)
        {
            _say("Loading your purchase history…");
            await LoadEverythingAsync(browser, done, ct);
        }
        else
        {
            _say("Reading your licenses page…");
            await GatherLicensesPagesAsync(browser, done, ct);
        }

        return await CaptureAsync(browser);
    }

    /// <summary>
    /// Clicks the purchase-history load-more control until the list is exhausted,
    /// the cap is reached, or a click stops producing rows.
    /// </summary>
    private async Task LoadEverythingAsync(CoreWebView2 browser, Func<bool> done, CancellationToken ct)
    {
        await TryExecuteAsync(browser, SteamHarvestScripts.DefineHelpers);

        var rowsBefore = -1;

        while (!done() && !ct.IsCancellationRequested)
        {
            var (present, rows) = await ReadLoadMoreStateAsync(browser);
            var decision = _policy.ClassifyLoadMore(LoadMoreClicks, rowsBefore, rows, present);

            if (decision != SteamLoadMoreDecision.Continue)
            {
                LoadMoreStop = decision;
                break;
            }

            rowsBefore = rows;

            if (!await ClickLoadMoreAsync(browser))
            {
                LoadMoreStop = SteamLoadMoreDecision.Exhausted;
                break;
            }

            LoadMoreClicks++;
            _say(string.Create(
                CultureInfo.CurrentCulture,
                $"Loading your purchase history ({LoadMoreClicks} of at most {_policy.MaxLoadMoreClicks} pages)…"));

            await WaitForMoreRowsAsync(browser, rowsBefore, ct);
        }

        _log.LogInformation(
            "Expanded the Steam purchase history with {Clicks} load-more clicks; stopped because: {Reason}.",
            LoadMoreClicks,
            LoadMoreStop?.ToString() ?? "the session ended");
    }

    /// <summary>
    /// Follows the licences paginator, merging each page's rows into the live
    /// document, until the paginator runs out, the cap is reached or a page
    /// stops adding rows.
    ///
    /// <para>Verified 2026-08-29: the licences page shows a hundred licences at a
    /// time. Without this, a 979-licence account is captured as 100 licences and
    /// the parser correctly reports a truncated document, which is an honest
    /// answer to the wrong question.</para>
    /// </summary>
    private async Task GatherLicensesPagesAsync(CoreWebView2 browser, Func<bool> done, CancellationToken ct)
    {
        await TryExecuteAsync(browser, SteamHarvestScripts.DefineHelpers);
        await TryExecuteAsync(browser, SteamHarvestScripts.LicensesWalkHelpers);

        var rowsBefore = -1;

        while (!done() && !ct.IsCancellationRequested)
        {
            var (hasNext, rows) = await ReadLicensesStateAsync(browser);
            var decision = _policy.ClassifyLicensesPage(LicensesPagesWalked, rowsBefore, rows, hasNext);

            if (decision != SteamLoadMoreDecision.Continue)
            {
                LicensesStop = decision;
                break;
            }

            rowsBefore = rows;

            if (!await StartLicensesFetchAsync(browser))
            {
                LicensesStop = SteamLoadMoreDecision.Exhausted;
                break;
            }

            LicensesPagesWalked++;
            _say(string.Create(
                CultureInfo.CurrentCulture,
                $"Reading your licenses page ({LicensesPagesWalked + 1} of at most {_policy.MaxLicensesPages + 1})…"));

            await WaitForLicensesFetchAsync(browser, rowsBefore, ct);
        }

        _log.LogInformation(
            "Followed the Steam licences paginator for {Pages} further pages; stopped because: {Reason}.",
            LicensesPagesWalked,
            LicensesStop?.ToString() ?? "the session ended");
    }

    /// <summary>
    /// Waits for a licences fetch to finish and its rows to land.
    ///
    /// <para>Two conditions rather than one: the fetch reporting itself done says
    /// the network is finished, and the row count says the merge actually
    /// happened. A fetch that succeeded but merged nothing is caught by the next
    /// probe as a stall.</para>
    /// </summary>
    private static async Task WaitForLicensesFetchAsync(
        CoreWebView2 browser, int rowsBefore, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + LoadMoreGrowthTimeout;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(LoadMorePollInterval, ct);

            var raw = await TryExecuteAsync(browser, SteamHarvestScripts.LicensesWalkState);
            var pending = ReadObject(raw) is { } state
                && state.TryGetProperty("pending", out var flag)
                && flag.ValueKind == JsonValueKind.True;

            if (pending)
            {
                continue;
            }

            var (_, rows) = await ReadLicensesStateAsync(browser);

            if (rows > rowsBefore)
            {
                return;
            }

            // The fetch is over and produced nothing. Waiting out the rest of the
            // deadline would only delay the stall the policy is about to declare.
            return;
        }
    }

    /// <summary>Reads whether the licences paginator offers another page, and how many rows are rendered.</summary>
    private static async Task<(bool HasNext, int Rows)> ReadLicensesStateAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.LicensesPaginatorProbe);

        if (ReadObject(raw) is not { } root)
        {
            return (false, 0);
        }

        var hasNext = root.TryGetProperty("nextUrl", out var next)
            && next.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(next.GetString());

        var rows = root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Number
            && r.TryGetInt32(out var count)
                ? count
                : 0;

        return (hasNext, rows);
    }

    private static async Task<bool> StartLicensesFetchAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.FetchNextLicensesPage);
        return string.Equals(raw?.Trim(), "true", StringComparison.Ordinal);
    }

    /// <summary>Waits for a click to actually add rows, so the next probe measures a settled page.</summary>
    private static async Task WaitForMoreRowsAsync(CoreWebView2 browser, int rowsBefore, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + LoadMoreGrowthTimeout;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(LoadMorePollInterval, ct);

            var (_, rows) = await ReadLoadMoreStateAsync(browser);

            if (rows > rowsBefore)
            {
                return;
            }
        }

        // Falling out is not a failure here. The next probe sees an unchanged row
        // count and the policy calls it stalled, which is where that judgement
        // belongs.
    }

    /// <summary>
    /// Takes the rendered document out of the page in slices.
    ///
    /// <para>An incomplete read is discarded rather than returned. A truncated
    /// HTML document parses perfectly well and silently omits whatever was cut
    /// off, which would show up as an account that owns fewer games than it
    /// does; a wrong answer is worse here than a missing one.</para>
    /// </summary>
    private async Task<string?> CaptureAsync(CoreWebView2 browser)
    {
        var length = await ReadNumberAsync(browser, SteamHarvestScripts.BeginCapture);

        try
        {
            if (length is null or <= 0)
            {
                return null;
            }

            var builder = new System.Text.StringBuilder(length.Value);

            for (var offset = 0; offset < length.Value; offset += CaptureChunkChars)
            {
                var slice = await ReadStringAsync(
                    browser, SteamHarvestScripts.Chunk(offset, CaptureChunkChars));

                if (string.IsNullOrEmpty(slice))
                {
                    break;
                }

                builder.Append(slice);
            }

            if (builder.Length != length.Value)
            {
                _log.LogWarning(
                    "Read {Read} of {Total} characters from {Origin} before the page changed underneath the "
                    + "capture.",
                    builder.Length, length.Value, _policy.HarvestOrigin);

                return null;
            }

            return builder.ToString();
        }
        finally
        {
            // Leaves no copy of the user's purchase history sitting in a global
            // the page's own script could reach.
            await TryExecuteAsync(browser, SteamHarvestScripts.EndCapture);
        }
    }

    /// <summary>Reads whether a load-more control is on the page, and how much is rendered.</summary>
    private static async Task<(bool Present, int Rows)> ReadLoadMoreStateAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.LoadMoreProbe);

        if (ReadObject(raw) is not { } root)
        {
            return (false, 0);
        }

        var present = root.TryGetProperty("present", out var p) && p.ValueKind == JsonValueKind.True;
        var rows = root.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Number
            && r.TryGetInt32(out var count)
                ? count
                : 0;

        return (present, rows);
    }

    private static async Task<bool> ClickLoadMoreAsync(CoreWebView2 browser)
    {
        var raw = await TryExecuteAsync(browser, SteamHarvestScripts.ClickLoadMore);
        return string.Equals(raw?.Trim(), "true", StringComparison.Ordinal);
    }

    /// <summary>A script result that is a JSON number.</summary>
    private static async Task<int?> ReadNumberAsync(CoreWebView2 browser, string script)
    {
        var raw = await TryExecuteAsync(browser, script);

        if (raw is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Number
                && document.RootElement.TryGetInt32(out var value)
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A script result that is a JSON string.</summary>
    private static async Task<string?> ReadStringAsync(CoreWebView2 browser, string script)
    {
        var raw = await TryExecuteAsync(browser, script);

        if (raw is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A script result that is a JSON object, cloned out of the document so the
    /// element outlives the <c>using</c>.
    /// </summary>
    internal static JsonElement? ReadObject(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Runs a script and returns its raw JSON result, or null when the browser
    /// went away.
    ///
    /// <para>Callers are all in the middle of a page that may navigate, close or
    /// crash under them, and none of them has anything useful to do about it
    /// beyond stopping.</para>
    /// </summary>
    internal static async Task<string?> TryExecuteAsync(CoreWebView2 browser, string script)
    {
        try
        {
            var result = await browser.ExecuteScriptAsync(script);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or ObjectDisposedException
            or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
