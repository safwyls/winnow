using Winnow.Core.Repositories;
using Winnow.Ingest.Epic.Web.Auth;
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

    /// <summary>Legacy plaintext key, emptied after successful protection.</summary>
    public const string ClientSecretSetting = "epic.oauth.client_secret";

    public const string ProtectedClientSecretSetting = "epic.oauth.client_secret.v1";

    private readonly ISettingsRepository? _settings;
    private readonly IEpicSecretProtector _protector;

    public SettingsTableEpicCredentialSource(ISettingsRepository? settings, IEpicSecretProtector protector)
    {
        _settings = settings;
        _protector = protector;
    }

    public string Name => "settings";

    public async ValueTask<EpicClientCredentials?> TryGetAsync(CancellationToken ct = default)
        => _settings is null
            ? null
            : EpicClientCredentials.TryCreate(
                await _settings.GetAsync(ClientIdSetting, ct),
                await GetSecretAsync(ct),
                Name);

    private async Task<string?> GetSecretAsync(CancellationToken ct)
    {
        var stored = await _settings!.GetAsync(ProtectedClientSecretSetting, ct);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            var secret = _protector.Unprotect(stored);
            if (!string.IsNullOrWhiteSpace(secret))
            {
                await ClearLegacyAsync(ct);
            }

            return secret;
        }

        var legacy = await _settings.GetAsync(ClientSecretSetting, ct);
        if (string.IsNullOrWhiteSpace(legacy))
        {
            return null;
        }

        var protectedValue = _protector.Protect(legacy.Trim());
        if (protectedValue is null)
        {
            // Preserve a user-entered value when protection fails, but never use it.
            return null;
        }

        await _settings.SetAsync(ProtectedClientSecretSetting, protectedValue, ct);
        await ClearLegacyAsync(ct);
        return legacy.Trim();
    }

    private async Task ClearLegacyAsync(CancellationToken ct)
    {
        // A previous run may have stopped between writing the blob and clearing plaintext.
        if (!string.IsNullOrEmpty(await _settings!.GetAsync(ClientSecretSetting, ct)))
        {
            await _settings.SetAsync(ClientSecretSetting, string.Empty, ct);
        }
    }
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
            services.GetService(typeof(ISettingsRepository)) as ISettingsRepository,
            (IEpicSecretProtector)services.GetService(typeof(IEpicSecretProtector))!);

    public string Name => _inner.Name;

    public ValueTask<EpicClientCredentials?> TryGetAsync(CancellationToken ct = default)
        => _inner.TryGetAsync(ct);
}
