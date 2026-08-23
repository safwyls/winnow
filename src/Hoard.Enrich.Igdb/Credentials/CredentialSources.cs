using Hoard.Enrich.Igdb.Storage;
using Microsoft.Extensions.Configuration;

namespace Hoard.Enrich.Igdb.Credentials;

/// <summary>
/// The settings table — the product path. §4.2: keys are user-supplied and
/// stored locally, so the app's own settings screen writes here and this is the
/// first source consulted.
/// </summary>
public sealed class SettingsTableCredentialSource : IIgdbCredentialSource
{
    /// <summary>Settings key holding the Twitch application client id.</summary>
    public const string ClientIdKey = "igdb.client_id";

    /// <summary>Settings key holding the Twitch application client secret.</summary>
    public const string ClientSecretKey = "igdb.client_secret";

    private readonly ISettingsStore _settings;

    public SettingsTableCredentialSource(ISettingsStore settings) => _settings = settings;

    public string Name => "settings";

    public async ValueTask<IgdbCredentials?> TryGetAsync(CancellationToken ct = default)
    {
        var clientId = await _settings.GetAsync(ClientIdKey, ct);
        var clientSecret = await _settings.GetAsync(ClientSecretKey, ct);
        return IgdbCredentials.TryCreate(clientId, clientSecret, Name);
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
