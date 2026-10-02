namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>What a save attempt did. The panel turns each value into a sentence.</summary>
public enum SteamApiKeySaveOutcome
{
    /// <summary>The key was protected and written; the old value is gone.</summary>
    Stored,

    /// <summary>
    /// Nothing was written. The host cannot encrypt at rest (or the container has
    /// no store), and §4.7's rule is to refuse rather than degrade to a plaintext
    /// row. The key still works in memory for whoever supplied it in another way.
    /// </summary>
    Refused,
}
