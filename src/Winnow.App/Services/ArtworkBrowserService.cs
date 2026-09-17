using System.Globalization;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;

namespace Winnow.App.Services;

/// <summary>Provider observations become durable local selections only after an explicit save.</summary>
public sealed class ArtworkBrowserService(
    ArtworkSelectionService selections, IWorkRepository works, IReleaseRepository releases,
    IWorkImageRepository images, IIgdbClient igdb, PluginCatalog plugins, IMetadataCache metadata,
    CoverPipeline pipeline, CoverDiskCache disk, UserArtStore userArt,
    ArtworkPreferences? preferences = null, IWorkIgdbPinRepository? pins = null) : IArtworkBrowserService
{
    public IReadOnlyList<ArtworkBrowserSource> Sources =>
    [
        new ArtworkBrowserSource("steam", "Steam", [ArtworkSlot.Hero, ArtworkSlot.Cover, ArtworkSlot.Icon]),
        new ArtworkBrowserSource("igdb", "IGDB", [ArtworkSlot.Hero, ArtworkSlot.Cover]),
        .. plugins.GetActive<IArtworkBrowserPlugin>().Select(p => new ArtworkBrowserSource(
            "plugin:" + p.Manifest.Id, p.Manifest.Name, [ArtworkSlot.Hero, ArtworkSlot.Cover, ArtworkSlot.Icon]))
    ];

    public async Task<ArtworkCandidate?> GetCurrentAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
    {
        var saved = await selections.GetAsync(workId, slot, ct);
        if (saved is not null && ArtKeys.Resolve(saved.AssetKey) is { } selectedKey)
            return new(saved.SourceId, SourceName(saved.SourceId), saved.AssetId, slot, selectedKey)
            { IsCurrent = true, Url = saved.SourceUrl, Creator = saved.Creator, PageUrl = saved.PageUrl };
        var work = await works.GetAsync(workId, ct);
        var ids = await SteamIdsAsync(workId, ct);
        CoverKey? key;
        if (slot == ArtworkSlot.Hero)
        {
            var group = await selections.GroupAsync(workId, ct);
            var rows = await BackdropImages.LoadAsync(images, workId, group, ct);
            key = BackdropSelection.Candidates(work?.BackgroundUrl, rows, steamAppIds: ids,
                sourceOrder: preferences?.SourceOrder).Cast<CoverKey?>().FirstOrDefault();
        }
        else if (slot == ArtworkSlot.Icon) key = null;
        else key = new CoverSelection(preferences?.AvailableSources.Select(s => s.Id)).Select(work?.CoverUrl,
            ids.FirstOrDefault(), pins is not null && await pins.GetAsync(workId, ct) is not null);
        return key is { } actual ? new("automatic", "Automatic", actual.ToString(), slot, actual) { IsCurrent = true } : null;
    }

    public async Task<ArtworkBrowserPage> BrowseAsync(long workId, ArtworkSlot slot, string sourceId,
        string? cursor = null, CancellationToken ct = default)
    {
        try
        {
            if (!Enum.IsDefined(slot)) return new([], Message: "This artwork type is unavailable.");
            var game = await GameAsync(workId, ct);
            if (game is null) return new([], Message: "This game is no longer in your library.");
            if (sourceId == "steam")
            {
                if (cursor is not null) return new([]);
                var ids = await SteamIdsAsync(workId, ct);
                var candidates = ids.SelectMany(id => slot == ArtworkSlot.Hero
                    ? new[] { SteamCandidate(id, slot), SteamCandidate(id, slot, true) }
                    : new[] { SteamCandidate(id, slot) }).ToArray();
                return new(candidates, Message: candidates.Length == 0 ? "No Steam copy is linked to this game." : null);
            }
            if (sourceId == "igdb") return await IgdbAsync(workId, slot, cursor, ct);
            var plugin = plugins.GetActive<IArtworkBrowserPlugin>().FirstOrDefault(p => "plugin:" + p.Manifest.Id == sourceId);
            if (plugin is null)
                return new([], Message: "This artwork source is not available. Check Settings → Plugins.");
            var kind = ToKind(slot);
            var page = await plugins.InvokeAsync(plugin,
                async (instance, token) =>
                {
                    var browser = (IArtworkBrowserPlugin)instance;
                    return browser.SupportedArtworkKinds.Contains(kind)
                        ? await browser.BrowseArtworkAsync(game, kind, cursor, token)
                        : new PluginArtworkPage([]) { Availability = PluginArtworkAvailability.Unsupported,
                            Message = "This source does not provide this artwork type." };
                }, ct);
            if (page is null) return new([], Message: "This source could not be reached. Try again.", CanRetry: true);
            var results = new List<ArtworkCandidate>();
            foreach (var asset in page.Items.Take(100))
            {
                if (asset.Kind != kind || asset.Animated || asset.Width is <= 0 or > 8192 || asset.Height is <= 0 or > 8192
                    || (long)asset.Width * asset.Height > 32 * 1024 * 1024 || !PluginArtworkSource.ValidUrl(plugin, asset.Url)) continue;
                await PluginArtworkSource.RegisterAsync(metadata, plugin.Manifest.Id, asset.Url, ct);
                CoverKey? thumbnail = null;
                if (PluginArtworkSource.ValidUrl(plugin, asset.ThumbnailUrl))
                {
                    await PluginArtworkSource.RegisterAsync(metadata, plugin.Manifest.Id, asset.ThumbnailUrl!, ct);
                    thumbnail = PluginArtRef.Key(plugin.Manifest.Id, asset.ThumbnailUrl);
                }
                results.Add(new(sourceId, plugin.Manifest.Name, asset.Id, slot, PluginArtRef.Key(plugin.Manifest.Id, asset.Url)!.Value)
                {
                    Url = asset.Url, ThumbnailKey = thumbnail, Width = asset.Width, Height = asset.Height,
                    Creator = SafeLabel(asset.Creator), PageUrl = SafePage(asset.PageUrl)
                });
            }
            return new(results.DistinctBy(c => c.PreviewKey).ToArray(),
                page.NextCursor is { Length: > 0 and <= 256 } next && next != cursor ? next : null,
                SafeLabel(page.Message), page.Availability is PluginArtworkAvailability.Unavailable or PluginArtworkAvailability.SetupRequired);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new([], Message: "Artwork could not be loaded. Try again.", CanRetry: true);
        }
    }

    public async Task<ArtworkSaveResult> SaveAsync(long workId, ArtworkSlot slot, ArtworkCandidate candidate, CancellationToken ct = default)
    {
        if (candidate.Slot != slot) return new(false, "Choose artwork for this slot.");
        try
        {
            // Fetch/decode through the same bounded pipeline used by previews; retain original bytes, not the display rendition.
            using var decoded = await pipeline.GetAsync(candidate.PreviewKey, 128, CoverLayers.Vivid, ct).ConfigureAwait(false);
            if (decoded is null || !disk.TryReadSource(candidate.PreviewKey, out var bytes))
                return new(false, "This image could not be downloaded. Your artwork is unchanged.");
            var imported = await Task.Run(() => userArt.ImportValidatedBytes(bytes), ct).ConfigureAwait(false);
            return await CommitAsync(workId, slot, imported, candidate, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return new(false, "Artwork could not be saved. Try again."); }
    }

    public async Task<ArtworkSaveResult> ResetAsync(long workId, ArtworkSlot slot, CancellationToken ct = default)
    {
        try
        {
            await selections.ResetAsync(workId, slot, ct);
            return new(true, "Using automatic artwork.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return new(false, "Artwork could not be reset. Try again."); }
    }

    public Task<ArtworkSaveResult> ImportFileAsync(long workId, ArtworkSlot slot, string path, CancellationToken ct = default)
        => ImportAsync(workId, slot, () => userArt.ImportFileAsync(path, ct), ct);

    public Task<ArtworkSaveResult> ImportUrlAsync(long workId, ArtworkSlot slot, string url, CancellationToken ct = default)
        => ImportAsync(workId, slot, () => userArt.ImportUrlAsync(url, ct), ct);

    private async Task<ArtworkSaveResult> ImportAsync(long workId, ArtworkSlot slot, Func<Task<UserArtImport>> import, CancellationToken ct)
    {
        try
        {
            var result = await import();
            if (UserArtRef.Token(result.Reference) is not { } token || !userArt.TryRead(token, out var bytes))
                return new(false, "This image could not be imported. Choose an image file or a direct image URL.");
            var validated = await Task.Run(() => userArt.ImportValidatedBytes(bytes), ct).ConfigureAwait(false);
            return await CommitAsync(workId, slot, validated, null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { return new(false, "This image could not be imported. Your artwork is unchanged."); }
    }

    private async Task<ArtworkSaveResult> CommitAsync(long workId, ArtworkSlot slot, UserArtImport imported,
        ArtworkCandidate? candidate, CancellationToken ct)
    {
        if (imported.Reference is null) return new(false, "Choose a valid static image up to 16 MB and 8192 pixels per side.");
        if (await works.GetAsync(workId, ct) is null) return new(false, "This game is no longer in your library.");
        await selections.SaveAsync(new ArtworkChoice
        {
            WorkId = workId, Slot = slot, Kind = ArtworkChoiceKind.Manual, AssetKey = imported.Reference,
            SourceId = candidate?.SourceId ?? "user", AssetId = candidate?.AssetId ?? imported.Reference,
            SourceUrl = candidate?.Url, Creator = candidate?.Creator, PageUrl = candidate?.PageUrl
        }, ct);
        return new(true, "Artwork saved.");
    }

    private async Task<ArtworkBrowserPage> IgdbAsync(long workId, ArtworkSlot slot, string? cursor, CancellationToken ct)
    {
        if (slot == ArtworkSlot.Icon) return new([], Message: "IGDB does not provide game icons.");
        if (!int.TryParse(cursor ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset < 0 || offset > 10000)
            return new([], Message: "This artwork page is unavailable.");
        var candidates = new List<ArtworkCandidate>();
        foreach (var member in await selections.GroupAsync(workId, ct))
        {
            var work = await works.GetAsync(member, ct);
            var games = work?.IgdbId is { } id ? await igdb.GetGamesAsync([id], ct: ct) : [];
            if (slot == ArtworkSlot.Cover)
            {
                foreach (var url in games.Select(g => g.CoverUrl).Append(work?.CoverUrl))
                    if (IgdbImageUrl.ImageId(url) is { } imageId) candidates.Add(IgdbCandidate(imageId, slot));
            }
            else
            {
                var rows = await images.GetForWorkAsync(member, ct);
                foreach (var row in rows.Where(r => r.Source == ImageSources.Igdb))
                    foreach (var imageId in row.Ids.Concat(row.Images.Select(i => i.ImageId)).Distinct())
                    {
                        var image = row.Images.FirstOrDefault(i => i.ImageId == imageId);
                        if (image?.Animated == true || image?.AlphaChannel == true
                            || image is { Width: { } w, Height: { } h } && w <= h) continue;
                        if (ImageIdList.IsImageId(imageId)) candidates.Add(IgdbCandidate(imageId, slot) with { Width = image?.Width, Height = image?.Height });
                    }
                foreach (var game in games)
                    foreach (var imageId in game.ArtworkImageIds.Concat(game.ScreenshotImageIds).Where(ImageIdList.IsImageId))
                    {
                        var image = game.ArtworkImages.Concat(game.ScreenshotImages).FirstOrDefault(i => i.ImageId == imageId);
                        if (image?.Animated == true || image?.AlphaChannel == true
                            || image is { Width: { } w, Height: { } h } && w <= h) continue;
                        candidates.Add(IgdbCandidate(imageId, slot) with { Width = image?.Width, Height = image?.Height });
                    }
            }
        }
        var unique = candidates.DistinctBy(c => c.AssetId).ToArray();
        return new(unique.Skip(offset).Take(40).ToArray(), offset + 40 < unique.Length ? (offset + 40).ToString(CultureInfo.InvariantCulture) : null,
            unique.Length > 0 ? null : await igdb.IsConfiguredAsync(ct)
                ? "No IGDB artwork is available. Check this game's IGDB match in details."
                : "No cached IGDB artwork. Add credentials in Settings → Metadata & artwork.");
    }

    private static ArtworkCandidate IgdbCandidate(string id, ArtworkSlot slot)
        => new("igdb", "IGDB", id, slot, slot == ArtworkSlot.Cover ? CoverKey.Igdb(id) : CoverKey.IgdbBackdrop(id))
        { Url = IgdbImageUrl.ForImageId(id, slot == ArtworkSlot.Cover ? "t_cover_big_2x" : "t_1080p_2x") };

    private static ArtworkCandidate SteamCandidate(string id, ArtworkSlot slot, bool standard = false)
        => new("steam", "Steam", id + ":" + slot + (standard ? ":standard" : ""), slot,
            SteamBrowserArtworkSource.Key(id, slot, standard)) { PageUrl = "https://store.steampowered.com/app/" + id };

    private async Task<PluginGame?> GameAsync(long workId, CancellationToken ct)
    {
        var work = await works.GetAsync(workId, ct);
        if (work is null) return null;
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in await selections.GroupAsync(workId, ct))
            foreach (var release in await releases.GetByWorkAsync(member, ct))
                foreach (var id in await releases.GetExternalIdsAsync(release.Id, ct)) ids.TryAdd(id.Provider, id.ProviderId);
        return new(workId.ToString(CultureInfo.InvariantCulture), work.Name, ids);
    }

    private async Task<string[]> SteamIdsAsync(long workId, CancellationToken ct)
    {
        var ids = new List<string>();
        foreach (var member in await selections.GroupAsync(workId, ct))
            foreach (var release in await releases.GetByWorkAsync(member, ct))
                ids.AddRange((await releases.GetExternalIdsAsync(release.Id, ct))
                    .Where(i => i.Provider == "steam" && GameLink.IsSteamAppId(i.ProviderId)).Select(i => i.ProviderId));
        return ids.Distinct().ToArray();
    }

    private string SourceName(string id) => Sources.FirstOrDefault(s => s.Id == id)?.Name
        ?? plugins.Plugins.FirstOrDefault(p => "plugin:" + p.Manifest.Id == id)?.Manifest.Name
        ?? (id == "user" ? "Your image" : "Saved artwork");
    private static string? SafeLabel(string? value) => value is { Length: <= 500 } && !value.Any(char.IsControl) ? value : null;
    private static string? SafePage(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? value : null;
    private static PluginArtworkKind ToKind(ArtworkSlot slot) => slot switch
    { ArtworkSlot.Cover => PluginArtworkKind.Cover, ArtworkSlot.Icon => PluginArtworkKind.Icon, _ => PluginArtworkKind.Background };
    private static ArtworkSlot ToSlot(PluginArtworkKind kind) => kind switch
    { PluginArtworkKind.Cover => ArtworkSlot.Cover, PluginArtworkKind.Icon => ArtworkSlot.Icon, _ => ArtworkSlot.Hero };
}
