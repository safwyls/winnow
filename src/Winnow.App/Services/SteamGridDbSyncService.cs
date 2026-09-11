using Microsoft.Extensions.Logging;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamGridDb;

namespace Winnow.App.Services;

/// <summary>Persists hero observations on their original works; linking is resolved when displayed.</summary>
public sealed class SteamGridDbSyncService(
    ILibraryQueryRepository library,
    IWorkImageRepository images,
    ISteamGridDbClient client,
    ILogger<SteamGridDbSyncService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var written = 0;
        try
        {
            var targets = await library.GetFacetTargetsAsync(ct);
            var results = new Dictionary<string, IReadOnlyList<GameImage>?>(StringComparer.Ordinal);
            foreach (var work in targets.Where(target => IsAppId(target.SteamAppId)).GroupBy(target => target.WorkId))
            {
                ct.ThrowIfCancellationRequested();
                var collected = new List<GameImage>();
                var complete = true;
                foreach (var appId in work.Select(target => target.SteamAppId!).Distinct())
                {
                    if (!results.TryGetValue(appId, out var heroes))
                    {
                        heroes = await client.GetHeroesAsync(appId, ct);
                        results.Add(appId, heroes);
                    }
                    if (heroes is null) complete = false;
                    else collected.AddRange(heroes);
                }
                // A partial outage must not replace a complete previous observation.
                if (!complete) continue;
                var heroesForWork = collected.DistinctBy(image => image.ImageId).ToArray();
                var stored = (await images.GetForWorkAsync(work.Key, ct))
                    .FirstOrDefault(row => row.Source == ImageSources.SteamGridDb && row.Kind == ImageKinds.Artwork);
                if (heroesForWork.Length == 0)
                {
                    if (stored is not null && await images.DeleteAsync(work.Key, ImageSources.SteamGridDb, ImageKinds.Artwork, ct)) written++;
                    continue;
                }
                if (stored is not null && stored.Images.SequenceEqual(heroesForWork)) continue;
                await images.UpsertAsync(new WorkImages
                {
                    WorkId = work.Key, Source = ImageSources.SteamGridDb, Kind = ImageKinds.Artwork,
                    ImageIds = ImageIdList.Join(heroesForWork.Select(image => image.ImageId)) ?? string.Empty,
                    Images = heroesForWork, ObservedAt = DateTime.UtcNow
                }, ct);
                written++;
            }
            return written;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            logger.LogWarning("SteamGridDB artwork refresh did not finish; stored artwork remains available.");
            return written;
        }
        finally { _gate.Release(); }
    }

    private static bool IsAppId(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 10 && value.All(char.IsAsciiDigit);
}
