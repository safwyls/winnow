using System.Text.Json;
using System.Text.Json.Serialization;
using Winnow.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// Persists the Epic session in the §6 <c>settings</c> table as a single
/// DPAPI-encrypted blob.
///
/// <para><b>Encrypted before it reaches the database, never after.</b> The whole
/// session is serialised to JSON, handed to <see cref="IEpicSecretProtector"/>,
/// and only the base64 ciphertext is written. The database therefore never holds
/// the access token, the refresh token, the account id or the display name in
/// the clear — which matters more here than for the Steam key, because an Epic
/// refresh token is, in the words Epic itself prints on the page that issues the
/// authorization code, "full access to your Epic account".</para>
///
/// <para><b>If it cannot be encrypted it is not written.</b> When the protector
/// is unavailable or fails, <see cref="SaveAsync"/> writes nothing and says so
/// once. There is deliberately no plaintext path, not even a guarded one: the
/// failure mode of a plaintext fallback is silent and permanent, while the
/// failure mode of refusing is a login the user repeats after a restart.</para>
///
/// <para><b>One key, not seven.</b> The session fields are only ever useful
/// together — an access token without its expiry is unusable, and a refresh
/// token without the client id that minted it cannot be spent. Storing them as
/// one blob makes a partial write impossible, and makes a damaged ciphertext
/// unreadable-and-therefore-discarded rather than half-readable.</para>
///
/// <para><b>The settings repository is optional</b>, for the module-boundary
/// reason set out on <see cref="Credentials.SettingsTableEpicCredentialSource"/>:
/// <c>Winnow.Ingest.Epic</c> does not reference <c>Winnow.Data</c>. No repository
/// means no persistence, which is the same in-memory-only behaviour as an
/// unavailable protector.</para>
/// </summary>
public sealed class SettingsEpicTokenStore : IEpicTokenStore
{
    /// <summary>
    /// Settings key holding the encrypted session. Namespaced per the §6
    /// convention, and versioned: a future change to the payload shape takes a
    /// new key rather than trying to interpret an old one, so a downgrade cannot
    /// read a blob it does not understand.
    /// </summary>
    public const string SessionSetting = "epic.oauth.session.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ISettingsRepository? _settings;
    private readonly IEpicSecretProtector _protector;
    private readonly ILogger<SettingsEpicTokenStore> _log;

    private bool _warnedAboutProtection;

    public SettingsEpicTokenStore(
        ISettingsRepository? settings,
        IEpicSecretProtector protector,
        ILogger<SettingsEpicTokenStore>? log = null)
    {
        _settings = settings;
        _protector = protector;
        _log = log ?? NullLogger<SettingsEpicTokenStore>.Instance;
    }

    public bool CanPersist => _settings is not null && _protector.IsAvailable;

    public async Task<EpicOAuthToken?> LoadAsync(CancellationToken ct = default)
    {
        if (_settings is null)
        {
            return null;
        }

        var stored = await _settings.GetAsync(SessionSetting, ct);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var json = _protector.Unprotect(stored);
        if (json is null)
        {
            // The protector has already said why, at the right level. Nothing to
            // add here, and nothing about the stored value is safe to print.
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<StoredSession>(json, SerializerOptions);
            return payload?.ToToken();
        }
        catch (JsonException)
        {
            // Decrypted successfully but did not parse — a shape change, or a
            // truncated write. Same remedy as an unreadable blob: no session.
            // The exception object is never logged; its message quotes the JSON,
            // and the JSON is the session.
            _log.LogWarning("Stored Epic session could not be parsed; a fresh sign-in is required.");
            return null;
        }
    }

    public async Task SaveAsync(EpicOAuthToken token, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (_settings is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(StoredSession.From(token), SerializerOptions);
        var protectedValue = _protector.Protect(json);
        if (protectedValue is null)
        {
            if (!_warnedAboutProtection)
            {
                _warnedAboutProtection = true;
                _log.LogWarning(
                    "Epic session cannot be encrypted at rest on this host ({Protector}), so it will not be "
                    + "stored. Sign-in works for this run and has to be repeated after a restart. Storing it "
                    + "unencrypted is deliberately not offered: the refresh token grants full access to the "
                    + "Epic account.",
                    _protector.Name);
            }

            return;
        }

        await _settings.SetAsync(SessionSetting, protectedValue, ct);
        _log.LogDebug("Epic session stored, encrypted with {Protector}.", _protector.Name);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (_settings is null)
        {
            return;
        }

        // Empty rather than a delete: ISettingsRepository is a two-method
        // key/value contract with no remove, and empty is unambiguously "unset"
        // to every reader here — LoadAsync treats blank as no session.
        await _settings.SetAsync(SessionSetting, string.Empty, ct);
        _log.LogInformation("Stored Epic session cleared.");
    }

    /// <summary>
    /// The persisted shape. Property names are explicit so a rename in
    /// <see cref="EpicOAuthToken"/> cannot silently orphan every stored session.
    /// </summary>
    private sealed record StoredSession
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; init; } = string.Empty;

        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("account_id")]
        public string AccountId { get; init; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; init; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; init; }

        // Nullable, and stored as null when Epic did not state one. Writing a
        // sentinel here would turn "not stated" into a date on the way back in.
        [JsonPropertyName("refresh_expires_at")]
        public DateTimeOffset? RefreshExpiresAt { get; init; }

        public static StoredSession From(EpicOAuthToken token) => new()
        {
            ClientId = token.ClientId,
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            AccountId = token.AccountId,
            DisplayName = token.DisplayName,
            ExpiresAt = token.ExpiresAt,
            RefreshExpiresAt = token.RefreshExpiresAt,
        };

        /// <summary>
        /// Null when a required field is missing. A session missing the client
        /// id, either token, or the account id cannot be used for anything, and
        /// returning a half-built one would only move the failure to the first
        /// request.
        /// </summary>
        public EpicOAuthToken? ToToken()
            => string.IsNullOrWhiteSpace(ClientId)
                || string.IsNullOrWhiteSpace(AccessToken)
                || string.IsNullOrWhiteSpace(RefreshToken)
                || string.IsNullOrWhiteSpace(AccountId)
                    ? null
                    : new EpicOAuthToken(
                        ClientId, AccessToken, RefreshToken, AccountId, DisplayName, ExpiresAt, RefreshExpiresAt);
    }
}

/// <summary>
/// Non-persistent <see cref="IEpicTokenStore"/>, for tests and for hosts running
/// without a database. The session lives as long as the process.
/// </summary>
public sealed class InMemoryEpicTokenStore : IEpicTokenStore
{
    private EpicOAuthToken? _token;

    public bool CanPersist => false;

    public Task<EpicOAuthToken?> LoadAsync(CancellationToken ct = default) => Task.FromResult(_token);

    public Task SaveAsync(EpicOAuthToken token, CancellationToken ct = default)
    {
        _token = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _token = null;
        return Task.CompletedTask;
    }
}
