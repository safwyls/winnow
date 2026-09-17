using Winnow.Plugins;
using Winnow.PluginSdk;

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
            foreach (var field in plugin.Manifest.Settings.Where(field => !field.ManagedByPlugin))
                fields.Add(new(field.Key, field.Label, field.Help, field.Secret, field.Required,
                    field.Secret ? null : await storage.ReadSettingAsync(plugin.Manifest.Id, field.Key, ct),
                    field.Secret && await storage.HasStoredSecretAsync(plugin.Manifest.Id, field.Key, ct), field.SetupUrl, field.IsBoolean));
            var hasAccount = plugin.Manifest.Capabilities.Contains(PluginCapabilities.Account);
            var account = hasAccount && plugin.Loaded
                ? await catalog.InvokeAsync<PluginAccountStatus>(plugin, (instance, token) => ((IPluginAccount)instance).GetAccountStatusAsync(token)!, ct)
                : null;
            snapshots.Add(new(plugin.Manifest.Id, plugin.Manifest.Name, plugin.Manifest.Description ?? "",
                plugin.Manifest.Version, string.Join(", ", plugin.Manifest.Capabilities.Select(c => c switch
                { "library" => "Library sources", "metadata" => "Metadata", "artwork" => "Artwork", "account" => "Account connection",
                    "game-actions" => "Game actions", "recommendations" => "Recommendation feeds", _ => c })), plugin.Enabled, plugin.Loaded,
                plugin.RestartRequired, plugin.Error ?? (plugin.RestartRequired ? "Restart Winnow to apply this change."
                    : string.Empty), fields, plugin.Manifest.Website, HasAccount: hasAccount,
                AccountConnected: account?.Connected == true, AccountHosts: plugin.Manifest.Network.AllowedHosts));
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
            var field = plugin.Manifest.Settings.SingleOrDefault(s => s.Key == key && !s.ManagedByPlugin) ?? throw new ArgumentException("Unknown setting.");
            if (value.Length > 4096 || field.Required && string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Invalid setting.");
            if (field.IsBoolean && !bool.TryParse(value, out _)) throw new ArgumentException("Invalid boolean setting.");
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
        if (!Find(pluginId).Manifest.Settings.Any(s => s.Key == key && s.Secret && !s.ManagedByPlugin)) throw new ArgumentException("Unknown secret.");
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

    public async Task<PluginSignInChallenge?> BeginSignInAsync(string pluginId, CancellationToken ct = default)
    {
        var plugin = FindAccount(pluginId);
        var challenge = await catalog.InvokeAsync<PluginSignInChallenge>(plugin,
            (instance, token) => ((IPluginAccount)instance).BeginSignInAsync(token), ct);
        if (challenge is null) return null;
        if (PluginSignInValidation.IsValid(challenge, plugin.Manifest.Network.AllowedHosts, DateTimeOffset.UtcNow)) return challenge;
        await CancelSignInAsync(pluginId, challenge.AttemptId, ct);
        throw new InvalidOperationException("The sign-in challenge is invalid.");
    }

    public async Task<PluginSignInResult> PollSignInAsync(string pluginId, string attemptId, CancellationToken ct = default)
    {
        var result = await catalog.InvokeAsync<PluginSignInResult>(FindAccount(pluginId),
            (instance, token) => ((IPluginAccount)instance).PollSignInAsync(attemptId, token)!, ct);
        if (result?.State == PluginSignInState.Connected) RefreshRequested?.Invoke();
        return result is null ? new(PluginSignInState.Failed, string.Empty) : result with { Message = string.Empty };
    }

    public async Task SignOutAsync(string pluginId, CancellationToken ct = default)
    {
        var result = await catalog.InvokeAsync<object>(FindAccount(pluginId), async (instance, token) =>
        {
            await ((IPluginAccount)instance).SignOutAsync(token);
            return new object();
        }, ct);
        if (result is null) throw new InvalidOperationException("The account could not be disconnected.");
        RefreshRequested?.Invoke();
    }

    public async Task CancelSignInAsync(string pluginId, string attemptId, CancellationToken ct = default)
    {
        await catalog.InvokeAsync<object>(FindAccount(pluginId), async (instance, token) =>
        {
            await ((IPluginAccount)instance).CancelSignInAsync(attemptId, token);
            return new object();
        }, ct);
    }

    private PluginDescriptor FindAccount(string id)
    {
        var plugin = Find(id);
        return plugin.Loaded && plugin.Manifest.Capabilities.Contains(PluginCapabilities.Account)
            ? plugin : throw new InvalidOperationException("The account provider is unavailable.");
    }

    private PluginDescriptor Find(string id) => catalog.Plugins.SingleOrDefault(p => p.Manifest.Id == id)
        ?? throw new ArgumentException("Unknown plugin.");
}
