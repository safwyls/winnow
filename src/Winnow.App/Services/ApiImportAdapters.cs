using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Core.Ingest;

namespace Winnow.App.Services;

public sealed class ApiAcquisitionExport(WinnowApiClient api) : IAcquisitionExport
{
    public async Task<AcquisitionCsv> ReadAsync(CancellationToken ct = default)
    {
        var result = await api.GetAsync<AcquisitionExportResponse>("exports/acquisitions", ct);
        return new(result.Content, result.OwnershipCount);
    }
}

public sealed class ApiSteamAccountPages(WinnowApiClient api) : ISteamAccountPageImport, ISteamAccountPageFileLoader
{
    private const long MaximumFileBytes = 64L * 1024 * 1024;
    private const long MaximumBatchBytes = 128L * 1024 * 1024;

    public async Task<SteamAccountPageLoadResult> LoadAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var uploads = new List<SteamPageUpload>();
        var failures = new List<SteamAccountPageFile>();
        long total = 0;
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) continue;
            var name = Path.GetFileName(path);
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > MaximumFileBytes || total + stream.Length > MaximumBatchBytes)
                {
                    failures.Add(new(name, SteamAccountPageFileOutcome.Unreadable, null, "The selected pages exceed the upload size limit."));
                    continue;
                }
                var bytes = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(bytes, ct);
                total += bytes.LongLength;
                uploads.Add(new(name, bytes));
            }
            catch (FileNotFoundException) { failures.Add(new(name, SteamAccountPageFileOutcome.NotFound, null, null)); }
            catch (IOException) { failures.Add(new(name, SteamAccountPageFileOutcome.Unreadable, null, null)); }
            catch (UnauthorizedAccessException) { failures.Add(new(name, SteamAccountPageFileOutcome.Unreadable, null, null)); }
        }
        var result = await api.SendAsync<SteamPageUploadRequest, SteamAccountPageLoadResult>(HttpMethod.Post,
            "imports/steam/load-files", new(uploads), ct: ct);
        return result with
        {
            Pages = result.Pages with { HasFailedSavedInputs = result.Pages.HasFailedSavedInputs || failures.Count > 0 },
            Files = result.Files.Concat(failures).ToArray(),
        };
    }

    public async Task<SteamAccountPageImportReport> ImportAsync(SteamAccountPages pages, CancellationToken ct = default)
    {
        var report = await api.SendAsync<SteamAccountPages, SteamImportResponse>(HttpMethod.Post, "imports/steam/pages", pages, ct: ct);
        return new SteamAccountPageImportReport
{
    LicensesOutcome = Enum.Parse<Winnow.Ingest.Steam.AccountPages.SteamAccountPageParseOutcome>(report.LicensesOutcome),
    HistoryOutcome = Enum.Parse<Winnow.Ingest.Steam.AccountPages.SteamAccountPageParseOutcome>(report.HistoryOutcome),
    LicensesFailureReason = report.LicensesFailureReason,
    HistoryFailureReason = report.HistoryFailureReason,
    LicenseRowsParsed = report.LicenseRowsParsed,
    LicenseRowsSkippedByParser = report.LicenseRowsSkippedByParser,
    LicenseRowsUnmappedAcquisition = report.LicenseRowsUnmappedAcquisition,
    HistoryRowsParsed = report.HistoryRowsParsed,
    HistoryRowsSkippedByParser = report.HistoryRowsSkippedByParser,
    LicensesTruncated = report.LicensesTruncated,
    HistoryTruncated = report.HistoryTruncated,
    LicensesReportedTotal = report.LicensesReportedTotal,
    SteamOwnershipsConsidered = report.SteamOwnershipsConsidered,
    OwnershipsAmbiguousByTitle = report.OwnershipsAmbiguousByTitle,
    AcquisitionsMatched = report.AcquisitionsMatched,
    PricesMatched = report.PricesMatched,
    OwnershipsFilled = report.OwnershipsFilled,
    OwnershipsAlreadyComplete = report.OwnershipsAlreadyComplete,
    SkippedNoOwnershipMatch = report.SkippedNoOwnershipMatch,
    SkippedAmbiguousTitle = report.SkippedAmbiguousTitle,
    SkippedConflictingRows = report.SkippedConflictingRows,
    SkippedBundleRows = report.SkippedBundleRows,
    SkippedRefundedRows = report.SkippedRefundedRows,
    SkippedNonPurchaseRows = report.SkippedNonPurchaseRows,
    SkippedNonProductRows = report.SkippedNonProductRows,
    TransactionFactsRecorded = report.TransactionFactsRecorded,
    TransactionFactsAlreadyRecorded = report.TransactionFactsAlreadyRecorded,
    LicenseFactsRecorded = report.LicenseFactsRecorded,
    LicenseFactsAlreadyRecorded = report.LicenseFactsAlreadyRecorded,
    Elapsed = report.Elapsed,
};
    }
}
