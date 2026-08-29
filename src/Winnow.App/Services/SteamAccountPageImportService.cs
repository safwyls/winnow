using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Matching;
using Winnow.Core.Repositories;
using Winnow.Ingest.Steam.AccountPages;

namespace Winnow.App.Services;

/// <summary>
/// ROADMAP M5 item 3: imports acquisition facts (date, licence type, price) from
/// the two Steam account pages into existing ownership rows.
///
/// <para>This service writes ONLY to existing ownerships. It never creates works,
/// releases or ownerships; that is the resolver's job (§5.1). An unmatched title
/// is counted, not resolved.</para>
/// </summary>
public interface ISteamAccountPageImport
{
    /// <inheritdoc cref="SteamAccountPageImportService.ImportAsync"/>
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

/// <summary>
/// Implements <see cref="ISteamAccountPageImport"/>.
///
/// <para>Matching is conservative per §5.3: exact-or-normalized title match via
/// <see cref="TitleNormalizer"/> against the user's owned Steam releases. Nothing
/// fuzzy is ever auto-applied. If two owned releases normalise to the same key,
/// that key is ambiguous and neither is touched. Provisional names (machine-minted
/// placeholders) are excluded because a match against them could only ever be
/// accidental.</para>
///
/// <para>Price is applied only from a single-item, non-refunded row whose type is
/// exactly "Purchase". A bundle row's price is never split across its items
/// (§4.7). A gift purchase is a game bought for somebody else, so its price
/// belongs to no ownership of this account's. An in-game purchase is the price of
/// an item inside a game, not of the game. A refunded purchase is money the user
/// did not spend.</para>
///
/// <para>When two page rows resolve to the same ownership and disagree, the
/// ownership is left entirely alone and counted. The licences page's date wins
/// over the history page's; the history date is a fallback for when only that page
/// was captured.</para>
/// </summary>
public sealed class SteamAccountPageImportService : ISteamAccountPageImport
{
    private readonly IOwnershipRepository _ownerships;
    private readonly IReleaseRepository _releases;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly LibrarySyncGate _gate;
    private readonly ILogger<SteamAccountPageImportService> _logger;

    public SteamAccountPageImportService(
        IOwnershipRepository ownerships,
        IReleaseRepository releases,
        IUnitOfWorkFactory unitOfWork,
        LibrarySyncGate gate,
        ILogger<SteamAccountPageImportService> logger)
    {
        _ownerships = ownerships;
        _releases = releases;
        _unitOfWork = unitOfWork;
        _gate = gate;
        _logger = logger;
    }

