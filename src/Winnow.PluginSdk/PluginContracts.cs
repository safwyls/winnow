namespace Winnow.PluginSdk;

public static class PluginApi
{
    public const int Version = 1;
}

/// <summary>Plugins run as trusted code in the host process. This API is not a security sandbox.</summary>
public interface IPlugin
{
    ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default);
}

/// <summary>A null result means unavailable; an empty result confirms that no entries were found.</summary>
public interface ILibrarySourcePlugin : IPlugin
{
    Task<IReadOnlyList<PluginLibraryGame>?> GetLibraryAsync(CancellationToken cancellationToken = default);
}

public interface IMetadataProviderPlugin : IPlugin
{
    Task<PluginMetadata?> GetMetadataAsync(PluginGame game, CancellationToken cancellationToken = default);
}

/// <summary>A null result preserves older artwork; an empty result replaces this source with no candidates.</summary>
public interface IArtworkProviderPlugin : IPlugin
{
    Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default);
}

public interface IRecommendationFeedPlugin : IPlugin
{
    Task<IReadOnlyList<PluginRecommendation>?> GetRecommendationsAsync(
        IReadOnlyList<PluginGame> library, CancellationToken cancellationToken = default);
}

/// <summary>Id is an opaque host handle. External IDs are the only safe cross-provider identity joins.</summary>
public sealed record PluginGame(string Id, string Title, IReadOnlyDictionary<string, string> ExternalIds)
{
    public bool Installed { get; init; }
    public long PlaytimeMinutes { get; init; }
    public DateTimeOffset? LastPlayedAt { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record PluginLibraryGame(string SourceId, string Title)
{
    public IReadOnlyDictionary<string, string> ExternalIds { get; init; } = new Dictionary<string, string>();
    public string? AccountRef { get; init; }
    public string? InstallPath { get; init; }
    public bool? Installed { get; init; }
    public long? PlaytimeMinutes { get; init; }
    public DateTimeOffset? LastPlayedAt { get; init; }
    public DateTimeOffset? AcquiredAt { get; init; }
}

/// <summary>Source-attributed observations; the host decides which missing canonical values to fill.</summary>
public sealed record PluginMetadata
{
    public string? Summary { get; init; }
    public DateTimeOffset? ReleaseDate { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public enum PluginArtworkKind { Background, Cover, Screenshot }

public sealed record PluginArtwork(string Id, string Url, int Width, int Height)
{
    public PluginArtworkKind Kind { get; init; } = PluginArtworkKind.Background;
    public string? ImageType { get; init; }
    public bool Animated { get; init; }
    public bool Transparent { get; init; }
}

/// <summary>GameId must name a supplied library entry; Score ranges from zero to one.</summary>
public sealed record PluginRecommendation(string GameId, double Score, string Reason);
