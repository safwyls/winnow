namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>One place a Steam Web API key might be stored.</summary>
public interface ISteamApiKeySource
{
    /// <summary>Name of this source, for diagnostics. Never a value.</summary>
    string Name { get; }

    /// <summary>The key held here, or null when this source holds none.</summary>
    ValueTask<SteamApiKey?> TryGetAsync(CancellationToken ct = default);
}

/// <summary>
/// Resolves the Steam Web API key, or reports that none is configured.
///
/// <para>Null is the ordinary, expected answer for a user who has not pasted a
/// key into settings. It is never an error: §5.1 says enrichment must not block
/// a user-facing path, and "the module declines" is how that is honoured.</para>
/// </summary>
public interface ISteamApiKeyProvider
{
    ValueTask<SteamApiKey?> GetAsync(CancellationToken ct = default);

    /// <summary>Drops the memoised result so the next call re-reads every source.</summary>
    void Invalidate();
}