    /// <summary>
    /// Parses both pages and fills acquisition facts into matching ownership rows.
    /// Safe to re-run: every column write is COALESCE(stored, incoming) through
    /// <see cref="IOwnershipRepository.FillAcquisitionFactsAsync"/>.
    /// </summary>
    public async Task<SteamAccountPageImportReport> ImportAsync(
        SteamAccountPages pages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var started = Stopwatch.GetTimestamp();

        if (pages.IsEmpty)
        {
            return SteamAccountPageImportReport.Nothing(Stopwatch.GetElapsedTime(started));
        }

        var parsed = SteamAccountPageReader.Read(pages);
        var index = await BuildIndexAsync(ct).ConfigureAwait(false);

        var pending = new Dictionary<long, PendingFill>();
        var counters = new Counters();

        ApplyLicenses(parsed.Licenses, index, pending, counters);
        ApplyHistory(parsed.History, index, pending, counters);

        // ── Write phase. One transaction for the whole pass, under the same
        // gate the resolver and the playtime backfill take, so a user clicking
        // Import and the startup pipeline's resolver pass never open concurrent
        // write transactions on SQLite's single writer. Everything above this
        // line is parsing and reading and holds no lock.
        var filled = 0;
        var alreadyComplete = 0;

        using (await _gate.EnterAsync(ct).ConfigureAwait(false))
        {
            using var scope = _unitOfWork.Begin();

            foreach (var (ownershipId, fill) in pending)
            {
                ct.ThrowIfCancellationRequested();

                if (fill.Conflicted)
                {
                    counters.SkippedConflictingRows++;
                    continue;
                }

                var write = new OwnershipAcquisitionFill(
                    ownershipId,
                    fill.AcquiredAt,
                    fill.LicenseType,
                    fill.PricePaidCents,
                    fill.PricePaidCents is null ? null : PriceSources.SteamAccountHistory);

                if (!write.HasAnythingToWrite)
                {
                    continue;
                }

                if (await _ownerships.FillAcquisitionFactsAsync(write, ct).ConfigureAwait(false))
                {
                    filled++;
                }
                else
                {
                    alreadyComplete++;
                }
            }

            scope.Commit();
        }

        var report = new SteamAccountPageImportReport
        {
            LicensesOutcome = parsed.Licenses.Outcome,
            HistoryOutcome = parsed.History.Outcome,
            LicensesFailureReason = parsed.Licenses.FailureReason,
            HistoryFailureReason = parsed.History.FailureReason,
            LicenseRowsParsed = parsed.Licenses.Rows.Count,
            LicenseRowsSkippedByParser = parsed.Licenses.SkippedRows,
            LicenseRowsUnmappedAcquisition = parsed.Licenses.RowsWithUnmappedAcquisition,
            HistoryRowsParsed = parsed.History.Rows.Count,
            HistoryRowsSkippedByParser = parsed.History.SkippedRows,
            LicensesTruncated = parsed.Licenses.IsTruncated,
            HistoryTruncated = parsed.History.IsTruncated,
            LicensesReportedTotal = parsed.Licenses.TotalLicensesReported,
            SteamOwnershipsConsidered = index.Considered,
            OwnershipsAmbiguousByTitle = index.AmbiguousKeys.Count,
            AcquisitionsMatched = counters.AcquisitionsMatched,
            PricesMatched = counters.PricesMatched,
            OwnershipsFilled = filled,
            OwnershipsAlreadyComplete = alreadyComplete,
            SkippedNoOwnershipMatch = counters.SkippedNoOwnershipMatch,
            SkippedAmbiguousTitle = counters.SkippedAmbiguousTitle,
            SkippedConflictingRows = counters.SkippedConflictingRows,
            SkippedBundleRows = counters.SkippedBundleRows,
            SkippedRefundedRows = counters.SkippedRefundedRows,
            SkippedNonPurchaseRows = counters.SkippedNonPurchaseRows,
            SkippedNonProductRows = counters.SkippedNonProductRows,
            Elapsed = Stopwatch.GetElapsedTime(started),
        };

        _logger.LogInformation(
            "Steam account pages imported: {Filled} ownerships filled from {Licences} licence rows and "
            + "{History} history rows; {NoMatch} unmatched, {Ambiguous} ambiguous, {Bundles} bundle rows "
            + "left alone (licences truncated: {LicTrunc}, history truncated: {HistTrunc})",
            report.OwnershipsFilled, report.LicenseRowsParsed, report.HistoryRowsParsed,
            report.SkippedNoOwnershipMatch, report.SkippedAmbiguousTitle, report.SkippedBundleRows,
            report.LicensesTruncated, report.HistoryTruncated);

        return report;
    }

    private static void ApplyLicenses(
        SteamLicensesPageResult licenses, TitleIndex index,
        Dictionary<long, PendingFill> pending, Counters counters)
    {
        if (licenses.Outcome != SteamAccountPageParseOutcome.Parsed)
        {
            return;
        }

        foreach (var row in licenses.Rows)
        {
            if (row.AcquiredAtUtc is null && row.LicenseType is null)
            {
                continue;
            }

            if (!index.TryResolve(row.ItemName, counters, out var ownershipId))
            {
                continue;
            }

            counters.AcquisitionsMatched++;
            var fill = Pending(pending, ownershipId);

            // license_type is written only for an acquisition method this parser
            // recognises. An unmapped method is left null rather than turned
            // into a vocabulary value nobody defined.
            fill.OfferAcquisition(row.AcquiredAtUtc, row.LicenseType);
        }
    }

    private static void ApplyHistory(
        SteamPurchaseHistoryPageResult history, TitleIndex index,
        Dictionary<long, PendingFill> pending, Counters counters)
    {
        if (history.Outcome != SteamAccountPageParseOutcome.Parsed)
        {
            return;
        }

        foreach (var row in history.Rows)
        {
            // A wallet top-up or a gift-card redemption is not a product.
            if (!row.IsProductRow)
            {
                counters.SkippedNonProductRows++;
                continue;
            }

            // §4.7's bundle-attribution problem: one price covers every item in
            // the row and there is no way to split it. Nothing is written.
            if (row.IsMultiItem)
            {
                counters.SkippedBundleRows++;
                continue;
            }

            // A refunded purchase is money the user did not spend.
            if (row.Refunded)
            {
                counters.SkippedRefundedRows++;
                continue;
            }

            // A gift purchase is a game bought FOR SOMEBODY ELSE, so its price
            // belongs to no ownership of this account's. An in-game purchase is
            // the price of an item inside a game, not of the game. A refund row
            // is the reversal, not the acquisition.
            if (!string.Equals(row.TransactionType, SteamTransactionTypes.Purchase, StringComparison.Ordinal))
            {
                counters.SkippedNonPurchaseRows++;
                continue;
            }

            if (row.Total is not { } total || total.Cents <= 0)
            {
                continue;
            }

            if (!index.TryResolve(row.Items[0], counters, out var ownershipId))
            {
                continue;
            }

            counters.PricesMatched++;
            Pending(pending, ownershipId).OfferPrice(total.Cents, row.PurchasedAtUtc);
        }
    }

