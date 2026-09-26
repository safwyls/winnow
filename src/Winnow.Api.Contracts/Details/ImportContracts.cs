using Winnow.Core.Ingest;

namespace Winnow.Api.Contracts.Details;

public sealed record AcquisitionExportResponse(string Content, int OwnershipCount);
public sealed record SteamPageUpload(string Name, byte[] Content);
public sealed record SteamPageUploadRequest(IReadOnlyList<SteamPageUpload> Files);

public sealed record SteamImportResponse
{
    public string LicensesOutcome { get; init; } = "Absent";
    public string HistoryOutcome { get; init; } = "Absent";
    public string? LicensesFailureReason { get; init; }
    public string? HistoryFailureReason { get; init; }
    public int LicenseRowsParsed { get; init; }
    public int LicenseRowsSkippedByParser { get; init; }
    public int LicenseRowsUnmappedAcquisition { get; init; }
    public int HistoryRowsParsed { get; init; }
    public int HistoryRowsSkippedByParser { get; init; }
    public bool LicensesTruncated { get; init; }
    public bool HistoryTruncated { get; init; }
    public int? LicensesReportedTotal { get; init; }
    public int SteamOwnershipsConsidered { get; init; }
    public int OwnershipsAmbiguousByTitle { get; init; }
    public int AcquisitionsMatched { get; init; }
    public int PricesMatched { get; init; }
    public int OwnershipsFilled { get; init; }
    public int OwnershipsAlreadyComplete { get; init; }
    public int SkippedNoOwnershipMatch { get; init; }
    public int SkippedAmbiguousTitle { get; init; }
    public int SkippedConflictingRows { get; init; }
    public int SkippedBundleRows { get; init; }
    public int SkippedRefundedRows { get; init; }
    public int SkippedNonPurchaseRows { get; init; }
    public int SkippedNonProductRows { get; init; }
    public int TransactionFactsRecorded { get; init; }
    public int TransactionFactsAlreadyRecorded { get; init; }
    public int LicenseFactsRecorded { get; init; }
    public int LicenseFactsAlreadyRecorded { get; init; }
    public TimeSpan Elapsed { get; init; }
}
