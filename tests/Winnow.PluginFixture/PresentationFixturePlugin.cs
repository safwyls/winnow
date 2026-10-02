using Winnow.PluginSdk;

namespace Winnow.PluginFixture;

/// <summary>A public-SDK package boundary for the source's injected settings snapshot.</summary>
public sealed class PresentationFixturePlugin : ILibrarySourcePlugin, IMetadataProviderPlugin, IArtworkProviderPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
    public Task<IReadOnlyList<PluginLibraryGame>?> GetLibraryAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PluginLibraryGame>?>([]);
    public Task<PluginMetadata?> GetMetadataAsync(PluginGame game, CancellationToken cancellationToken = default)
        => Task.FromResult<PluginMetadata?>(null);
    public Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PluginArtwork>?>([]);
}
