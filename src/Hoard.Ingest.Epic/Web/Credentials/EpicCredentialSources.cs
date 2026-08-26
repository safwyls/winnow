using Hoard.Core.Repositories;
using Microsoft.Extensions.Configuration;

namespace Hoard.Ingest.Epic.Web.Credentials;

/// <summary>
/// The <c>settings</c> table — the product path. The pair is user-supplied and
/// stored locally, so the app's settings screen writes here and this is the
/// first source consulted.
///
/// <para>Reads through <see cref="ISettingsRepository"/>, the shared §6
/// key/value contract, rather than a private store of its own.</para>
///
/// <para><b>The repository is optional, and that is a module-boundary
/// decision.</b> <c>Hoard.Ingest.Epic</c> deliberately does not reference
/// <c>Hoard.Data</c> — the local readers need nothing but the filesystem, and a
/// project reference added for one settings lookup would drag the whole data
/// layer into a module whose §5.1 job is to read a source and emit candidates.
/// So this source takes the <i>interface</i> from <c>Hoard.Core</c> and accepts
/// null for it: a host that has no settings table simply has no credentials
/// here, which is the same well-trodden "not configured" path as a host that has
/// one and never wrote to it.</para>
/// </summary>
public sealed class SettingsTableEpicCredentialSource : IEpicCredentialSource
{
    /// <summary>Settings key holding the user's Epic OAuth client id. Namespaced per the §6 convention.</summary>
    public const string ClientIdSetting = "epic.oauth.client_id";

    /// <summary>Settings key holding the user's Epic OAuth client secret.</summary>
    public const string ClientSecretSetting = "epic.oauth.client_secret";

    private readonly ISettingsRepository? _settings;

    public SettingsTableEpicCredentialSource(ISettingsRepository? settings) => _settings = settings;

    public string Name => "settings";

    public async ValueTask<EpicClientCredentials?> TryGetAsync(CancellationToken ct = default)
        => _settings is null
            ? null
            : EpicClientCredentials.TryCreate(
                await _settings.GetAsync(ClientIdSetting, ct),
                await _settings.GetAsync(ClientSecretSetting, ct),
                Name);
}

/// <summary>
/// <see cref="IConfiguration"/> — the developer path. Reads
/// <c>Epic:ClientId</c> / <c>Epic:ClientSecret</c>, which the standard providers
/// populate from the environment variables <c>Epic__ClientId</c> /
/// <c>Epic__ClientSecret</c> and from an optional, git-ignored
/// <c>appsettings.local.json</c>.
///
/// <para>Tolerates a host with no configuration at all, because "no credentials"
/// must never be a startup failure.</para>
/// </summary>
public class ConfigurationEpicCredentialSource : IEpicCredentialSource
{
    /// <summary>Configuration section the pair lives under.</summary>
    public const string SectionName = "Epic";

    /// <summary>Key within <see cref="SectionName"/> (so: <c>Epic:ClientId</c> / <c>Epic__ClientId</c>).</summary>
    public const string ClientIdName = "ClientId";

    /// <summary>Key within <see cref="SectionName"/> (so: <c>Epic:ClientSecret</c> / <c>Epic__ClientSecret</c>).</summary>
    public const string ClientSecretName = "ClientSecret";

    private readonly IConfiguration? _configuration;

    public ConfigurationEpicCredentialSource(IConfiguration? configuration) => _configuration = configuration;

    public string Name => "configuration";

    public virtual ValueTask<EpicClientCredentials?> TryGetAsync(CancellationToken ct = default)
    {
        if (_configuration is null)
        {
            return ValueTask.FromResult<EpicClientCredentials?>(null);
        }

        var section = _configuration.GetSection(SectionName);
        return ValueTask.FromResult(
            EpicClientCredentials.TryCreate(section[ClientIdName], section[ClientSecretName], Name));
    }
}

/// <summary>
/// DI-constructible <see cref="ConfigurationEpicCredentialSource"/>.
///
/// <para>Exists only so the registration can use
/// <c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;IEpicCredentialSource, …&gt;())</c>,
/// which needs a concrete implementation type to deduplicate on and therefore
/// cannot take a factory lambda — and a lambda is what would otherwise be needed,
/// because <see cref="IConfiguration"/> is optional here and DI has no way to
/// inject an optional dependency. Same shape as
/// <c>DefaultConfigurationApiKeySource</c> in the Steam Web module.</para>
/// </summary>
internal sealed class DefaultConfigurationEpicCredentialSource : ConfigurationEpicCredentialSource
{
    public DefaultConfigurationEpicCredentialSource(IServiceProvider services)
        : base(services.GetService(typeof(IConfiguration)) as IConfiguration)
    {
    }
}

/// <summary>
/// DI-constructible <see cref="SettingsTableEpicCredentialSource"/>, for the
/// same <c>TryAddEnumerable</c> reason as
/// <see cref="DefaultConfigurationEpicCredentialSource"/> — and additionally
/// because <see cref="ISettingsRepository"/> is optional here (see that class's
/// remarks) and DI cannot inject an optional dependency.
/// </summary>
internal sealed class DefaultSettingsTableEpicCredentialSource : IEpicCredentialSource
{
    private readonly SettingsTableEpicCredentialSource _inner;

    public DefaultSettingsTableEpicCredentialSource(IServiceProvider services)
        => _inner = new SettingsTableEpicCredentialSource(
            services.GetService(typeof(ISettingsRepository)) as ISettingsRepository);

    public string Name => _inner.Name;

    public ValueTask<EpicClientCredentials?> TryGetAsync(CancellationToken ct = default)
        => _inner.TryGetAsync(ct);
}
