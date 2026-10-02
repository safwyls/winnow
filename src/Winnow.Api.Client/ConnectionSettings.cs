using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;
using Winnow.PluginSdk;

namespace Winnow.Api.Client;

public sealed class ConnectionIgdbSettingsService(WinnowApiClient api) : IIgdbSettingsService
{
    public Task<IgdbSettingsSnapshot> LoadAsync(CancellationToken ct = default) => api.GetAsync<IgdbSettingsSnapshot>("connections/igdb", ct);
    public Task<IgdbSettingsSaveResult> SaveAsync(string clientId, string clientSecret) =>
        api.SendAsync<SaveIgdbCredentials, IgdbSettingsSaveResult>(HttpMethod.Put, "connections/igdb", new(clientId, clientSecret));
    public Task<bool> RemoveAsync() => api.SendAsync<object?, bool>(HttpMethod.Delete, "connections/igdb", null);
}

public sealed class ConnectionPluginSettingsBackend(WinnowApiClient api) : IPluginSettingsBackend
{
    private readonly string _clientId = Guid.NewGuid().ToString("N");
    public string UserPluginDirectory { get; private set; } = string.Empty;

    public async Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default)
    {
        UserPluginDirectory = (await api.GetAsync<PluginDirectoryResponse>("connections/plugins/directory", ct)).Directory;
        return await api.GetAsync<IReadOnlyList<PluginSettingsSnapshot>>("connections/plugins", ct);
    }

    public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) =>
        api.SendAsync(HttpMethod.Put, Path(pluginId) + "/settings", new PluginSettingsValues(values), ct);
    public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) =>
        api.SendAsync<object?>(HttpMethod.Delete, Path(pluginId) + "/secrets/" + Uri.EscapeDataString(key), null, ct);
    public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) =>
        api.SendAsync(HttpMethod.Put, Path(pluginId) + "/enabled", new SetPluginEnabled(enabled), ct);
    public Task RefreshAsync(string pluginId, CancellationToken ct = default) =>
        api.SendAsync<object?>(HttpMethod.Post, Path(pluginId) + "/refresh", null, ct);
    public async Task<PluginSignInChallenge?> BeginSignInAsync(string pluginId, CancellationToken ct = default) =>
        (await api.SendAsync<PluginSignInRequest, PluginChallengeResponse>(HttpMethod.Post, Path(pluginId) + "/sign-in", new(_clientId), ct: ct)).Challenge;
    public Task<PluginSignInResult> PollSignInAsync(string pluginId, string attemptId, CancellationToken ct = default) =>
        api.SendAsync<PluginSignInAttempt, PluginSignInResult>(HttpMethod.Post, Path(pluginId) + "/sign-in/poll", new(_clientId, attemptId), ct: ct);
    public Task SignOutAsync(string pluginId, CancellationToken ct = default) =>
        api.SendAsync<object?>(HttpMethod.Post, Path(pluginId) + "/sign-out", null, ct);
    public Task CancelSignInAsync(string pluginId, string attemptId, CancellationToken ct = default) =>
        api.SendAsync(HttpMethod.Post, Path(pluginId) + "/sign-in/cancel", new PluginSignInAttempt(_clientId, attemptId), ct);
    private static string Path(string pluginId) => "connections/plugins/" + Uri.EscapeDataString(pluginId);
}
