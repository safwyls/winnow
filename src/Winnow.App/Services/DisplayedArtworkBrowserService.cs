using Winnow.Core.Domain;
using Winnow.Covers;

namespace Winnow.App.Services;

/// <summary>The library projection owns cover preference, pins and linked-store header selection.</summary>
public sealed class DisplayedArtworkBrowserService(IArtworkBrowserService inner, Func<CoverKey?> currentCover) : IArtworkBrowserService
{
    public IReadOnlyList<ArtworkBrowserSource> Sources => inner.Sources;

    public async Task<ArtworkCandidate?> GetCurrentAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
    {
        var current = await inner.GetCurrentAsync(workId, slot, ct);
        if (slot == ArtworkSlot.Cover && (current is null || current.SourceId == "automatic") && currentCover() is { } key)
            return new("automatic", "Automatic", key.ToString(), slot, key) { IsCurrent = true };
        return current;
    }

    public Task<ArtworkBrowserPage> BrowseAsync(long workId, ArtworkSlot slot, string sourceId, string? cursor = null, CancellationToken ct = default)
        => inner.BrowseAsync(workId, slot, sourceId, cursor, ct);
    public Task<ArtworkSaveResult> SaveAsync(long workId, ArtworkSlot slot, ArtworkCandidate candidate, CancellationToken ct = default)
        => inner.SaveAsync(workId, slot, candidate, ct);
    public Task<ArtworkSaveResult> ResetAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
        => inner.ResetAsync(workId, slot, ct);
    public Task<ArtworkSaveResult> ImportFileAsync(long workId, ArtworkSlot slot, string path, CancellationToken ct = default)
        => inner.ImportFileAsync(workId, slot, path, ct);
    public Task<ArtworkSaveResult> ImportUrlAsync(long workId, ArtworkSlot slot, string url, CancellationToken ct = default)
        => inner.ImportUrlAsync(workId, slot, url, ct);
}
