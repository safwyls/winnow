using Winnow.Plugins;

namespace Winnow.App.Services;

public sealed class PluginSettingsBackend(PluginCatalog catalog, PluginStorage storage, string userPluginDirectory) : IPluginSettingsBackend
{
    public string UserPluginDirectory { get; } = userPluginDirectory;
    public event Action? RefreshRequested;

    public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        var snapshots = new List<PluginSettingsSnapshot>();
        foreach (var plugin in catalog.Plugins.ToArray())
        {
            var fields = new List<PluginSettingSnapshot>();
            foreach (var field in plugin.Manifest.Settings)
                fields.Add(new(field.Key, field.Label, field.Help, field.Secret, field.Required,
                    field.Secret ? null : await storage.ReadSettingAsync(plugin.Manifest.Id, field.Key, ct),
                    field.Secret && await storage.HasStoredSecretAsync(plugin.Manifest.Id, field.Key, ct), field.SetupUrl));
            snapshots.Add(new(plugin.Manifest.Id, plugin.Manifest.Name, plugin.Manifest.Description ?? "",
                plugin.Manifest.Version, string.Join(", ", plugin.Manifest.Capabilities.Select(c => c switch
                { "library" => "Library sources", "metadata" => "Metadata", "artwork" => "Artwork", _ => "Recommendation feeds" })), plugin.Enabled, plugin.Loaded,
                plugin.RestartRequired, plugin.Error ?? (plugin.RestartRequired ? "Restart Winnow to apply this change."
                    : plugin.Loaded ? "Enabled" : "Disabled"), fields, plugin.Manifest.Website));
        }
        foreach (var issue in catalog.Issues.ToArray())
            snapshots.Add(new("invalid:" + issue.DirectoryPath, Path.GetFileName(issue.DirectoryPath), "This plugin could not be loaded.", "", "",
                false, false, false, issue.Message, [], CanConfigure: false));
        return (IReadOnlyList<PluginSettingsSnapshot>)snapshots;
    }, ct);

    public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) => Task.Run(async () =>
    {
        var plugin = Find(pluginId);
        foreach (var (key, value) in values)
        {
            var field = plugin.Manifest.Settings.SingleOrDefault(s => s.Key == key) ?? throw new ArgumentException("Unknown setting.");
            if (value.Length > 4096 || field.Required && string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Invalid setting.");
            if (field.Secret && (!OperatingSystem.IsWindows() || value.Any(char.IsControl))) throw new InvalidOperationException("Credential cannot be saved.");
        }
        foreach (var (key, value) in values)
        {
            var field = plugin.Manifest.Settings.Single(s => s.Key == key);
            if (field.Secret) await storage.WriteSecretAsync(pluginId, key, value.Trim(), ct);
            else await storage.WriteSettingAsync(pluginId, key, value, ct);
        }
        RefreshRequested?.Invoke();
    }, ct);

    public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) => Task.Run(async () =>
    {
        if (!Find(pluginId).Manifest.Settings.Any(s => s.Key == key && s.Secret)) throw new ArgumentException("Unknown secret.");
        await storage.RemoveSecretAsync(pluginId, key, ct);
        RefreshRequested?.Invoke();
    }, ct);

    public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
        => Task.Run(() => catalog.SetEnabledAsync(pluginId, enabled, ct), ct);

    public Task RefreshAsync(string pluginId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Find(pluginId).Loaded) RefreshRequested?.Invoke();
        return Task.CompletedTask;
    }

    private PluginDescriptor Find(string id) => catalog.Plugins.SingleOrDefault(p => p.Manifest.Id == id)
        ?? throw new ArgumentException("Unknown plugin.");
}
