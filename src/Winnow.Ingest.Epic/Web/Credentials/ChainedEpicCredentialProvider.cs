using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web.Credentials;

/// <summary>
/// Consults each <see cref="IEpicCredentialSource"/> in registration order and
/// returns the first complete pair found: the <c>settings</c> table first (the
/// product path), then <c>IConfiguration</c> (the developer path).
///
/// <para>A source holding nothing is skipped, not an error. All sources empty
/// means "not configured", which is returned as null and disables the module.</para>
///
/// <para>The result is memoised, because the credential lookup happens on every
/// sync and every token refresh and the settings table would otherwise be read
/// each time. Call <see cref="Invalidate"/> after the user edits the pair.</para>
///
/// <para>Deliberately mirrors <c>ChainedIgdbCredentialProvider</c>, down to the
/// half-configured rule: a client id with no secret falls through to the next
/// source rather than being returned as a credential, so a stray environment
/// variable cannot shadow a complete pair in the settings table.</para>
/// </summary>
public sealed class ChainedEpicCredentialProvider : IEpicCredentialProvider
{
    private readonly IEpicCredentialSource[] _sources;
    private readonly ILogger<ChainedEpicCredentialProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _resolved;
    private EpicClientCredentials? _cached;

    public ChainedEpicCredentialProvider(
        IEnumerable<IEpicCredentialSource> sources, ILogger<ChainedEpicCredentialProvider> log)
    {
        _sources = sources.ToArray();
        _log = log;
    }

    public async ValueTask<EpicClientCredentials?> GetAsync(CancellationToken ct = default)
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
                    // Source name only. This is the one place in the module
                    // holding both halves in a local, and therefore the one place
                    // tempted to print them.
                    _log.LogInformation("Epic OAuth client credentials resolved from {Source}.", source.Name);
                    _cached = credentials;
                    _resolved = true;
                    return _cached;
                }
            }

            // Debug, not Information: an unconfigured Epic API is the default
            // state of every install, and a line at Information would announce a
            // non-event on every sync.
            _log.LogDebug(
                "Epic OAuth client credentials not configured ({SourceCount} sources checked); "
                + "Epic API ownership is disabled and the local readers are unaffected.",
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
