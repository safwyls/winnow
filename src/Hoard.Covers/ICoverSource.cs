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

    /// <summary>
    /// This source's contribution to the negative-marker identity
    /// (<see cref="CoverSourceSet"/>). Defaults to <see cref="Name"/>, which is
    /// right for any source whose ability to answer is fixed at compile time.
    ///
    /// <para>Override it when that ability depends on state outside the code —
    /// IGDB answers only once credentials exist — so that a <c>.none</c> written
    /// while the source could not answer carries a different identity from one
    /// written when it could, and is therefore retried the moment the state
    /// changes. Read on the <see cref="CoverPipeline.IsKnownMissing"/> path, so
    /// it must be cheap and must not block.</para>
    /// </summary>
    string SourceSetId => Name;

    /// <summary>
    /// Whether this source can answer for the key's provider/id shape. Shape
    /// only — never configuration or availability, which belong in
    /// <see cref="SourceSetId"/>: a source that hides itself here is never
    /// asked again and so can never notice that it became able to answer.
    /// </summary>
    bool CanHandle(CoverKey key);

    /// <summary>
    /// The encoded image bytes, or <see langword="null"/> when this source has
    /// no art for the key. A missing capsule is a normal outcome, not an error —
    /// only transport failures throw.
    /// </summary>
    Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default);
}
