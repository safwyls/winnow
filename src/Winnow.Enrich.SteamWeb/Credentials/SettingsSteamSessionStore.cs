using System.Text.Json;
using System.Text.Json.Serialization;
using Winnow.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Persists the Steam session in the settings table as a single DPAPI-encrypted
/// blob. Refuses to store unencrypted; no-op when the repository is absent.
///
/// <para>One blob, not eleven settings rows. The alternative would leave the
/// account id and both expiry timestamps sitting in the clear next to an
/// encrypted token, and a partial write would produce a session whose halves
/// disagreed. Encrypting the whole record makes the unit of storage the same as
/// the unit of meaning.</para>
/// </summary>
public sealed class SettingsSteamSessionStore : ISteamSessionStore
{
    /// <summary>
    /// Settings key holding the encrypted session. Namespaced per the §6
    /// convention, and versioned exactly as
    /// <c>epic.oauth.session.v1</c> is: a future change to the payload shape
    /// takes a new key rather than trying to interpret an old one, so a
    /// downgrade cannot read a blob it does not understand.
    /// </summary>
    public const string SessionSetting = "steam.session.v1";

    /// <summary>
    /// Nulls are written, not skipped. The persisted shape is a closed list of
    /// eleven fields that section 4.7's second amendment makes auditable, and an
    /// audit is easier against a record whose key set does not depend on whether
    /// a renewal has happened yet.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsRepository? _settings;
    private readonly ISteamSecretProtector _protector;
    private readonly ILogger<SettingsSteamSessionStore> _log;

    private bool _warnedAboutProtection;

    public SettingsSteamSessionStore(
        ISettingsRepository? settings,
        ISteamSecretProtector protector,
        ILogger<SettingsSteamSessionStore>? log = null)
    {
        _settings = settings;
        _protector = protector;
        _log = log ?? NullLogger<SettingsSteamSessionStore>.Instance;
    }

    public bool CanPersist => _settings is not null && _protector.IsAvailable;

    public async Task<SteamSession?> LoadAsync(CancellationToken ct = default)
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
            return payload?.ToSession();
        }
        catch (JsonException)
        {
            // Decrypted successfully but did not parse: a shape change, or a
            // truncated write. Same remedy as an unreadable blob: no session.
            // The exception object is never logged; its message quotes the JSON,
            // and the JSON is the session.
            _log.LogWarning("The stored Steam session could not be parsed; a fresh sign-in is required.");
            return null;
        }
    }

    public async Task SaveAsync(SteamSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (_settings is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(StoredSession.From(session), SerializerOptions);
        var protectedValue = _protector.Protect(json);
        if (protectedValue is null)
        {
            if (!_warnedAboutProtection)
            {
                _warnedAboutProtection = true;
                _log.LogWarning(
                    "The Steam session cannot be encrypted at rest on this host ({Protector}), so it will not "
                    + "be stored. Sign-in works for this run and has to be repeated after a restart. Storing it "
                    + "unencrypted is deliberately not offered: the refresh token re-mints access to the Steam "
                    + "account for as long as it lives.",
                    _protector.Name);
            }

            return;
        }

        await _settings.SetAsync(SessionSetting, protectedValue, ct);
        _log.LogDebug("Steam session stored, encrypted with {Protector}.", _protector.Name);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (_settings is null)
        {
            return;
        }

        // Empty rather than a delete: ISettingsRepository is a two-method
        // key/value contract with no remove, and empty is unambiguously "unset"
        // to every reader here; LoadAsync treats blank as no session.
        await _settings.SetAsync(SessionSetting, string.Empty, ct);
        _log.LogInformation("Stored Steam session cleared.");
    }

    /// <summary>
    /// The persisted shape, and the whole of it.
    ///
    /// <para>These eleven fields are the closed list section 4.7's second
    /// amendment permits at rest. There is no cookie, no
    /// <c>steamLoginSecure</c>, no <c>sessionid</c>, no browser profile, no page
    /// content and no API key here, and a test asserts on the serialized key set
    /// so that adding one is a test failure rather than a review question.</para>
    ///
    /// <para>Property names are explicit so a rename in
    /// <see cref="SteamSession"/> cannot silently orphan every stored
    /// session.</para>
    /// </summary>
    private sealed record StoredSession
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; init; }

        [JsonPropertyName("audience")]
        public IReadOnlyList<string> Audience { get; init; } = [];

        [JsonPropertyName("issuer")]
        public string? Issuer { get; init; }

        // The SteamID64 as a string. Stored as text because it is an identifier
        // rather than a quantity, and because a 17-digit value round-trips
        // through every JSON reader as text and through only some as a number.
        [JsonPropertyName("steamid64")]
        public string SteamId64 { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;

        // Nullable, and stored as null when the refresh token did not decode.
        // Writing a sentinel, or the measured 207 days, would turn "not known"
        // into a date on the way back in.
        [JsonPropertyName("refresh_expires_at")]
        public DateTimeOffset? RefreshExpiresAt { get; init; }

        [JsonPropertyName("minted_at")]
        public DateTimeOffset MintedAt { get; init; }

        [JsonPropertyName("last_renewed_at")]
        public DateTimeOffset? LastRenewedAt { get; init; }

        [JsonPropertyName("renewal_failures")]
        public int RenewalFailures { get; init; }

        [JsonPropertyName("last_failure_kind")]
        public SteamSessionRenewalFailure LastFailureKind { get; init; }

        public static StoredSession From(SteamSession session) => new()
        {
            AccessToken = session.AccessToken,
            ExpiresAt = session.ExpiresAt,
            Audience = session.Audience,
            Issuer = session.Issuer,
            SteamId64 = session.SteamId.ToString(),
            RefreshToken = session.RefreshToken,
            RefreshExpiresAt = session.RefreshExpiresAt,
            MintedAt = session.MintedAt,
            LastRenewedAt = session.LastRenewedAt,
            RenewalFailures = session.RenewalFailures,
            LastFailureKind = session.LastFailureKind,
        };

        /// <summary>
        /// Null when a required field is missing or the account id does not
        /// parse. A session missing either token, or naming no account, cannot
        /// be used for anything; returning a half-built one would only move the
        /// failure to the first request.
        /// </summary>
        public SteamSession? ToSession()
            => string.IsNullOrWhiteSpace(AccessToken)
                || string.IsNullOrWhiteSpace(RefreshToken)
                || !SteamId.TryParse(SteamId64, out var steamId)
                    ? null
                    : new SteamSession(
                        AccessToken,
                        ExpiresAt,
                        Audience ?? [],
                        Issuer,
                        steamId,
                        RefreshToken,
                        RefreshExpiresAt,
                        MintedAt,
                        LastRenewedAt,
                        RenewalFailures,
                        LastFailureKind);
    }
}

/// <summary>
/// Non-persistent <see cref="ISteamSessionStore"/>, for tests and for hosts
/// running without a database. The session lives as long as the process, which
/// is also what a Windows-less host gets from the real store.
/// </summary>
public sealed class InMemorySteamSessionStore : ISteamSessionStore
{
    private SteamSession? _session;

    public bool CanPersist => false;

    public Task<SteamSession?> LoadAsync(CancellationToken ct = default) => Task.FromResult(_session);

    public Task SaveAsync(SteamSession session, CancellationToken ct = default)
    {
        _session = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _session = null;
        return Task.CompletedTask;
    }
}
