using Winnow.PluginSdk;

namespace Winnow.PluginFixture;

public sealed class ArtworkBrowserFixture : IArtworkProviderPlugin, IArtworkBrowserPlugin, IMetadataProviderPlugin
{
    public IReadOnlyList<PluginArtworkKind> SupportedArtworkKinds => [PluginArtworkKind.Background, PluginArtworkKind.Icon];

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) => default;

    public Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PluginArtwork>?>([]);

    public Task<PluginMetadata?> GetMetadataAsync(PluginGame game,
        CancellationToken cancellationToken = default) => Task.FromResult<PluginMetadata?>(new());

    public Task<PluginArtworkPage> BrowseArtworkAsync(PluginGame game, PluginArtworkKind kind,
        string? cursor = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!SupportedArtworkKinds.Contains(kind))
            return Task.FromResult(new PluginArtworkPage([]) { Availability = PluginArtworkAvailability.Unsupported });
        if (cursor is not null and not "fixture-next") throw new ArgumentException("Unexpected fixture cursor.", nameof(cursor));
        var assetId = cursor is null ? "first" : "second";
        return Task.FromResult(new PluginArtworkPage(
            [new(assetId, $"https://art.example/{game.ExternalIds["steam"]}/{assetId}.png", 256, 256)
            {
                Kind = kind,
                ThumbnailUrl = $"https://art.example/thumb/{assetId}.png",
                Creator = "Fixture artist",
                PageUrl = $"https://art.example/asset/{assetId}",
            }], cursor is null ? "fixture-next" : null));
    }
}
