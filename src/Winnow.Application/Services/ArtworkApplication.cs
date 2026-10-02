using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Winnow.Application;
using Winnow.Core.Domain;

namespace Winnow.App.Services;

public sealed class ArtworkApplication(ArtworkBrowserService browser, IApplicationChangePublisher changes)
{
    private sealed record Offer(long WorkId, ArtworkSlot Slot, ArtworkCandidate Candidate, DateTimeOffset Expires);
    private readonly ConcurrentDictionary<string, Offer> _offers = new();
    private readonly SemaphoreSlim _writes = new(1);

    public IReadOnlyList<ArtworkBrowserSource> Sources => browser.Sources;

    public Task<BackdropArtwork> BackdropAsync(long workId, double aspectRatio, CancellationToken ct)
        => browser.GetBackdropAsync(workId, aspectRatio, ct);

    public async Task<ArtworkState> StateAsync(long workId, ArtworkSlot slot, CancellationToken ct)
    {
        Validate(slot);
        var current = await browser.GetCurrentAsync(workId, slot, ct);
        return new(current, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current))));
    }

    public async Task<ArtworkBrowserPage> BrowseAsync(long workId, ArtworkSlot slot, string source, string? cursor, CancellationToken ct)
    {
        Validate(slot);
        if (source.Length > 128 || cursor?.Length > 256) throw new ArgumentException("Invalid artwork source or page.");
        foreach (var expired in _offers.Where(pair => pair.Value.Expires <= DateTimeOffset.UtcNow))
            _offers.TryRemove(expired.Key, out _);
        var page = await browser.BrowseAsync(workId, slot, source, cursor, ct);
        var candidates = page.Items.Take(100).Select(candidate =>
        {
            if (_offers.Count >= 10000) throw new ApplicationConflictException("Close old artwork browsers and try again.");
            var id = Guid.NewGuid().ToString("N");
            _offers[id] = new(workId, slot, candidate, DateTimeOffset.UtcNow.AddMinutes(30));
            return candidate with { OfferId = id };
        }).ToArray();
        return page with { Items = candidates };
    }

    public Task<ArtworkSaveResult> SaveAsync(long workId, ArtworkSlot slot, ArtworkSaveRequest request, CancellationToken ct)
    {
        if (!_offers.TryGetValue(request.OfferId, out var offer) || offer.WorkId != workId || offer.Slot != slot || offer.Expires <= DateTimeOffset.UtcNow)
            throw new ApplicationConflictException("This artwork selection expired. Reload the artwork browser.");
        return WriteAsync(workId, slot, request.Revision, () => browser.SaveAsync(workId, slot, offer.Candidate, ct), ct);
    }

    public Task<ArtworkSaveResult> ResetAsync(long workId, ArtworkSlot slot, string revision, CancellationToken ct)
        => WriteAsync(workId, slot, revision, () => browser.ResetAsync(workId, slot, ct), ct);

    public Task<ArtworkSaveResult> ImportAsync(long workId, ArtworkSlot slot, string revision, byte[] bytes, CancellationToken ct)
        => WriteAsync(workId, slot, revision, () => browser.ImportBytesAsync(workId, slot, bytes, ct), ct);

    public Task<ArtworkSaveResult> ImportUrlAsync(long workId, ArtworkSlot slot, ArtworkUrlRequest request, CancellationToken ct)
        => WriteAsync(workId, slot, request.Revision, () => browser.ImportUrlAsync(workId, slot, request.Url, ct), ct);

    private async Task<ArtworkSaveResult> WriteAsync(long workId, ArtworkSlot slot, string revision,
        Func<Task<ArtworkSaveResult>> write, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        try
        {
            if ((await StateAsync(workId, slot, ct)).Revision != revision)
                throw new ApplicationConflictException("Artwork changed in another window. Reload before saving.");
            var result = await write();
            if (result.Success) changes.Publish("library.changed", $"works/{workId}/artwork");
            return result;
        }
        finally { _writes.Release(); }
    }

    private static void Validate(ArtworkSlot slot)
    {
        if (!Enum.IsDefined(slot)) throw new ArgumentException("Unknown artwork slot.");
    }
}
