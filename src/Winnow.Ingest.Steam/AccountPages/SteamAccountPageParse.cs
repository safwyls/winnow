namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// How one page parse attempt ended. NotRecognized means the document's overall
/// structure does not match the expected table, so it is refused with a reason
/// instead of producing junk rows. Absent means the page was not captured at all.
/// </summary>
public enum SteamAccountPageParseOutcome
{
    /// <summary>The document was recognised and rows were extracted.</summary>
    Parsed = 0,

    /// <summary>The document was present but its structure did not match. Carries a reason.</summary>
    NotRecognized = 1,

    /// <summary>No document was provided for this page.</summary>
    Absent = 2,
}

/// <summary>
/// Parse result for the licences page. Carries rows plus counters for skipped,
/// unmapped and unparsed items so a Steam redesign shows up as a count rather
/// than as silent data loss. <see cref="IsTruncated"/> reports whether this
/// document is a partial view of the account, detected via the paginator.
/// </summary>
public sealed record SteamLicensesPageResult
{
    /// <summary>How the parse ended.</summary>
    public required SteamAccountPageParseOutcome Outcome { get; init; }

    /// <summary>Why the document was not recognised, when <see cref="Outcome"/> is <see cref="SteamAccountPageParseOutcome.NotRecognized"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>The licence rows successfully extracted.</summary>
    public IReadOnlyList<SteamLicenseRow> Rows { get; init; } = [];

    /// <summary>Table rows the parser could not interpret. Counted, never guessed at.</summary>
    public int SkippedRows { get; init; }

    /// <summary>Rows whose acquisition method did not map to a known licence type.</summary>
    public int RowsWithUnmappedAcquisition { get; init; }

    /// <summary>Rows whose date cell could not be parsed.</summary>
    public int RowsWithUnparsedDate { get; init; }

    /// <summary>The total licence count reported by the paginator ("Showing X-Y of Z"), or null when no paginator was found.</summary>
    public int? TotalLicensesReported { get; init; }

    /// <summary>Whether a next-page link exists in the paginator.</summary>
    public bool HasNextPage { get; init; }

    /// <summary>Whether this document is a partial view of the account, either because a next page exists or because the parsed count falls short of the reported total.</summary>
    public bool IsTruncated => HasNextPage
        || (TotalLicensesReported is { } total && Rows.Count + SkippedRows < total);

    /// <summary>No document was provided.</summary>
    public static SteamLicensesPageResult Absent()
        => new() { Outcome = SteamAccountPageParseOutcome.Absent };

    /// <summary>The document was present but not a recognisable licences table.</summary>
    public static SteamLicensesPageResult NotRecognized(string reason)
        => new() { Outcome = SteamAccountPageParseOutcome.NotRecognized, FailureReason = reason };
}

/// <summary>
/// Parse result for the purchase-history page. Carries rows plus counters for
/// skipped and unparsed items. <see cref="IsTruncated"/> reports whether a
/// visible load-more control indicates more data exists beyond what was captured.
/// </summary>
public sealed record SteamPurchaseHistoryPageResult
{
    /// <summary>How the parse ended.</summary>
    public required SteamAccountPageParseOutcome Outcome { get; init; }

    /// <summary>Why the document was not recognised, when <see cref="Outcome"/> is <see cref="SteamAccountPageParseOutcome.NotRecognized"/>.</summary>
    public string? FailureReason { get; init; }

    /// <summary>The purchase rows successfully extracted.</summary>
    public IReadOnlyList<SteamPurchaseRow> Rows { get; init; } = [];

    /// <summary>Table rows the parser could not interpret. Counted, never guessed at.</summary>
    public int SkippedRows { get; init; }

    /// <summary>Rows whose date cell could not be parsed.</summary>
    public int RowsWithUnparsedDate { get; init; }

    /// <summary>Rows that had a base price but whose total could not be parsed.</summary>
    public int RowsWithUnparsedTotal { get; init; }

    /// <summary>Whether the <c>#load_more_button</c> is present and not hidden. Steam's own script hides it with jQuery when exhausted rather than removing it, so the parser tests the inline display style.</summary>
    public bool HasMoreToLoad { get; init; }

    /// <summary>Whether this document is a partial view of the account.</summary>
    public bool IsTruncated => HasMoreToLoad;

    /// <summary>No document was provided.</summary>
    public static SteamPurchaseHistoryPageResult Absent()
        => new() { Outcome = SteamAccountPageParseOutcome.Absent };

    /// <summary>The document was present but not a recognisable purchase-history table.</summary>
    public static SteamPurchaseHistoryPageResult NotRecognized(string reason)
        => new() { Outcome = SteamAccountPageParseOutcome.NotRecognized, FailureReason = reason };
}

/// <summary>
/// The combined parse result for both account pages. A partial set (one page
/// parsed, the other absent) is still worth importing.
/// </summary>
/// <param name="Licenses">Parse result for the licences page.</param>
/// <param name="History">Parse result for the purchase-history page.</param>
public sealed record SteamAccountPageParseResult(
    SteamLicensesPageResult Licenses,
    SteamPurchaseHistoryPageResult History)
{
    /// <summary>Whether at least one page produced rows.</summary>
    public bool AnythingParsed =>
        Licenses.Outcome == SteamAccountPageParseOutcome.Parsed
        || History.Outcome == SteamAccountPageParseOutcome.Parsed;

    /// <summary>Whether either page is a partial view of the account.</summary>
    public bool IsTruncated => Licenses.IsTruncated || History.IsTruncated;
}
