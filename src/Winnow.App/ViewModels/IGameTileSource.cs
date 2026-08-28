namespace Winnow.App.ViewModels;

/// <summary>
/// The library's own tiles, addressable by ownership.
///
/// <para><b>Why the Feed borrows tiles instead of building its own.</b> A feed
/// card has to show the same cover, at the same point on the dormancy ramp,
/// with the same unread badge, and offer the same honestly-named Play/Install
/// the library offers — and every one of those facts is assembled by
/// <see cref="LibraryViewModel"/>'s load: external ids per store, the cover key,
/// the Epic launch triple, the install state, the §7 bucket name, and the
/// commands wired to the launcher and the detail modal. Rebuilding that beside
/// it would be a second projection of the same game, and the failure mode is not
/// a crash — it is the feed and the library quietly disagreeing about whether a
/// game is installed.</para>
///
/// <para><b>One method is the entire dependency, so it is the entire seam</b> —
/// the same argument <see cref="IStoreTitleCounts"/> makes for the Stores panel.
/// A feed view model taking a whole <c>LibraryViewModel</c> would drag five
/// repositories into its constructor and make its tests need a migrated
/// database to assert on a sentence.</para>
/// </summary>
public interface IGameTileSource
{
    /// <summary>
    /// Raised when the library has finished a load and every tile object has
    /// been replaced.
    ///
    /// <para>It matters because a reload is not rare: enrichment renames works
    /// and lands covers behind a library the user is already browsing (§7), and
    /// the non-game preference reloads too. A feed holding the previous
    /// generation of tiles would keep showing the titles and the art they had
    /// before the pass that fixed them, and would only come right on the next
    /// launch — the quiet kind of staleness nobody files a bug for.</para>
    /// </summary>
    event EventHandler? TilesChanged;

    /// <summary>
    /// True once the library has loaded at least one tile. False is "not known
    /// yet", never "you own nothing" — a feed that read it the second way would
    /// tell the user their library was empty while it was still loading.
    /// </summary>
    bool HasTiles { get; }

    /// <summary>
    /// The tile for an ownership, or null when the library does not hold one.
    ///
    /// <para>Null is a real answer rather than an error: the feed scores with
    /// the §6.1 defaults, while the library may be showing a wider set (the
    /// non-game preference), so the two sets agree or the library's is larger —
    /// but a reload racing a feed can still land here, and a missing card is a
    /// better outcome than an invented one.</para>
    /// </summary>
    GameTileViewModel? TileForOwnership(long ownershipId);

    /// <summary>
    /// The tile for a RELEASE, or null when the library does not hold one.
    ///
    /// <para><b>Why the second key exists.</b> Everything the feed draws is
    /// keyed by ownership, but the one fact the feedback loop stores is keyed by
    /// release — a verdict is about the game, not about which of your copies of
    /// it the card happened to be (§6b widens a dismissal to the work for
    /// precisely that reason). So the inspection screen, which starts from
    /// stored rows rather than from cards, has nothing but a release id to put a
    /// title against.</para>
    ///
    /// <para>Null is a real answer here too, and a likelier one than above: a
    /// verdict outlives the library it was given in. A game consolidated away as
    /// a demo, or hidden by the non-game preference, still has its row, and the
    /// screen names it as one it can no longer find rather than dropping it —
    /// hiding a verdict the user gave would defeat the surface.</para>
    /// </summary>
    GameTileViewModel? TileForRelease(long releaseId);
}
