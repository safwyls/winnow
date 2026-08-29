namespace Winnow.App.ViewModels;

/// <summary>
/// Narrow seam over <see cref="LibraryViewModel"/>'s loaded tiles, so the feed
/// and other panels can borrow tile instances without depending on the full VM.
/// </summary>
public interface IGameTileSource
{
    /// <summary>Raised after a library reload replaces all tile instances.</summary>
    event EventHandler? TilesChanged;

    /// <summary>True once the library has loaded at least one tile; false means not yet loaded.</summary>
    bool HasTiles { get; }

    /// <summary>The tile for an ownership, or null when the library does not hold one.</summary>
    GameTileViewModel? TileForOwnership(long ownershipId);

    /// <summary>
    /// The tile for a release, or null. Needed because feedback verdicts (§6b) are
    /// keyed by release, not ownership.
    /// </summary>
    GameTileViewModel? TileForRelease(long releaseId);
}
