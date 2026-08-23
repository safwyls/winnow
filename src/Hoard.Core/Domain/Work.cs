namespace Hoard.Core.Domain;

/// <summary>
/// Canonical identity layer 1 of 4: the game as a concept ("Skyrim").
/// Never collapse <see cref="Release"/> into this — Skyrim SE is not Skyrim.
/// </summary>
public sealed record Work
{
    public long Id { get; init; }
    public long? IgdbId { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// True when <see cref="Name"/> is a machine-minted placeholder (e.g.
    /// "App 1203620") rather than a real title — the case for a Steam appid
    /// known only from localconfig playtime, with no installed appmanifest to
    /// name it. Enrichment (and a later sync that carries a real title) clears
    /// this. A real title is never demoted back to provisional.
    /// </summary>
    public bool NameIsProvisional { get; init; }
    public string? SortName { get; init; }
    public int? FirstReleaseYear { get; init; }
    public string? Summary { get; init; }
    public string? CoverUrl { get; init; }
}
