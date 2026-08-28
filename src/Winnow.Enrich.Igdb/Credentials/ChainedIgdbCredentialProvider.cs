using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Igdb.Credentials;

/// <summary>
/// Consults each <see cref="IIgdbCredentialSource"/> in registration order and
/// returns the first complete pair. The registered order is:
///
/// <list type="number">
///   <item><description><see cref="SettingsTableCredentialSource"/> — the <c>settings</c> table, the product path.</description></item>
///   <item><description><see cref="ConfigurationCredentialSource"/> — <c>IConfiguration</c> (env vars, appsettings.local.json).</description></item>
/// </list>
///
/// <para>A source that holds nothing is skipped, not an error. All sources
/// empty means "not configured", which is returned as null.</para>
///
/// <para>The result is memoised: credential lookup happens on the hot path of
/// every enrichment call and the settings table would otherwise be read each
/// time. <see cref="Invalidate"/> after the user edits their keys.</para>
/// </summary>
public sealed class ChainedIgdbCredentialProvider : IIgdbCredentialProvider
{
    private readonly IIgdbCredentialSource[] _sources;
    private readonly ILogger<ChainedIgdbCredentialProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _resolved;
    private IgdbCredentials? _cached;

    public ChainedIgdbCredentialProvider(
        IEnumerable<IIgdbCredentialSource> sources, ILogger<ChainedIgdbCredentialProvider> log)
    {
        _sources = sources.ToArray();
        _log = log;
    }

    public async ValueTask<IgdbCredentials?> GetAsync(CancellationToken ct = default)
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
                var credentials = await source.TryGetAsync(ct);
                if (credentials is not null)
                {
                    // Source name only. Logging a client id or secret is a
                    // hard no (§4.2) and this is the one place tempted to.
                    _log.LogInformation("IGDB credentials resolved from {Source}.", source.Name);
                    _cached = credentials;
                    _resolved = true;
                    return _cached;
                }
            }

            _log.LogInformation(
                "IGDB credentials not configured ({SourceCount} sources checked); enrichment disabled.",
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
