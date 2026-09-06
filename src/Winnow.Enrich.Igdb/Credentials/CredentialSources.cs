using Winnow.Enrich.Igdb.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Enrich.Igdb.Credentials;

/// <summary>
/// The settings table — the product path. §4.2: keys are user-supplied and
/// stored locally, so this is the first source consulted.
///
/// <para>The client id is a public identifier and stays as it is. The client
/// secret is a long-lived bearer credential and is stored protected: this
/// source reads it through <see cref="IIgdbSecretProtector"/>, and a plaintext
/// row left by a pre-protection install is migrated on first read and left
/// empty. On a host that cannot encrypt, a plaintext secret is refused rather
/// than used — and never destroyed, because refusing is not license to delete
/// what a user typed.</para>
/// </summary>
public sealed class SettingsTableCredentialSource : IIgdbCredentialSource
{
    /// <summary>Settings key holding the Twitch application client id.</summary>
    public const string ClientIdKey = "igdb.client_id";

    /// <summary>
    /// Settings key that held the client secret in the clear before protected
    /// storage existed. Kept as a constant because this source migrates it and
    /// empties it; nothing writes a value here any more.
    /// </summary>
    public const string ClientSecretKey = "igdb.client_secret";

    /// <summary>
    /// Settings key holding the protected client secret. Versioned: a future
    /// change to the payload shape takes a new key rather than trying to
    /// interpret an old one.
    /// </summary>
    public const string ClientSecretProtectedKey = "igdb.client_secret.v1";

    private readonly ISettingsStore _settings;
    private readonly IIgdbSecretProtector _protector;
    private readonly ILogger<SettingsTableCredentialSource> _log;

    private bool _warnedAboutProtection;

    public SettingsTableCredentialSource(
        ISettingsStore settings,
        IIgdbSecretProtector protector,
        ILogger<SettingsTableCredentialSource>? log = null)
    {
        _settings = settings;
        _protector = protector;
        _log = log ?? NullLogger<SettingsTableCredentialSource>.Instance;
    }

    public string Name => "settings";

    public async ValueTask<IgdbCredentials?> TryGetAsync(CancellationToken ct = default)
    {
        var clientId = await _settings.GetAsync(ClientIdKey, ct);
        var clientSecret = await GetClientSecretAsync(ct);
        return IgdbCredentials.TryCreate(clientId, clientSecret, Name);
    }

    /// <summary>
    /// The client secret, protected at rest. One-time migration from the
    /// plaintext row an older build may have written: read it, store it
    /// protected, leave the plaintext row empty.
    /// </summary>
    private async Task<string?> GetClientSecretAsync(CancellationToken ct)
    {
        var stored = await _settings.GetAsync(ClientSecretProtectedKey, ct);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            var secret = _protector.Unprotect(stored);
            if (!string.IsNullOrWhiteSpace(secret))
            {
                // Retry cleanup if migration stopped after writing the protected row.
                if (!string.IsNullOrEmpty(await _settings.GetAsync(ClientSecretKey, ct)))
                {
                    await _settings.SetAsync(ClientSecretKey, string.Empty, ct);
                }

                return secret;
            }

            // The row exists but this host cannot read it. Not an error to the
            // user — the credentials are simply not in force here.
            return null;
        }

        var legacy = await _settings.GetAsync(ClientSecretKey, ct);
        if (string.IsNullOrWhiteSpace(legacy))
        {
            return null;
        }

        var protectedValue = _protector.Protect(legacy.Trim());
        if (protectedValue is null)
        {
            // A host that cannot encrypt refuses to use a plaintext credential
            // rather than letting the row keep paying out. What the user typed is
            // left exactly where it was; the environment variables are the
            // supported alternative on such a host.
            if (!_warnedAboutProtection)
            {
                _warnedAboutProtection = true;
                _log.LogWarning(
                    "An IGDB client secret is present in the clear from an earlier version, and this host "
                    + "cannot encrypt at rest ({Protector}), so it is not used. Supply the credentials through "
                    + "the Igdb__ClientId / Igdb__ClientSecret environment variables instead.",
                    _protector.Name);
            }

            return null;
        }

        await _settings.SetAsync(ClientSecretProtectedKey, protectedValue, ct);
        await _settings.SetAsync(ClientSecretKey, string.Empty, ct);
        _log.LogInformation(
            "The stored IGDB client secret was migrated to protected storage; the plaintext row was emptied.");
        return legacy.Trim();
    }
}

/// <summary>
/// <see cref="IConfiguration"/> — the developer path. Reads <c>Igdb:ClientId</c>
/// and <c>Igdb:ClientSecret</c>, which the standard providers populate from the
/// environment variables <c>Igdb__ClientId</c> / <c>Igdb__ClientSecret</c> and
/// from an optional, git-ignored <c>appsettings.local.json</c>.
///
/// <para>Tolerates a host with no configuration at all (<c>configuration</c> may
/// be null), because "no credentials" must never be a startup failure.</para>
/// </summary>
public class ConfigurationCredentialSource : IIgdbCredentialSource
{
    /// <summary>Configuration section these keys live under.</summary>
    public const string SectionName = "Igdb";

    private readonly IConfiguration? _configuration;

    public ConfigurationCredentialSource(IConfiguration? configuration) => _configuration = configuration;

    public string Name => "configuration";

    public virtual ValueTask<IgdbCredentials?> TryGetAsync(CancellationToken ct = default)
    {
        if (_configuration is null)
        {
            return ValueTask.FromResult<IgdbCredentials?>(null);
        }

        var section = _configuration.GetSection(SectionName);
        return ValueTask.FromResult(
            IgdbCredentials.TryCreate(section["ClientId"], section["ClientSecret"], Name));
    }
}

/// <summary>
/// DI-constructible <see cref="ConfigurationCredentialSource"/>.
///
/// <para>Exists only so the registration can use
/// <c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;IIgdbCredentialSource, …&gt;())</c>,
/// which needs a concrete implementation type to deduplicate on and therefore
/// cannot take a factory lambda. The lambda is what would otherwise be needed,
/// because <see cref="IConfiguration"/> is optional here and DI has no way to
/// inject an optional dependency.</para>
/// </summary>
internal sealed class DefaultConfigurationCredentialSource : ConfigurationCredentialSource
{
    public DefaultConfigurationCredentialSource(IServiceProvider services)
        : base(services.GetService(typeof(IConfiguration)) as IConfiguration)
    {
    }
}
