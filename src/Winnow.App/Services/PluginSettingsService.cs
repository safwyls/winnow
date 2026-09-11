namespace Winnow.App.Services;

/// <summary>Presentation snapshots never contain stored secret values.</summary>
public sealed record PluginSettingSnapshot(
    string Key, string Label, string? Description, bool IsSecret, bool IsRequired,
    string? Value, bool HasStoredSecret, string? SetupUrl = null);

public sealed record PluginSettingsSnapshot(
    string Id, string Name, string Description, string Version, string Capabilities,
    bool Enabled, bool IsLoaded, bool RestartRequired, string Status,
    IReadOnlyList<PluginSettingSnapshot> Settings, string? WebsiteUrl = null, bool CanConfigure = true);

public interface IPluginSettingsBackend
{
    string UserPluginDirectory { get; }
    Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);
    Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default);
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default);
    Task RefreshAsync(string pluginId, CancellationToken ct = default);
}
