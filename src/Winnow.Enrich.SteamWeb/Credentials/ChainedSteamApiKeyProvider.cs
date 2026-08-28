using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// Consults each <see cref="ISteamApiKeySource"/> in registration order and
/// returns the first key found. The registered order is:
///
/// <list type="number">
///   <item><description><see cref="SettingsTableApiKeySource"/> — the <c>settings</c> table, the product path.</description></item>
///   <item><description><see cref="ConfigurationApiKeySource"/> — <c>IConfiguration</c> (<c>Steam__ApiKey</c>, appsettings.local.json).</description></item>
/// </list>
///
/// <para>A source that holds nothing is skipped, not an error. All sources empty
/// means "not configured", which is returned as null.</para>
///
/// <para>The result is memoised: key lookup happens on every enrichment call and
/// the settings table would otherwise be read each time.
/// <see cref="Invalidate"/> after the user edits their key.</para>
///
/// <para>Deliberately mirrors <c>ChainedIgdbCredentialProvider</c>. The one
/// difference is that a Steam key is a single value rather than a pair, so there
/// is no half-configured state to fall through on.</para>
/// </summary>
public sealed class ChainedSteamApiKeyProvider : ISteamApiKeyProvider
{
    private readonly ISteamApiKeySource[] _sources;
    private readonly ILogger<ChainedSteamApiKeyProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _resolved;
    private SteamApiKey? _cached;

    public ChainedSteamApiKeyProvider(
        IEnumerable<ISteamApiKeySource> sources, ILogger<ChainedSteamApiKeyProvider> log)
    {
        _sources = sources.ToArray();
        _log = log;
    }

    public async ValueTask<SteamApiKey?> GetAsync(CancellationToken ct = default)
    {
        if (_resolved)
        {
            return _cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_resolved)
            {
                return _cached;
            }

            foreach (var source in _sources)
            {
                var key = await source.TryGetAsync(ct);
                if (key is not null)
                {
                    // Source name only. Logging the key is a hard no (§4.2) and
                    // this is the one place tempted to.
                    _log.LogInformation("Steam Web API key resolved from {Source}.", source.Name);
                    _cached = key;
                    _resolved = true;
                    return _cached;
                }
            }

            _log.LogInformation(
                "Steam Web API key not configured ({SourceCount} sources checked); "
                + "Steam Web API enrichment disabled.",
                _sources.Length);
            _cached = null;
            _resolved = true;
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _resolved = false;
        _cached = null;
    }
}
