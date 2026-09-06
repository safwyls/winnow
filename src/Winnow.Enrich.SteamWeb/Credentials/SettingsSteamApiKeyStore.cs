using Winnow.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>What a save attempt did. The panel turns each value into a sentence.</summary>
public enum SteamApiKeySaveOutcome
{
    /// <summary>The key was protected and written; the old value is gone.</summary>
    Stored,

    /// <summary>
    /// Nothing was written. The host cannot encrypt at rest (or the container has
    /// no store), and §4.7's rule is to refuse rather than degrade to a plaintext
    /// row. The key still works in memory for whoever supplied it in another way.
    /// </summary>
    Refused,
}

/// <summary>
/// The one owner of the Web API key at rest. Reads, writes and clears all go
/// through here, because a key that is protected on one path and plaintext on
/// another is not protected.
/// </summary>
public interface ISteamApiKeyStore
{
    /// <summary>
    /// The stored key, decrypted, or null when there is none this host can read.
    /// Never a secret in logs — the caller stamps provenance, not value.
    /// </summary>
    ValueTask<string?> GetAsync(CancellationToken ct = default);

    /// <summary>Stores the key encrypted, or refuses. See <see cref="SteamApiKeySaveOutcome"/>.</summary>
    Task<SteamApiKeySaveOutcome> SaveAsync(string key, CancellationToken ct = default);

    /// <summary>Removes the stored key. No protector is needed to remove a value.</summary>
    Task ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// Persists the Web API key in the settings table as one DPAPI-protected row,
/// mirroring <see cref="SettingsSteamSessionStore"/>. Refuses to store
/// unencrypted; no-op when the repository is absent.
///
/// <para>Two rows, one meaning. <see cref="ProtectedSetting"/> holds the
/// protected blob and is the only row anything writes today;
/// <see cref="SettingsTableApiKeySource.ApiKeySetting"/> is the plaintext row
/// an install older than this store may still have. It is read once, migrated
/// into the protected row, and left empty — read, not destroyed, so a host
/// that cannot encrypt leaves what the user typed alone and simply refuses to
/// use it.</para>
/// </summary>
public sealed class SettingsSteamApiKeyStore : ISteamApiKeyStore
{
    /// <summary>
    /// Settings key holding the protected key. Versioned exactly as
    /// <c>steam.session.v1</c> is: a future change to the payload shape takes a
    /// new key rather than trying to interpret an old one.
    /// </summary>
    public const string ProtectedSetting = "steam.api_key.v1";

    private readonly ISettingsRepository? _settings;
    private readonly ISteamApiKeyProtector _protector;
    private readonly ILogger<SettingsSteamApiKeyStore> _log;

    private bool _warnedAboutProtection;

    public SettingsSteamApiKeyStore(
        ISettingsRepository? settings,
        ISteamApiKeyProtector protector,
        ILogger<SettingsSteamApiKeyStore>? log = null)
    {
        _settings = settings;
        _protector = protector;
        _log = log ?? NullLogger<SettingsSteamApiKeyStore>.Instance;
    }

    public async ValueTask<string?> GetAsync(CancellationToken ct = default)
    {
        if (_settings is null)
        {
            return null;
        }

        var stored = await _settings.GetAsync(ProtectedSetting, ct);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            var key = _protector.Unprotect(stored);
            if (!string.IsNullOrWhiteSpace(key))
            {
                // Retry cleanup if migration stopped after writing the protected row.
                if (!string.IsNullOrEmpty(await _settings.GetAsync(SettingsTableApiKeySource.ApiKeySetting, ct)))
                {
                    await _settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, string.Empty, ct);
                }

                return key;
            }

            // The row exists but this host cannot read it. Not an error to the
            // user — the key is simply not in force here and has to be re-entered.
            return null;
        }

        return await MigrateLegacyRowAsync(ct);
    }

    public async Task<SteamApiKeySaveOutcome> SaveAsync(string key, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_settings is null)
        {
            return SteamApiKeySaveOutcome.Refused;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            // The empty string is the cleared state rather than a deleted row:
            // ISettingsRepository has no delete, and every reader treats blank as
            // unset. Saving nothing is clearing.
            await ClearAsync(ct);
            return SteamApiKeySaveOutcome.Stored;
        }

        var protectedValue = _protector.Protect(key.Trim());
        if (protectedValue is null)
        {
            WarnAboutProtection();
            return SteamApiKeySaveOutcome.Refused;
        }

        await _settings.SetAsync(ProtectedSetting, protectedValue, ct);
        await _settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, string.Empty, ct);
        return SteamApiKeySaveOutcome.Stored;
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (_settings is null)
        {
            return;
        }

        // Empty rather than a delete: ISettingsRepository is a two-method key/value
        // contract with no remove, and empty is unambiguously "unset" to every
        // reader. Both rows, so a pre-migration install is cleared in one press
        // too.
        await _settings.SetAsync(ProtectedSetting, string.Empty, ct);
        await _settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, string.Empty, ct);
        _log.LogInformation("Stored Steam Web API key cleared.");
    }

    /// <summary>
    /// The one-time move of a pre-protection install's key into the protected
    /// row. Returns the key only after protection succeeds; an unavailable
    /// protector leaves the legacy value intact but refuses to use it.
    /// </summary>
    private async Task<string?> MigrateLegacyRowAsync(CancellationToken ct)
    {
        var legacy = await _settings!.GetAsync(SettingsTableApiKeySource.ApiKeySetting, ct);
        if (string.IsNullOrWhiteSpace(legacy))
        {
            return null;
        }

        if (!_protector.IsAvailable)
        {
            // A host that cannot encrypt refuses to use a plaintext credential
            // rather than letting the row keep paying out. What the user typed is
            // left exactly where it was — refusing never destroys user material —
            // and the panel says the key has to be re-entered or supplied by
            // configuration.
            if (!_warnedAboutProtection)
            {
                _warnedAboutProtection = true;
                _log.LogWarning(
                    "A Steam Web API key is present in the clear from an earlier version, and this host "
                    + "cannot encrypt at rest ({Protector}), so it is not used. Supply it through the "
                    + "Steam__ApiKey environment variable instead, or remove the settings row by hand.",
                    _protector.Name);
            }

            return null;
        }

        var protectedValue = _protector.Protect(legacy.Trim());
        if (protectedValue is null)
        {
            WarnAboutProtection();
            return null;
        }

        await _settings.SetAsync(ProtectedSetting, protectedValue, ct);
        await _settings.SetAsync(SettingsTableApiKeySource.ApiKeySetting, string.Empty, ct);
        _log.LogInformation("The stored Steam Web API key was migrated to protected storage; the plaintext row was emptied.");
        return legacy.Trim();
    }

    private void WarnAboutProtection()
    {
        if (_warnedAboutProtection)
        {
            return;
        }

        _warnedAboutProtection = true;
        _log.LogWarning(
            "The Steam Web API key cannot be encrypted at rest on this host ({Protector}), so it will not "
            + "be stored. Storing it unencrypted is deliberately not offered: the key reads the full "
            + "owned-games list of the account it belongs to.",
            _protector.Name);
    }
}
