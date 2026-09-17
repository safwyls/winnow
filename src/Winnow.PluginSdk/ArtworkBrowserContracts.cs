namespace Winnow.PluginSdk;

/// <summary>Optional interactive capability; existing automatic artwork providers remain valid.</summary>
public interface IArtworkBrowserPlugin : IPlugin
{
    IReadOnlyList<PluginArtworkKind> SupportedArtworkKinds { get; }

    Task<PluginArtworkPage> BrowseArtworkAsync(PluginGame game, PluginArtworkKind kind,
        string? cursor = null, CancellationToken cancellationToken = default);
}

public enum PluginArtworkAvailability { Available, SetupRequired, Unsupported, Unavailable }

/// <summary>Cursors are opaque to the host. A missing next cursor means the last page.</summary>
public sealed record PluginArtworkPage(IReadOnlyList<PluginArtwork> Items, string? NextCursor = null)
{
    public PluginArtworkAvailability Availability { get; init; } = PluginArtworkAvailability.Available;
    public string? Message { get; init; }
}
