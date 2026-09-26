using Winnow.Core.Ingest;
using Winnow.Ingest.Steam.AccountPages;

namespace Winnow.App.Services;

/// <summary>
/// ROADMAP M5 item 3: does two things per pass. First, it records every parsed
/// row from both account pages as a fact in the <c>account_transactions</c> /
/// <c>account_licenses</c> tables (migration 0014), regardless of whether the
/// row matches an owned release. Second, it records acquisition observations
/// against matching ownerships with the captured account's provenance.
///
/// <para>This service writes ONLY to existing ownerships. It never creates works,
/// releases or ownerships; that is the resolver's job (§5.1). An unmatched title
/// is counted, not resolved.</para>
/// </summary>
public interface ISteamAccountPageImport
{
    /// <summary>Imports captured Steam account pages and returns the committed result.</summary>
    Task<SteamAccountPageImportReport> ImportAsync(
        SteamAccountPages pages, CancellationToken ct = default);
}

/// <summary>
/// What one import pass did. Every field is a count or a flag, never a payload.
/// Reports truncation so a caller knows the pass saw part of the account, not all
/// of it.
/// </summary>
public sealed record SteamAccountPageImportReport
{
    /// <summary>How the licences page parse ended.</summary>
    public SteamAccountPageParseOutcome LicensesOutcome { get; init; } = SteamAccountPageParseOutcome.Absent;

    /// <summary>How the purchase-history page parse ended.</summary>
    public SteamAccountPageParseOutcome HistoryOutcome { get; init; } = SteamAccountPageParseOutcome.Absent;

    /// <summary>Why the licences page was not recognised, if applicable.</summary>
    public string? LicensesFailureReason { get; init; }

    /// <summary>Why the purchase-history page was not recognised, if applicable.</summary>
    public string? HistoryFailureReason { get; init; }

    /// <summary>Licence rows successfully parsed.</summary>
    public int LicenseRowsParsed { get; init; }

    /// <summary>Licence rows the parser could not interpret.</summary>
    public int LicenseRowsSkippedByParser { get; init; }

    /// <summary>Licence rows whose acquisition method did not map to a known type.</summary>
    public int LicenseRowsUnmappedAcquisition { get; init; }

    /// <summary>Purchase-history rows successfully parsed.</summary>
    public int HistoryRowsParsed { get; init; }

    /// <summary>Purchase-history rows the parser could not interpret.</summary>
    public int HistoryRowsSkippedByParser { get; init; }

    /// <summary>Whether the licences page was a partial view of the account.</summary>
    public bool LicensesTruncated { get; init; }

    /// <summary>Whether the purchase-history page was a partial view of the account.</summary>
    public bool HistoryTruncated { get; init; }

    /// <summary>Total licence count the paginator reported, or null when no paginator was found.</summary>
    public int? LicensesReportedTotal { get; init; }

    /// <summary>Steam ownerships in the title index (excluding provisional names and ambiguous keys).</summary>
    public int SteamOwnershipsConsidered { get; init; }

    /// <summary>Distinct normalised keys where two owned releases collided, making neither matchable.</summary>
    public int OwnershipsAmbiguousByTitle { get; init; }

    /// <summary>Licence rows that matched an ownership and contributed a date or licence type.</summary>
    public int AcquisitionsMatched { get; init; }

    /// <summary>Purchase-history rows that matched an ownership and contributed a price.</summary>
    public int PricesMatched { get; init; }

    /// <summary>Ownership rows that received at least one new column value.</summary>
    public int OwnershipsFilled { get; init; }

    /// <summary>Ownership rows that matched but already had values in every offered column.</summary>
    public int OwnershipsAlreadyComplete { get; init; }

    /// <summary>Page rows with no title match against the owned Steam releases.</summary>
    public int SkippedNoOwnershipMatch { get; init; }

    /// <summary>Page rows whose normalised title was ambiguous between two owned releases.</summary>
    public int SkippedAmbiguousTitle { get; init; }

    /// <summary>Ownerships where two page rows resolved to the same id and disagreed.</summary>
    public int SkippedConflictingRows { get; init; }

    /// <summary>Multi-item purchase rows whose price cannot be split across items (§4.7).</summary>
    public int SkippedBundleRows { get; init; }

    /// <summary>Purchase rows marked as refunded. A refunded purchase is money the user did not spend.</summary>
    public int SkippedRefundedRows { get; init; }

    /// <summary>Purchase rows whose type is not "Purchase" (gifts, in-game, refunds).</summary>
    public int SkippedNonPurchaseRows { get; init; }

    /// <summary>Purchase rows with no product name (wallet movements, redemptions).</summary>
    public int SkippedNonProductRows { get; init; }

    /// <summary>Transaction rows this pass wrote to the fact table.</summary>
    public int TransactionFactsRecorded { get; init; }

    /// <summary>Transaction rows this pass found already recorded from an earlier capture.</summary>
    public int TransactionFactsAlreadyRecorded { get; init; }

    /// <summary>Licence rows this pass wrote to the fact table.</summary>
    public int LicenseFactsRecorded { get; init; }

    /// <summary>Licence rows this pass found already recorded from an earlier capture.</summary>
    public int LicenseFactsAlreadyRecorded { get; init; }

    /// <summary>Wall-clock time for the whole pass.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Whether any ownership row was actually written to.</summary>
    public bool WroteAnything => OwnershipsFilled > 0;

    /// <summary>
    /// Whether the licences page was present and read as a licences table.
    ///
    /// <para>Stated as a flag rather than leaving callers to compare
    /// <see cref="LicensesOutcome"/> themselves: the UI has no business naming
    /// the parser's vocabulary, and §5.1's boundary is kept by the App layer
    /// answering the question rather than by a view model learning an ingest
    /// enum.</para>
    /// </summary>
    public bool LicensesParsed => LicensesOutcome == SteamAccountPageParseOutcome.Parsed;

    /// <summary>The licences document was present and its structure did not match.</summary>
    public bool LicensesUnrecognized => LicensesOutcome == SteamAccountPageParseOutcome.NotRecognized;

    /// <summary>Whether the purchase-history page was present and read as a history table.</summary>
    public bool HistoryParsed => HistoryOutcome == SteamAccountPageParseOutcome.Parsed;

    /// <summary>The purchase-history document was present and its structure did not match.</summary>
    public bool HistoryUnrecognized => HistoryOutcome == SteamAccountPageParseOutcome.NotRecognized;

    /// <summary>The pass that did nothing: no pages, disabled, or nothing to import.</summary>
    public static SteamAccountPageImportReport Nothing(TimeSpan elapsed) => new() { Elapsed = elapsed };
}
