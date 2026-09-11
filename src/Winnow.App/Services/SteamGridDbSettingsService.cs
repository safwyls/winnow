using Winnow.Enrich.SteamGridDb;

namespace Winnow.App.Services;

public enum SteamGridDbSettingsSaveResult { Saved, MissingKey, InvalidKey, ProtectionUnavailable }
public sealed record SteamGridDbSettingsSnapshot(bool HasStoredKey, bool IsConfigured, bool CanSave, string? Source);

public interface ISteamGridDbSettingsService
{
    Task<SteamGridDbSettingsSnapshot> LoadAsync(CancellationToken ct = default);
    Task<SteamGridDbSettingsSaveResult> SaveAsync(string apiKey);
    Task<bool> RemoveAsync();
}

/// <summary>Protected key editing shared by both presentations, with no secret in read results.</summary>
public sealed class SteamGridDbSettingsService(ISteamGridDbSettingsStore store) : ISteamGridDbSettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public event Action? CredentialsChanged;

    public Task<SteamGridDbSettingsSnapshot> LoadAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        await _gate.WaitAsync(ct);
        try
        {
            var status = await store.GetStatusAsync(ct);
            return new SteamGridDbSettingsSnapshot(status.HasStoredKey, status.IsConfigured, status.CanSave, status.Source);
        }
        finally { _gate.Release(); }
    }, ct);

    public Task<SteamGridDbSettingsSaveResult> SaveAsync(string apiKey) => Task.Run(async () =>
    {
        var key = apiKey.Trim();
        if (key.Length == 0) return SteamGridDbSettingsSaveResult.MissingKey;
        await _gate.WaitAsync();
        try
        {
            if (!await store.SaveAsync(key))
                return (await store.GetStatusAsync()).CanSave
                    ? SteamGridDbSettingsSaveResult.InvalidKey : SteamGridDbSettingsSaveResult.ProtectionUnavailable;
        }
        finally { _gate.Release(); }
        CredentialsChanged?.Invoke();
        return SteamGridDbSettingsSaveResult.Saved;
    });

    public Task<bool> RemoveAsync() => Task.Run(async () =>
    {
        bool configured;
        await _gate.WaitAsync();
        try
        {
            await store.RemoveAsync();
            configured = (await store.GetStatusAsync()).IsConfigured;
        }
        finally { _gate.Release(); }
        CredentialsChanged?.Invoke();
        return configured;
    });
}