    private static PendingFill Pending(Dictionary<long, PendingFill> pending, long ownershipId)
    {
        if (!pending.TryGetValue(ownershipId, out var fill))
        {
            fill = new PendingFill();
            pending[ownershipId] = fill;
        }

        return fill;
    }

    private async Task<TitleIndex> BuildIndexAsync(CancellationToken ct)
    {
        var identities = await _releases.GetIdentitiesAsync(ct).ConfigureAwait(false);
        var byRelease = identities.ToDictionary(i => i.ReleaseId);

        var ownerships = await _ownerships.GetAllAsync(ct).ConfigureAwait(false);

        var byKey = new Dictionary<string, long>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        var considered = 0;

        foreach (var ownership in ownerships)
        {
            if (!string.Equals(ownership.Store, ExternalIdProviders.Steam, StringComparison.Ordinal))
            {
                continue;
            }

            if (!byRelease.TryGetValue(ownership.ReleaseId, out var identity))
            {
                continue;
            }

            // A provisional name is a placeholder like "App 1203620". Matching a
            // page title against it could only ever succeed by accident.
            if (identity.NameIsProvisional)
            {
                continue;
            }

            considered++;

            var key = TitleNormalizer.Normalize(identity.MatchTitle).Core;
            if (key.Length == 0)
            {
                continue;
            }

            if (!byKey.TryAdd(key, ownership.Id))
            {
                // Two owned releases normalise to the same title. §5.3 forbids
                // guessing which one a page row meant, so neither is matchable.
                ambiguous.Add(key);
            }
        }

        foreach (var key in ambiguous)
        {
            byKey.Remove(key);
        }

        return new TitleIndex(byKey, ambiguous, considered);
    }

    private sealed class TitleIndex(
        Dictionary<string, long> byKey, HashSet<string> ambiguousKeys, int considered)
    {
        public HashSet<string> AmbiguousKeys { get; } = ambiguousKeys;

        public int Considered { get; } = considered;

        public bool TryResolve(string title, Counters counters, out long ownershipId)
        {
            ownershipId = 0;

            var key = TitleNormalizer.Normalize(title).Core;
            if (key.Length == 0)
            {
                counters.SkippedNoOwnershipMatch++;
                return false;
            }

            if (AmbiguousKeys.Contains(key))
            {
                counters.SkippedAmbiguousTitle++;
                return false;
            }

            if (!byKey.TryGetValue(key, out ownershipId))
            {
                counters.SkippedNoOwnershipMatch++;
                return false;
            }

            return true;
        }
    }

    private sealed class Counters
    {
        public int AcquisitionsMatched;
        public int PricesMatched;
        public int SkippedNoOwnershipMatch;
        public int SkippedAmbiguousTitle;
        public int SkippedConflictingRows;
        public int SkippedBundleRows;
        public int SkippedRefundedRows;
        public int SkippedNonPurchaseRows;
        public int SkippedNonProductRows;
    }

    private sealed class PendingFill
    {
        public DateTime? AcquiredAt { get; private set; }

        public string? LicenseType { get; private set; }

        public long? PricePaidCents { get; private set; }

        // Two page rows resolved to the same ownership and disagreed. The
        // resolver's rule applies: an ambiguous claim is never guessed at, so
        // this ownership is left entirely alone.
        public bool Conflicted { get; private set; }

        public void OfferAcquisition(DateTime? acquiredAt, string? licenseType)
        {
            if (AcquiredAt is not null && acquiredAt is not null && AcquiredAt != acquiredAt)
            {
                Conflicted = true;
                return;
            }

            if (LicenseType is not null && licenseType is not null
                && !string.Equals(LicenseType, licenseType, StringComparison.Ordinal))
            {
                Conflicted = true;
                return;
            }

            AcquiredAt ??= acquiredAt;
            LicenseType ??= licenseType;
        }

        public void OfferPrice(long cents, DateTime? purchasedAt)
        {
            if (PricePaidCents is not null && PricePaidCents != cents)
            {
                Conflicted = true;
                return;
            }

            PricePaidCents ??= cents;

            // The history page dates a purchase as precisely as the licences
            // page dates the licence, so it is a fallback when only one page was
            // captured — never an override of the licences page.
            AcquiredAt ??= purchasedAt;
        }
    }
}
