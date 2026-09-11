using Microsoft.Extensions.Configuration;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb.Auth;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;

namespace Winnow.App.Services;

public sealed record IgdbSettingsSnapshot(string ClientId, bool HasSavedCredentials, bool IsReadable,
    bool HasConfigurationCredentials);

public enum IgdbSettingsSaveResult { Saved, MissingFields, ProtectionUnavailable }

/// <summary>Protected local settings operations shared by both presentation paths.</summary>
public interface IIgdbSettingsService
{
    Task<IgdbSettingsSnapshot> LoadAsync(CancellationToken ct = default);
    Task<IgdbSettingsSaveResult> SaveAsync(string clientId, string clientSecret);
    Task<bool> RemoveAsync();
}

public sealed class IgdbSettingsService(
    ISettingsStore settings,
    IIgdbSecretProtector protector,
    IUnitOfWorkFactory unitOfWork,
    IConfiguration? configuration = null,
    IIgdbCredentialUpdater? credentialUpdater = null) : IIgdbSettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public event Action? CredentialsChanged;

    private bool HasConfigurationCredentials => IgdbCredentials.TryCreate(
        configuration?["Igdb:ClientId"], configuration?["Igdb:ClientSecret"], "configuration") is not null;

    public Task<IgdbSettingsSnapshot> LoadAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Keep automatic legacy migration identical to the runtime credential source.
            var credentials = await new SettingsTableCredentialSource(settings, protector).TryGetAsync(ct);
            var id = await settings.GetAsync(SettingsTableCredentialSource.ClientIdKey, ct);
            var secret = await settings.GetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey, ct);
            var legacy = await settings.GetAsync(SettingsTableCredentialSource.ClientSecretKey, ct);
            return new IgdbSettingsSnapshot(id ?? string.Empty,
                !string.IsNullOrWhiteSpace(id) || !string.IsNullOrWhiteSpace(secret) || !string.IsNullOrWhiteSpace(legacy),
                credentials is not null, HasConfigurationCredentials);
        }
        finally { _gate.Release(); }
    }, ct);

    public Task<IgdbSettingsSaveResult> SaveAsync(string clientId, string clientSecret) => Task.Run(async () =>
    {
        var id = clientId.Trim();
        var secret = clientSecret.Trim();
        if (id.Length == 0 || secret.Length == 0) return IgdbSettingsSaveResult.MissingFields;
        var protectedSecret = protector.Protect(secret);
        if (string.IsNullOrWhiteSpace(protectedSecret)) return IgdbSettingsSaveResult.ProtectionUnavailable;
        await MutateAsync(async () =>
        {
            using var transaction = unitOfWork.Begin();
            await settings.SetAsync(SettingsTableCredentialSource.ClientIdKey, id);
            await settings.SetAsync(SettingsTableCredentialSource.ClientSecretProtectedKey, protectedSecret);
            await settings.RemoveAsync(SettingsTableCredentialSource.ClientSecretKey);
            await ClearTokenAsync();
            transaction.Commit();
        });
        return IgdbSettingsSaveResult.Saved;
    });

    /// <returns>Whether credentials from configuration remain available after removal.</returns>
    public Task<bool> RemoveAsync() => Task.Run(async () =>
    {
        var hasConfiguration = HasConfigurationCredentials;
        await MutateAsync(async () =>
        {
            using var transaction = unitOfWork.Begin();
            await settings.RemoveAsync(SettingsTableCredentialSource.ClientIdKey);
            await settings.RemoveAsync(SettingsTableCredentialSource.ClientSecretProtectedKey);
            await settings.RemoveAsync(SettingsTableCredentialSource.ClientSecretKey);
            await ClearTokenAsync();
            transaction.Commit();
        });
        return hasConfiguration;
    });

    private async Task MutateAsync(Func<Task> update)
    {
        await _gate.WaitAsync();
        try
        {
            if (credentialUpdater is null) await update();
            else await credentialUpdater.UpdateCredentialsAsync(update);
        }
        finally { _gate.Release(); }
        CredentialsChanged?.Invoke();
    }

    private async Task ClearTokenAsync()
    {
        await settings.RemoveAsync(TwitchTokenProvider.TokenBlobKey);
        await settings.RemoveAsync(TwitchTokenProvider.TokenClientIdKey);
        await settings.RemoveAsync(TwitchTokenProvider.TokenValueKey);
        await settings.RemoveAsync(TwitchTokenProvider.TokenExpiresAtKey);
    }
}
