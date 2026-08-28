namespace Winnow.Enrich.Igdb.Credentials;

/// <summary>
/// One place credentials might live. Sources are consulted in registration
/// order by <see cref="ChainedIgdbCredentialProvider"/>.
/// </summary>
public interface IIgdbCredentialSource
{
    /// <summary>Short identifier used in log lines. Never the credential itself.</summary>
    string Name { get; }

    /// <summary>The credentials this source holds, or null when it holds none.</summary>
    ValueTask<IgdbCredentials?> TryGetAsync(CancellationToken ct = default);
}

/// <summary>
/// Resolves the IGDB credentials, or reports that there are none.
///
/// <para>Returning null is the supported "not configured" state: Winnow runs
/// perfectly well with no IGDB account, it simply does not enrich. Callers must
/// treat null as a no-op, never as an error.</para>
/// </summary>
public interface IIgdbCredentialProvider
{
    ValueTask<IgdbCredentials?> GetAsync(CancellationToken ct = default);

    /// <summary>Drops any memoised lookup so the next call re-reads its sources.</summary>
    void Invalidate();
}
