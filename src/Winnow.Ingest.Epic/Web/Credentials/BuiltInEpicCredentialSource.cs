namespace Winnow.Ingest.Epic.Web.Credentials;

/// <summary>
/// Epic's launcher OAuth client, embedded in Winnow. Last source consulted so
/// user-supplied credentials take precedence. See <c>docs/spikes/epic-oauth.md</c>
/// and <c>ROADMAP.md</c> §3 for the trade-offs.
/// </summary>
public sealed class BuiltInEpicCredentialSource : IEpicCredentialSource
{
    /// <summary>
    /// Epic's launcher client id (<c>launcherAppClient2</c>). Not a secret in any
    /// meaningful sense — it identifies which client is being impersonated, and
    /// it is in every one of the tools named above.
    /// </summary>
    private const string LauncherClientId = "34a02cf8f4414e29b15921876da36f9a";

    /// <summary>Epic's launcher client secret. Publicly circulated since 2020; unrotated as of 2026-08-26.</summary>
    private const string LauncherClientSecret = "daafbccc737745039dffe53d94fc76cf";

    /// <inheritdoc/>
    public string Name => "built-in";

    /// <inheritdoc/>
    public ValueTask<EpicClientCredentials?> TryGetAsync(CancellationToken ct = default)
        => ValueTask.FromResult(EpicClientCredentials.TryCreate(LauncherClientId, LauncherClientSecret, Name));
}
