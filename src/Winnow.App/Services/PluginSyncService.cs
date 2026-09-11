using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Winnow.Resolve;

namespace Winnow.App.Services;

/// <summary>Validates provider observations before applying them through the host's repositories.</summary>
public sealed class PluginSyncService(PluginCatalog catalog, ILibraryQueryRepository library,
    IWorkRepository works, IReleaseRepository releases, IWorkImageRepository images, IPluginFacetRepository facets,
    IMetadataCache cache, ExternalIdResolver resolver, LibrarySyncGate libraryGate, ILogger<PluginSyncService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var plugin in catalog.GetActive<ILibrarySourcePlugin>())
            {
                var games = await catalog.InvokeAsync(plugin, (instance, token) => ((ILibrarySourcePlugin)instance).GetLibraryAsync(token), ct);
                if (games is null) continue;
                using var lease = await libraryGate.EnterAsync(ct);
                await ApplySafelyAsync(plugin, () => ImportAsync(plugin, games, ct), ct);
            }
            var snapshot = await library.GetSnapshotAsync(BucketThresholds.Default, ct);
            var ownedReleases = snapshot.Ownerships.Select(o => o.ReleaseId).ToHashSet();
            var targets = snapshot.Releases.Where(r => ownedReleases.Contains(r.Id)).GroupBy(r => r.WorkId);
            var workById = snapshot.Works.ToDictionary(w => w.Id);
            foreach (var target in targets)
            {
                if (!workById.TryGetValue(target.Key, out var work)) continue;
                var games = target.Select(release =>
                {
                    var externalIds = snapshot.ExternalIds.Where(e => e.ReleaseId == release.Id)
                        .GroupBy(e => e.Provider).ToDictionary(g => g.Key, g => g.First().ProviderId);
                    if (work.IgdbId is { } igdb) externalIds["igdb"] = igdb.ToString(CultureInfo.InvariantCulture);
                    return new PluginGame(work.Id.ToString(CultureInfo.InvariantCulture), work.Name, externalIds);
                }).ToArray();
                foreach (var plugin in catalog.GetActive<IMetadataProviderPlugin>())
                {
                    var metadata = await catalog.InvokeAsync(plugin, (instance, token) => ((IMetadataProviderPlugin)instance).GetMetadataAsync(games[0], token), ct);
                    if (metadata is not null) await ApplySafelyAsync(plugin, () => ApplyMetadataAsync(plugin.Manifest.Id, work.Id, metadata, ct), ct);
                }
                foreach (var plugin in catalog.GetActive<IArtworkProviderPlugin>())
                {
                    var combined = new List<PluginArtwork>();
                    var complete = true;
                    foreach (var game in games)
                    {
                        var artwork = await catalog.InvokeAsync<PluginArtwork[]>(plugin, async (instance, token) =>
                        {
                            var result = await ((IArtworkProviderPlugin)instance).GetArtworkAsync(game, token);
                            return result?.Take(500).ToArray();
                        }, ct);
                        if (artwork is null) complete = false;
                        else combined.AddRange(artwork.Take(500));
                    }
                    if (complete) await ApplySafelyAsync(plugin, () => ApplyArtworkAsync(plugin, work.Id, combined, ct), ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { logger.LogWarning("Plugin refresh did not finish; existing library data remains available."); }
        finally { _gate.Release(); }
    }

    private async Task ApplySafelyAsync(PluginDescriptor plugin, Func<Task> apply, CancellationToken ct)
    {
        try { await apply(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { catalog.ReportInvalidResult(plugin); }
    }

    internal async Task ImportAsync(PluginDescriptor plugin, IReadOnlyList<PluginLibraryGame> games, CancellationToken ct)
    {
        var source = PluginArtRef.SourcePrefix + plugin.Manifest.Id;
        foreach (var game in games.Take(50000).Where(g => g is not null).DistinctBy(g => g.SourceId))
        {
            if (string.IsNullOrWhiteSpace(game.SourceId) || game.SourceId.Length > 256 || game.SourceId.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(game.Title) || game.Title.Length > 500 || game.PlaytimeMinutes < 0) continue;
            // All matching external IDs must agree before attaching this source to an existing release.
            var known = new List<Release>();
            foreach (var (provider, id) in (game.ExternalIds ?? new Dictionary<string, string>()).Take(32))
            {
                if (!ValidExternalId(provider, id)) continue;
                if (await releases.FindByExternalIdAsync(provider, id, ct) is { } found) known.Add(found);
            }
            if (await releases.FindByExternalIdAsync(source, game.SourceId, ct) is null
                && known.Select(r => r.Id).Distinct().ToArray() is [var existing])
                await releases.AddExternalIdAsync(new() { ReleaseId = existing, Provider = source, ProviderId = game.SourceId }, ct);

            var candidate = new CandidateOwnership(source, game.SourceId, game.Title.Trim(), game.AccountRef,
                game.InstallPath is { } path && Path.IsPathFullyQualified(path) ? path : null,
                game.Installed, game.PlaytimeMinutes, game.LastPlayedAt?.UtcDateTime, game.AcquiredAt?.UtcDateTime,
                source, DateTime.UtcNow);
            await resolver.ResolveAsync([candidate], ct, PlaytimeView.LowerBound);
            // Keep a provider's additional IDs only when no other release claims them.
            var release = await releases.FindByExternalIdAsync(source, game.SourceId, ct);
            if (release is null) continue;
            foreach (var (provider, id) in (game.ExternalIds ?? new Dictionary<string, string>()).Take(32))
                if (ValidExternalId(provider, id) && await releases.FindByExternalIdAsync(provider, id, ct) is null)
                    await releases.AddExternalIdAsync(new() { ReleaseId = release.Id, Provider = provider, ProviderId = id }, ct);
        }
    }

    private static bool ValidExternalId(string provider, string id) => provider is "steam" or "epic" or "gog"
        && !string.IsNullOrWhiteSpace(id) && id.Length <= 256 && !id.Any(char.IsControl);

    internal async Task ApplyMetadataAsync(string pluginId, long workId, PluginMetadata metadata, CancellationToken ct)
    {
        var source = PluginArtRef.SourcePrefix + pluginId;
        var sanitized = metadata with
        {
            Summary = metadata.Summary is { Length: <= 20000 } summary ? summary : null,
            Genres = CleanNames(metadata.Genres), Tags = CleanNames(metadata.Tags),
            ReleaseDate = metadata.ReleaseDate?.Year is >= 1950 and <= 2200 ? metadata.ReleaseDate : null
        };
        await cache.SetAsync(source, "metadata:" + workId, JsonSerializer.Serialize(sanitized), DateTime.UtcNow, ct);
        await works.ApplyEnrichmentAsync(new(workId, Summary: sanitized.Summary, FirstReleaseYear: sanitized.ReleaseDate?.Year)
            { Source = source }, ct);
        await facets.SetAsync(workId, source,
            sanitized.Genres.Select(n => new FacetAssignment(FacetKinds.Genre, n))
                .Concat(sanitized.Tags.Select(n => new FacetAssignment(FacetKinds.Tag, n))).ToArray(), ct);
    }

    private static string[] CleanNames(IReadOnlyList<string>? values) => (values ?? []).Take(100)
        .Where(v => !string.IsNullOrWhiteSpace(v) && v.Length <= 100 && !v.Any(char.IsControl)).Select(v => v.Trim()).Distinct().ToArray();

    internal async Task ApplyArtworkAsync(PluginDescriptor plugin, long workId, IReadOnlyList<PluginArtwork> artwork, CancellationToken ct)
    {
        var source = PluginArtRef.SourcePrefix + plugin.Manifest.Id;
        var usable = artwork.Take(500).Where(a => a is not null && !a.Animated && a.Width is > 0 and <= 8192 && a.Height is > 0 and <= 8192
            && (long)a.Width * a.Height <= 32 * 1024 * 1024 && Enum.IsDefined(a.Kind)
            && PluginArtworkSource.ValidUrl(plugin, a.Url)).DistinctBy(a => (a.Kind, a.Url)).ToArray();
        // Invalid responses cannot erase a previous valid observation.
        if (artwork.Count > 0 && usable.Length == 0) return;
        foreach (var asset in usable) await PluginArtworkSource.RegisterAsync(cache, plugin.Manifest.Id, asset.Url, ct);
        var stored = await images.GetForWorkAsync(workId, ct);
        foreach (var kind in new[] { ImageKinds.Artwork, ImageKinds.Screenshot })
        {
            var items = usable.Where(a => kind == ImageKinds.Screenshot ? a.Kind == PluginArtworkKind.Screenshot : a.Kind == PluginArtworkKind.Background)
                .Select(a => new GameImage
                {
                    ImageId = PluginArtRef.Key(plugin.Manifest.Id, a.Url)!.Value.Id, Url = a.Url,
                    Width = a.Width, Height = a.Height, AlphaChannel = a.Transparent, Animated = false, ImageType = a.ImageType
                }).ToArray();
            var previous = stored.FirstOrDefault(r => r.Source == source && r.Kind == kind);
            if (items.Length == 0) { if (previous is not null) await images.DeleteAsync(workId, source, kind, ct); }
            else if (previous is null || !previous.Images.SequenceEqual(items))
                await images.UpsertAsync(new() { WorkId = workId, Source = source, Kind = kind,
                    Images = items, ImageIds = ImageIdList.Join(items.Select(a => a.ImageId)) ?? "", ObservedAt = DateTime.UtcNow }, ct);
        }
        var cover = usable.FirstOrDefault(a => a.Kind == PluginArtworkKind.Cover && !a.Transparent);
        if (cover is not null)
            await works.ApplyEnrichmentAsync(new(workId, CoverUrl: PluginArtRef.Reference(PluginArtRef.Key(plugin.Manifest.Id, cover.Url)!.Value))
                { Source = source }, ct);
    }
}
