namespace Winnow.PluginSdk;

public static class PluginCapabilities
{
    public const string Library = "library";
    public const string Metadata = "metadata";
    public const string Artwork = "artwork";
    public const string Recommendations = "recommendations";
}

/// <summary>Describes plugin.json. The host reads it before executing any plugin code.</summary>
public sealed record PluginManifest
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public int ApiVersion { get; init; } = PluginApi.Version;
    public string EntryAssembly { get; init; } = "";
    public string EntryType { get; init; } = "";
    public string? Description { get; init; }
    public string? Website { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = [];
    public IReadOnlyList<PluginSettingDefinition> Settings { get; init; } = [];
    public PluginNetworkOptions Network { get; init; } = new();
}

public sealed record PluginSettingDefinition
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public bool Secret { get; init; }
    public bool Required { get; init; }
    public string? Help { get; init; }
    public string? SetupUrl { get; init; }
}

public sealed record PluginNetworkOptions
{
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];
    public double RequestsPerSecond { get; init; } = 1;
    public int MaxRetries { get; init; } = 2;
    public int MaxResponseBytes { get; init; } = 2 * 1024 * 1024;
    public int TimeoutSeconds { get; init; } = 90;
}
