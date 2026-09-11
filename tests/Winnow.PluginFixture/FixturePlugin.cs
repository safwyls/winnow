using System.Runtime.Loader;
using Winnow.PluginSdk;

namespace Winnow.PluginFixture;

/// <summary>An executable package fixture: it references only the published SDK.</summary>
public sealed class FixturePlugin : ILibrarySourcePlugin, IMetadataProviderPlugin, IArtworkProviderPlugin, IRecommendationFeedPlugin
{
    private IPluginContext? _context;
    public async ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        await context.Settings.SetAsync("loaded-in-private-context",
            (AssemblyLoadContext.GetLoadContext(GetType().Assembly) != AssemblyLoadContext.Default).ToString(), cancellationToken);
    }

    public Task<IReadOnlyList<PluginLibraryGame>?> GetLibraryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PluginLibraryGame>?>([new("fixture-game", "Fixture game")
        {
            ExternalIds = new Dictionary<string, string> { ["steam"] = "220" }, Installed = false,
        }]);

    public Task<PluginMetadata?> GetMetadataAsync(PluginGame game, CancellationToken cancellationToken = default) =>
        Task.FromResult<PluginMetadata?>(new() { Summary = "Fixture metadata from " + _context!.PluginId, Genres = ["Adventure"] });

    public Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PluginArtwork>?>([new("fixture-art", "https://art.example/hero.png", 3840, 2160)]);

    public Task<IReadOnlyList<PluginRecommendation>?> GetRecommendationsAsync(IReadOnlyList<PluginGame> library,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PluginRecommendation>?>(library.Select(x => new PluginRecommendation(x.Id, 0.8, "Fixture feed")).ToArray());
}

public sealed class ThrowingPlugin : IArtworkProviderPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("private-api-key-must-not-escape");
    public Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("private-api-key-must-not-escape");
}

public sealed class SlowPlugin : IArtworkProviderPlugin
{
    public async ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) =>
        await Task.Delay(Timeout.Infinite, cancellationToken);
    public Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PluginArtwork>?>([]);
}
