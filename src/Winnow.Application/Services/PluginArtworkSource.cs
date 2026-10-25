using Winnow.Covers;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;

namespace Winnow.App.Services;

/// <summary>Fetches only host-validated artwork observations from active plugins.</summary>
public sealed class PluginArtworkSource(PluginCatalog catalog, PluginHttpClient http, IMetadataCache cache) : ICoverSource
{
    public string Name => "plugin-artwork";
    public string SourceSetId => string.Join(',', catalog.GetActive<IArtworkProviderPlugin>().Select(p => p.Manifest.Id));
    public bool CanHandle(CoverKey key) => PluginArtRef.PluginId(key) is not null;

    public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        if (PluginArtRef.PluginId(key) is not { } id) return null;
        var plugin = catalog.GetActive<IArtworkProviderPlugin>().FirstOrDefault(p => p.Manifest.Id == id);
        if (plugin is null) return null;
        var stored = await cache.GetAsync("plugin-artwork", id + ":" + key.Id, ct);
        if (stored?.PayloadJson is not { } url || PluginArtRef.Key(id, url) != key) return null;
        var result = await http.CreateScope(plugin.Manifest, 32 * 1024 * 1024).SendAsync(new(url), ct);
        if (result.StatusCode == 404) return null;
        if (result.StatusCode is < 200 or >= 300) throw new HttpRequestException("Plugin artwork download failed.");
        return result.Body.Length == 0 ? null : result.Body;
    }

    public static bool ValidUrl(PluginDescriptor plugin, string? url)
        => url is { Length: <= 2048 } && PluginArtRef.Key(plugin.Manifest.Id, url) is not null
            && plugin.Manifest.Network.AllowedHosts.Contains(new Uri(url).Host, StringComparer.OrdinalIgnoreCase);

    public static Task RegisterAsync(IMetadataCache cache, string id, string url, CancellationToken ct)
    {
        var key = PluginArtRef.Key(id, url) ?? throw new ArgumentException("Invalid artwork URL.");
        return cache.SetAsync("plugin-artwork", id + ":" + key.Id, new Uri(url).AbsoluteUri, DateTime.UtcNow, ct);
    }
}
