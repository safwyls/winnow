using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.Services;

/// <summary>One portrait-cover policy for library tiles, merge rows and work previews.</summary>
public sealed class CoverSelection(IEnumerable<string>? availableSources = null)
{
    private readonly HashSet<string> _available = new(availableSources ?? [ArtworkPreferences.Steam, ArtworkPreferences.Igdb], StringComparer.Ordinal);

    public CoverKey? Select(string? coverUrl, string? steamAppId = null, bool hasIgdbPin = false)
    {
        if (UserArtRef.Token(coverUrl) is { Length: > 0 } userToken) return CoverKey.User(userToken);
        var igdbImage = _available.Contains(ArtworkPreferences.Igdb) ? IgdbImageUrl.ImageId(coverUrl) : null;
        // A pin belongs to the same work as coverUrl. It corrects storefront art
        // without evicting the original Steam image or borrowing another work's pin.
        if (hasIgdbPin && igdbImage is { Length: > 0 }) return CoverKey.Igdb(igdbImage);
        if (_available.Contains(ArtworkPreferences.Steam) && !string.IsNullOrEmpty(steamAppId)) return CoverKey.Steam(steamAppId);
        if (igdbImage is { Length: > 0 }) return CoverKey.Igdb(igdbImage);
        if (PluginArtRef.Parse(coverUrl) is { } plugin && _available.Contains("plugin:" + PluginArtRef.PluginId(plugin))) return plugin;
        return null;
    }
}
