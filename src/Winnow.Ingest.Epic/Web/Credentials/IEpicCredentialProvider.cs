namespace Winnow.Ingest.Epic.Web.Credentials;

/// <summary>One place an Epic OAuth client pair might be stored.</summary>
public interface IEpicCredentialSource
{
    /// <summary>Name of this source, for diagnostics. Never a value.</summary>
    string Name { get; }

    /// <summary>The pair held here, or null when this source holds none.</summary>
    ValueTask<EpicClientCredentials?> TryGetAsync(CancellationToken ct = default);
}

/// <summary>
/// Resolves the Epic OAuth client pair, or reports that none is configured.
///
/// <para>Null is the ordinary, expected answer — it is the state of every user
/// who has not deliberately opted in. It is never an error: §5.1 forbids
/// enrichment blocking a user-facing path, and "the module declines" is how that
/// is honoured. Mirrors <c>IIgdbCredentialProvider</c> and
/// <c>ISteamApiKeyProvider</c> so all three credential paths behave the same
/// way.</para>
/// </summary>
public interface IEpicCredentialProvider
{
    ValueTask<EpicClientCredentials?> GetAsync(CancellationToken ct = default);

    /// <summary>Drops the memoised result so the next call re-reads every source.</summary>
    void Invalidate();
}
