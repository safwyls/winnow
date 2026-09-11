using System.Collections;
using Winnow.PluginSdk;

namespace Winnow.PluginFixture;

public sealed class ReleaseArtworkPlugin : IArtworkProviderPlugin
{
    private IPluginContext? _context;
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default)
    {
        if (!game.ExternalIds.TryGetValue("steam", out var appId)) return null;
        var settings = _context!.Settings;
        var calls = await settings.GetAsync("calls", cancellationToken);
        await settings.SetAsync("calls", string.IsNullOrEmpty(calls) ? appId : calls + "," + appId, cancellationToken);
        if (await settings.GetAsync("unavailable", cancellationToken) == appId) return null;
        if (appId != "3" || await settings.GetAsync("empty", cancellationToken) == "true") return [];
        var revision = await settings.GetAsync("revision", cancellationToken) ?? "old";
        return [new("hero-" + appId, $"https://art.example/hero-{appId}-{revision}.png", 3840, 2160) { ImageType = "hero" }];
    }
}

public sealed class NullShapeSyncPlugin : ILibrarySourcePlugin, IMetadataProviderPlugin, IArtworkProviderPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public Task<IReadOnlyList<PluginLibraryGame>?> GetLibraryAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PluginLibraryGame>?>([null!, new("nullable-game", "Game without external IDs") { ExternalIds = null! }]);
    public Task<PluginMetadata?> GetMetadataAsync(PluginGame game, CancellationToken cancellationToken = default)
        => Task.FromResult<PluginMetadata?>(new() { Genres = null!, Tags = null! });
    public Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PluginArtwork>?>([null!, new("null-url", null!, 3840, 2160)]);
}

public sealed class BrokenMetadataPlugin : IMetadataProviderPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public Task<PluginMetadata?> GetMetadataAsync(PluginGame game, CancellationToken cancellationToken = default)
        => Task.FromResult<PluginMetadata?>(new() { Genres = new BrokenNames() });

    private sealed class BrokenNames : IReadOnlyList<string>
    {
        public int Count => 1;
        public string this[int index] => throw new InvalidOperationException("fixture-private-key-must-not-escape");
        public IEnumerator<string> GetEnumerator() => throw new InvalidOperationException("fixture-private-key-must-not-escape");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
