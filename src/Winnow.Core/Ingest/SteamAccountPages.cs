using System.Globalization;
using System.Text;

namespace Winnow.Core.Ingest;

/// <summary>Which of the two Steam account pages a captured document is.</summary>
public enum SteamAccountPageKind
{
    /// <summary><c>store.steampowered.com/account/licenses/</c>: every product ever added, with its acquisition route.</summary>
    Licenses = 0,

    /// <summary><c>store.steampowered.com/account/history/</c>: purchase history with amounts.</summary>
    PurchaseHistory = 1,
}

/// <summary>How a set of pages reached Winnow.</summary>
public enum SteamAccountPageSource
{
    /// <summary>Captured from an embedded browser session the user signed in to and watched.</summary>
    EmbeddedSession = 0,

    /// <summary>Read from files the user saved out of their own browser.</summary>
    SavedFile = 1,
}

/// <summary>
/// The two Steam account pages, as raw HTML, however they were obtained.
///
/// <para>This is the seam between capture and parsing. The embedded-session
/// harvester and the saved-file route both produce one of these, and the parser
/// consumes it without knowing which route ran, so a change to either route
/// cannot reach the parser, and the parser can be tested against fixtures with
/// no browser anywhere near it.</para>
///
/// <para><b>Sensitive.</b> The purchase-history document contains what the user
/// bought and what they paid. Nothing here is written to disk by the type
/// itself, and <see cref="ToString"/> is redacted so that a log line, a debugger
/// watch or a crash dump reports sizes rather than contents. A caller that wants
/// these on disk has to write them there deliberately.</para>
/// </summary>
public sealed record SteamAccountPages
{
    /// <summary>The rendered licenses page, or null when it was not captured.</summary>
    public string? LicensesHtml { get; init; }

    /// <summary>The rendered purchase-history page, or null when it was not captured.</summary>
    public string? HistoryHtml { get; init; }

    /// <summary>When the capture happened. The parser records this against every fact it derives.</summary>
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>Which route produced these documents.</summary>
    public SteamAccountPageSource Source { get; init; } = SteamAccountPageSource.EmbeddedSession;

    /// <summary>Whether the licenses page is present and non-empty.</summary>
    public bool HasLicenses => !string.IsNullOrWhiteSpace(LicensesHtml);

    /// <summary>Whether the purchase-history page is present and non-empty.</summary>
    public bool HasHistory => !string.IsNullOrWhiteSpace(HistoryHtml);

    /// <summary>Both pages are present. A partial set is still worth parsing; this only says it is not one.</summary>
    public bool IsComplete => HasLicenses && HasHistory;

    /// <summary>Neither page is present, so there is nothing to parse.</summary>
    public bool IsEmpty => !HasLicenses && !HasHistory;

    /// <summary>The document for one page, or null when that page is absent.</summary>
    public string? Html(SteamAccountPageKind kind) => kind switch
    {
        SteamAccountPageKind.Licenses => LicensesHtml,
        SteamAccountPageKind.PurchaseHistory => HistoryHtml,
        _ => null,
    };

    /// <summary>This set with one page replaced. Records are immutable; capture happens one page at a time.</summary>
    public SteamAccountPages With(SteamAccountPageKind kind, string? html) => kind switch
    {
        SteamAccountPageKind.Licenses => this with { LicensesHtml = html },
        SteamAccountPageKind.PurchaseHistory => this with { HistoryHtml = html },
        _ => this,
    };

    /// <summary>UTF-8 size of one page. The only measure of a document that is safe to log.</summary>
    public int ByteCount(SteamAccountPageKind kind)
        => Html(kind) is { } html ? Encoding.UTF8.GetByteCount(html) : 0;

    /// <summary>UTF-8 size of everything held here.</summary>
    public int TotalByteCount
        => ByteCount(SteamAccountPageKind.Licenses) + ByteCount(SteamAccountPageKind.PurchaseHistory);

    /// <summary>Sizes and provenance only. The documents themselves are never rendered into a string.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"SteamAccountPages({Source}, licenses {ByteCount(SteamAccountPageKind.Licenses)} bytes, "
        + $"history {ByteCount(SteamAccountPageKind.PurchaseHistory)} bytes, "
        + $"captured {CapturedAt:u}, content redacted)");
}
