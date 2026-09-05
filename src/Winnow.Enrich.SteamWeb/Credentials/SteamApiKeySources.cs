using Microsoft.Extensions.Configuration;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// The stored key — the product path. §4.2: keys are user-supplied and stored
/// locally, so the app's own settings screen writes there and this is the first
/// source consulted.
///
/// <para>Reads through <see cref="ISteamApiKeyStore"/>, the one owner of the key
/// at rest, so that what this source resolves is what the screen saved: a key
/// that was protected on write is decrypted on read, and the pre-protection
/// plaintext row is migrated and emptied on the first read here. The key stopped
/// being a plain preference when it stopped being storable in the clear.</para>
/// </summary>
public sealed class SettingsTableApiKeySource : ISteamApiKeySource
{
    /// <summary>
    /// Settings key that held the key in the clear before protected storage
    /// existed. Kept as a constant because <see cref="SettingsSteamApiKeyStore"/>
    /// migrates it and empties it; nothing writes a value here any more.
    /// </summary>
    public const string ApiKeySetting = "steam.api_key";

    /// <summary>
    /// The provenance string this source stamps on a key it resolves, exposed as
    /// a constant because a caller has to be able to tell an app-managed key from
    /// a developer one without matching a literal. A key from here is the one the
    /// Stores screen's field owns: it can be replaced and it can be cleared.
    /// </summary>
    public const string SourceName = "settings";

    private readonly ISteamApiKeyStore _store;

    public SettingsTableApiKeySource(ISteamApiKeyStore store) => _store = store;

    public string Name => SourceName;

    public async ValueTask<SteamApiKey?> TryGetAsync(CancellationToken ct = default)
        => SteamApiKey.TryCreate(await _store.GetAsync(ct), Name);
}

/// <summary>
/// <see cref="IConfiguration"/> — the developer path. Reads <c>Steam:ApiKey</c>,
/// which the standard providers populate from the environment variable
/// <c>Steam__ApiKey</c> and from an optional, git-ignored
/// <c>appsettings.local.json</c>.
///
/// <para>Tolerates a host with no configuration at all (<paramref name="configuration"/>
/// may be null), because "no key" must never be a startup failure.</para>
/// </summary>
public class ConfigurationApiKeySource : ISteamApiKeySource
{
    /// <summary>Configuration section the key lives under.</summary>
    public const string SectionName = "Steam";

    /// <summary>Key within <see cref="SectionName"/> (so: <c>Steam:ApiKey</c> / <c>Steam__ApiKey</c>).</summary>
    public const string ApiKeyName = "ApiKey";

    /// <summary>
    /// The provenance string this source stamps on a key it resolves. A key from
    /// here lives in the environment or in a file beside the executable, so the
    /// Stores screen's field cannot remove it and has to say so rather than
    /// offering a Clear that would appear not to work.
    /// </summary>
    public const string SourceName = "configuration";

    private readonly IConfiguration? _configuration;

    public ConfigurationApiKeySource(IConfiguration? configuration) => _configuration = configuration;

    public string Name => SourceName;

    public virtual ValueTask<SteamApiKey?> TryGetAsync(CancellationToken ct = default)
        => ValueTask.FromResult(
            _configuration is null
                ? null
                : SteamApiKey.TryCreate(_configuration.GetSection(SectionName)[ApiKeyName], Name));
}

/// <summary>
/// DI-constructible <see cref="ConfigurationApiKeySource"/>.
///
/// <para>Exists only so the registration can use
/// <c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;ISteamApiKeySource, …&gt;())</c>,
/// which needs a concrete implementation type to deduplicate on and therefore
/// cannot take a factory lambda. The lambda is what would otherwise be needed,
/// because <see cref="IConfiguration"/> is optional here and DI has no way to
/// inject an optional dependency.</para>
/// </summary>
internal sealed class DefaultConfigurationApiKeySource : ConfigurationApiKeySource
{
    public DefaultConfigurationApiKeySource(IServiceProvider services)
        : base(services.GetService(typeof(IConfiguration)) as IConfiguration)
    {
    }
}
