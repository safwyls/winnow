using Winnow.Core.Repositories;
using Microsoft.Extensions.Configuration;

namespace Winnow.Ingest.Epic.Web.Credentials;

/// <summary>
/// Reads Epic OAuth credentials from the <c>settings</c> table. First source
/// consulted. The repository is optional for module-boundary reasons.
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
/// Reads Epic OAuth credentials from <see cref="IConfiguration"/>
/// (<c>Epic:ClientId</c> / <c>Epic:ClientSecret</c>). Developer path.
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

/// <summary>DI-constructible wrapper for <see cref="ConfigurationEpicCredentialSource"/>.</summary>
internal sealed class DefaultConfigurationEpicCredentialSource : ConfigurationEpicCredentialSource
{
    public DefaultConfigurationEpicCredentialSource(IServiceProvider services)
        : base(services.GetService(typeof(IConfiguration)) as IConfiguration)
    {
    }
}

/// <summary>DI-constructible wrapper for <see cref="SettingsTableEpicCredentialSource"/>.</summary>
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
