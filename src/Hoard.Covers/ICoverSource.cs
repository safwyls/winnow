namespace Hoard.Covers;

/// <summary>
/// One place cover art can come from. Sources are tried in registration order
/// and only the first one that answers is used, so a later source (IGDB, via
/// its own client) fills the gaps Steam's capsule leaves without this project
/// knowing anything about it. Registering a second source is the whole
/// integration: <c>services.AddSingleton&lt;ICoverSource, MySource&gt;()</c>.
/// </summary>
public interface ICoverSource
{
    /// <summary>Diagnostic name.</summary>
    string Name { get; }

    /// <summary>Whether this source can answer for the key's provider/id shape.</summary>
    bool CanHandle(CoverKey key);

    /// <summary>
    /// The encoded image bytes, or <see langword="null"/> when this source has
    /// no art for the key. A missing capsule is a normal outcome, not an error —
    /// only transport failures throw.
    /// </summary>
    Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default);
}
