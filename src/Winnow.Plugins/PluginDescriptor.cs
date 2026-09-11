using Winnow.PluginSdk;

namespace Winnow.Plugins;

public interface IPluginStateStore
{
    ValueTask<bool?> GetEnabledAsync(string pluginId, CancellationToken cancellationToken = default);
    ValueTask SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default);
}

public interface IPluginContextFactory
{
    IPluginContext Create(PluginManifest manifest);
}

public sealed class PluginDescriptor
{
    private volatile bool _enabled;
    private volatile bool _loaded;
    private volatile string? _error;
    public required PluginManifest Manifest { get; init; }
    public required string DirectoryPath { get; init; }
    public bool BuiltIn { get; init; }
    /// <summary>Desired state for the next launch; changing it does not hot-load or unload code.</summary>
    public bool Enabled { get => _enabled; internal set => _enabled = value; }
    public bool Loaded { get => _loaded; internal set => _loaded = value; }
    public string? Error { get => _error; internal set => _error = value; }
    public bool RestartRequired => Enabled != Loaded && Error is null;
    internal IPlugin? Instance { get; set; }
    internal PluginLoadContext? LoadContext { get; set; }
    internal SemaphoreSlim InvocationGate { get; } = new(1, 1);
}

public sealed record PluginDiscoveryIssue(string DirectoryPath, string Message);
