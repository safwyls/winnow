using Winnow.Api.Contracts.Details;
using Winnow.App.Services;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;

namespace Winnow.Application.Details;

public sealed class ImportApplication(IAcquisitionExport export, IUnitOfWorkFactory transactions,
    ISteamAccountPageFileLoader loader, ISteamAccountPageImport importer, IApplicationChangePublisher changes)
{
    public const long MaximumFileBytes = 64L * 1024 * 1024;
    public const long MaximumBatchBytes = 128L * 1024 * 1024;

    public async Task<AcquisitionExportResponse> ExportAcquisitionsAsync(CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        var response = await export.ReadAsync(ct);
        transaction.Commit();
        return new(response.Content, response.OwnershipCount);
    }

    public async Task<SteamAccountPageLoadResult> LoadPagesAsync(SteamPageUploadRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.Files);
        if (request.Files.Any(x => x.Content is null || x.Content.LongLength > MaximumFileBytes)
            || request.Files.Sum(x => x.Content.LongLength) > MaximumBatchBytes)
            throw new ArgumentException("Saved pages exceed the upload size limit.");
        var directory = Path.Combine(Path.GetTempPath(), "winnow-page-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var file in request.Files)
            {
                var path = Path.Combine(directory, names.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".html");
                await File.WriteAllBytesAsync(path, file.Content, ct);
                names.Add(path, Path.GetFileName(file.Name));
            }
            var response = await loader.LoadAsync(names.Keys, ct);
            return response with { Files = response.Files.Select(x => x with { Path = names[x.Path] }).ToArray() };
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public async Task<SteamImportResponse> ImportPagesAsync(SteamAccountPages pages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.TotalByteCount > MaximumBatchBytes) throw new ArgumentException("Captured pages exceed the import size limit.");
        var report = await importer.ImportAsync(pages, ct);
        if (report.WroteAnything || report.TransactionFactsRecorded > 0 || report.LicenseFactsRecorded > 0)
            changes.Publish("library.changed", "acquisitions");
        return Map(report);
    }

    private static SteamImportResponse Map(SteamAccountPageImportReport report)
    {
        return new SteamImportResponse
{
    LicensesOutcome = report.LicensesOutcome.ToString(),
    HistoryOutcome = report.HistoryOutcome.ToString(),
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
