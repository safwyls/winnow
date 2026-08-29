using System.Globalization;
using Winnow.Core.Ingest;

namespace Winnow.Core.Auth;

/// <summary>How one harvest attempt ended. All outcomes are non-exceptional.</summary>
public enum SteamPageHarvestOutcome
{
    /// <summary>Both pages were captured.</summary>
    Captured = 0,

    /// <summary>One page was captured and the other was not. Worth parsing; not a complete run.</summary>
    Partial = 1,

    /// <summary>The user closed the window, or consent was never recorded. Never retried on its own.</summary>
    Cancelled = 2,

    /// <summary>No embedded browser can run here. The caller falls back to the saved-file route.</summary>
    Unavailable = 3,

    /// <summary>The session ran and produced nothing.</summary>
    Failed = 4,

    /// <summary>Nobody signed in, so Steam never rendered an account page. The remedy is to sign in, not to retry.</summary>
    NoSession = 5,
}

/// <summary>
/// One request to harvest the two Steam account pages from an embedded,
/// user-present browser session.
/// </summary>
public sealed record SteamPageHarvestRequest
{
    /// <summary>
    /// Whether the user has been shown what this does and agreed to it.
    ///
    /// <para>Required, and the harvester refuses to open a window without it.
    /// The consent surface itself belongs to the caller. This flow signs a user
    /// into Steam and reads what they bought, and the record that they agreed to
    /// that must not be something the mechanism can grant itself.</para>
    /// </summary>
    public required bool ConsentGranted { get; init; }

    /// <summary>
    /// How long the whole session may take, sign-in included.
    ///
    /// <para>Generous by default because most of it is the user typing a
    /// password, waiting for a Steam Guard code and reading a captcha.</para>
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many times the purchase-history load-more control may be clicked.
    /// Clamped to <see cref="SteamAccountPagePolicy.MaxAllowedLoadMoreClicks"/>.
    /// </summary>
    public int MaxLoadMoreClicks { get; init; } = SteamAccountPagePolicy.DefaultMaxLoadMoreClicks;

    /// <summary>
    /// How many further licences pages may be followed past the first. Clamped to
    /// <see cref="SteamAccountPagePolicy.MaxAllowedLicensesPages"/>.
    ///
    /// <para>The licences page shows a hundred licences at a time and paginates,
    /// so on any substantial account this is the difference between a hundred
    /// licences and all of them.</para>
    /// </summary>
    public int MaxLicensesPages { get; init; } = SteamAccountPagePolicy.DefaultMaxLicensesPages;
}

/// <summary>
/// The outcome of one harvest. Carries documents or a reason, never an exception.
///
/// <para><see cref="ToString"/> is redacted for the same reason
/// <see cref="SteamAccountPages.ToString"/> is: the thing this type carries is
/// the user's purchase history.</para>
/// </summary>
public sealed record SteamPageHarvestResult
{
    private SteamPageHarvestResult(
        SteamPageHarvestOutcome outcome,
        SteamAccountPages? pages,
        string? detail,
        int loadMoreClicks,
        SteamLoadMoreDecision? loadMoreStoppedBecause,
        int licensesPagesWalked,
        SteamLoadMoreDecision? licensesStoppedBecause)
    {
        Outcome = outcome;
        Pages = pages;
        Detail = detail;
        LoadMoreClicks = loadMoreClicks;
        LoadMoreStoppedBecause = loadMoreStoppedBecause;
        LicensesPagesWalked = licensesPagesWalked;
        LicensesStoppedBecause = licensesStoppedBecause;
    }

    /// <summary>How the attempt ended.</summary>
    public SteamPageHarvestOutcome Outcome { get; }

    /// <summary>The captured documents, or null when nothing was captured.</summary>
    public SteamAccountPages? Pages { get; }

    /// <summary>A safe one-line reason. Never contains page content.</summary>
    public string? Detail { get; }

    /// <summary>How many times the purchase-history load-more control was clicked.</summary>
    public int LoadMoreClicks { get; }

    /// <summary>
    /// Why the clicking stopped, or null when the history page was never reached.
    /// <see cref="SteamLoadMoreDecision.ReachedCap"/> means the document is
    /// truncated and the parser is not seeing the whole account.
    /// </summary>
    public SteamLoadMoreDecision? LoadMoreStoppedBecause { get; }

