using Winnow.Core.Domain;
using Winnow.Covers;

namespace Winnow.App.Services;

public sealed record ArtworkBrowserSource(string Id, string Name, IReadOnlyList<ArtworkSlot> Slots);

public sealed record ArtworkCandidate(string SourceId, string SourceName, string AssetId,
    ArtworkSlot Slot, CoverKey PreviewKey)
{
    public string? Url { get; init; }
    public CoverKey? ThumbnailKey { get; init; }
    public string? Creator { get; init; }
    public string? PageUrl { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed record ArtworkBrowserPage(IReadOnlyList<ArtworkCandidate> Items, string? NextCursor = null,
    string? Message = null, bool CanRetry = false);

public sealed record ArtworkSaveResult(bool Success, string Message);

public interface IArtworkBrowserService
{
    IReadOnlyList<ArtworkBrowserSource> Sources { get; }
    Task<ArtworkCandidate?> GetCurrentAsync(long workId, ArtworkSlot slot, CancellationToken ct = default);
    Task<ArtworkBrowserPage> BrowseAsync(long workId, ArtworkSlot slot, string sourceId,
        string? cursor = null, CancellationToken ct = default);
    Task<ArtworkSaveResult> SaveAsync(long workId, ArtworkSlot slot, ArtworkCandidate candidate, CancellationToken ct = default);
    Task<ArtworkSaveResult> ResetAsync(long workId, ArtworkSlot slot, CancellationToken ct = default);
    Task<ArtworkSaveResult> ImportFileAsync(long workId, ArtworkSlot slot, string path, CancellationToken ct = default);
    Task<ArtworkSaveResult> ImportUrlAsync(long workId, ArtworkSlot slot, string url, CancellationToken ct = default);
}
