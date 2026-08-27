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

    /// <summary>
    /// The work's primary publisher, or null when unknown (migration 0005).
    ///
    /// <para>One name, not a list: §5.3's publisher signal compares a single
    /// normalised string, and the writer picks deterministically from IGDB's
    /// publisher list so that two library rows for the same game agree. Null is
    /// "unknown" and makes the signal not fire; it is never a mismatch.</para>
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Valve's own <c>common.type</c> for the Steam appid — <c>Game</c>,
    /// <c>Demo</c>, <c>Tool</c> — or null when nothing has read it (migration
    /// 0006).
    ///
    /// <para>Stored verbatim, including the service's casing, which is not
    /// stable: Bastion answers lower-case <c>game</c> and Monster Hunter Wilds
    /// answers <c>Game</c>. Compare case-insensitively, always.</para>
    ///
    /// <para>Null means "not known", never "not a demo". Several appids answer
    /// <c>_missing_token</c> with no <c>common</c> object at all, so the title
    /// gate in <see cref="Queries.DemoConsolidation"/> stays the fallback.</para>
    /// </summary>
    public string? SteamAppType { get; init; }

    /// <summary>
    /// Epic's own <c>categories[].path</c> list for the catalog item, comma-joined
    /// in the storefront's order, or null when nothing has read it (migration
    /// 0009) — e.g. <c>public,games,applications</c> for a game and
    /// <c>engines,engines/ue4</c> for an Unreal Engine build.
    ///
    /// <para>The Epic sibling of <see cref="SteamAppType"/>, and read through the
    /// same one rule both Epic callers share,
    /// <see cref="Queries.EpicGameFilter"/>. Stored verbatim so a future reader
    /// can tell a category this build never saw from one it normalised away.</para>
    ///
    /// <para>Null means "not known", never "not a game" — every Epic work named
    /// from <c>catcache.bin</c> before this column existed carries null, and an
    /// unknown row is always visible.</para>
    /// </summary>
    public string? EpicCategories { get; init; }
}
