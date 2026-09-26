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