    /// <summary>
    /// How many further licences pages were followed past the first and merged
    /// into the captured document.
    ///
    /// <para>Zero means one page of at most a hundred licences, which is the
    /// whole account only for a small one. The parser reports truncation from the
    /// document's own paginator; this reports what the session did to try to
    /// avoid it, which is the number that says whether a truncated document is a
    /// Steam change or Winnow's own cap.</para>
    /// </summary>
    public int LicensesPagesWalked { get; }

    /// <summary>
    /// Why the licences walk stopped, or null when the licences page was never
    /// reached. <see cref="SteamLoadMoreDecision.ReachedCap"/> means Winnow
    /// stopped, not Steam.
    /// </summary>
    public SteamLoadMoreDecision? LicensesStoppedBecause { get; }

    /// <summary>Whether anything usable came back.</summary>
    public bool HasPages => Pages is { IsEmpty: false };

    /// <summary>Both pages arrived.</summary>
    public static SteamPageHarvestResult Captured(
        SteamAccountPages pages,
        int loadMoreClicks,
        SteamLoadMoreDecision? loadMoreStoppedBecause,
        int licensesPagesWalked = 0,
        SteamLoadMoreDecision? licensesStoppedBecause = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        return new(
            SteamPageHarvestOutcome.Captured, pages, null,
            loadMoreClicks, loadMoreStoppedBecause, licensesPagesWalked, licensesStoppedBecause);
    }

    /// <summary>One page arrived and the other did not.</summary>
    public static SteamPageHarvestResult Partial(
        SteamAccountPages pages,
        string? detail,
        int loadMoreClicks,
        SteamLoadMoreDecision? loadMoreStoppedBecause,
        int licensesPagesWalked = 0,
        SteamLoadMoreDecision? licensesStoppedBecause = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        return new(
            SteamPageHarvestOutcome.Partial, pages, detail,
            loadMoreClicks, loadMoreStoppedBecause, licensesPagesWalked, licensesStoppedBecause);
    }

    /// <summary>The user backed out, or never agreed in the first place.</summary>
    public static SteamPageHarvestResult Cancelled(string? detail = null)
        => new(SteamPageHarvestOutcome.Cancelled, null, detail, 0, null, 0, null);

    /// <summary>No embedded browser here.</summary>
    public static SteamPageHarvestResult Unavailable(string? detail = null)
        => new(SteamPageHarvestOutcome.Unavailable, null, detail, 0, null, 0, null);

    /// <summary>The session ran and produced nothing.</summary>
    public static SteamPageHarvestResult Failed(string? detail = null)
        => new(SteamPageHarvestOutcome.Failed, null, detail, 0, null, 0, null);

    /// <summary>Nobody signed in.</summary>
    public static SteamPageHarvestResult NoSession(string? detail = null)
        => new(SteamPageHarvestOutcome.NoSession, null, detail, 0, null, 0, null);

    /// <summary>Sizes and outcome only. The documents are never rendered into a string.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"SteamPageHarvestResult({Outcome}"
        + $"{(Detail is null ? string.Empty : ": " + Detail)}"
        + $"{(Pages is null ? string.Empty : ", " + Pages)})");
}

/// <summary>
/// Signs the user into Steam in a browser they can see, then captures the two
/// account pages. Contract only (no IO); the implementation lives in
/// Winnow.Auth.WebView.
///
/// <para>Expected failures return a <see cref="SteamPageHarvestResult"/> with a
/// reason, never throw. A host with no embedded browser gets
/// <see cref="SteamPageHarvestOutcome.Unavailable"/> and falls back to the
/// saved-file route, which produces the same
/// <see cref="SteamAccountPages"/>.</para>
/// </summary>
public interface ISteamAccountPageHarvester
{
    /// <summary>Short human name, for logs and for telling the user which route ran.</summary>
    string Name { get; }

    /// <summary>Whether this can run on this machine right now. Must not open a window or do IO.</summary>
    ValueTask<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Opens the session, waits for the user to sign in, and captures the two pages.</summary>
    Task<SteamPageHarvestResult> HarvestAsync(SteamPageHarvestRequest request, CancellationToken ct = default);
}
