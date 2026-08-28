namespace Winnow.Core.Queries;

/// <summary>
/// Derived bucket names (§6.1). Buckets are QUERIES, never stored columns:
/// thresholds get tuned, and stored values rot.
/// </summary>
public static class LibraryBuckets
{
    /// <summary>
    /// Playtime below the refund line (<see cref="BucketThresholds.BouncedFloorMinutes"/>),
    /// zero included.
    ///
    /// <para>Not "zero minutes": under Steam's two-hour refund window the game
    /// could still have been handed back, so nothing was really committed to.
    /// Ninety minutes and no minutes are the same fact about the user — they
    /// never played it — and splitting them put the larger half of a bundle
    /// library in a bucket nobody looks at.</para>
    /// </summary>
    public const string NeverPlayed = "never_played";

    /// <summary>
    /// Refund line (inclusive) up to the retired floor — the highest-value pile.
    /// Past the point of no return and abandoned anyway.
    /// </summary>
    public const string Bounced = "bounced";

    /// <summary>
    /// Opened at all, then a release update landed more than the stale window
    /// after last play.
    ///
    /// <para>Outranks <see cref="NeverPlayed"/> and <see cref="Bounced"/> — the
    /// badge is this bucket's membership (design-system §5.2) and forty minutes
    /// of play can absolutely be forty minutes behind a patch. Only a game with
    /// no minutes AND no last-played date has nothing to be behind on, and only
    /// that case is tested ahead of this one. <see cref="Retired"/> still
    /// outranks it: high-playtime games are excluded from surfacing.</para>
    /// </summary>
    public const string StaleButPatched = "stale_but_patched";

    /// <summary>High playtime; excluded from surfacing.</summary>
    public const string Retired = "retired";

    /// <summary>
    /// The residue, and not a rail bucket: with
    /// <see cref="NeverPlayed"/>, <see cref="Bounced"/> and <see cref="Retired"/>
    /// now tiling the whole playtime axis, the only rows that reach here are the
    /// ones with no usable playtime number at all — a real last-played date
    /// beside zero recorded minutes, which is a source admitting it did not
    /// measure the session.
    /// </summary>
    public const string Active = "active";
}

/// <summary>
/// Tunable thresholds for the derived-bucket query. Deliberately parameters,
/// not schema: §6.1 requires retuning without migration.
/// </summary>
/// <param name="BouncedFloorMinutes">
/// The boundary between Never played and Bounced off, not a ceiling of either:
/// playtime strictly below it is <see cref="LibraryBuckets.NeverPlayed"/>,
/// playtime at or above it is <see cref="LibraryBuckets.Bounced"/> until
/// <paramref name="RetiredFloorMinutes"/>, which is Bounced's real ceiling.
/// <para>Default 120 — Steam's refund line, and the only non-arbitrary number
/// available. Below it the purchase was still reversible, so "I never played
/// it" is the literal truth; at or above it the user committed and gave up
/// anyway, which is a different and far more interesting fact.</para>
/// </param>
/// <param name="RetiredFloorMinutes">
/// Playtime at or above this is Retired — and therefore also the ceiling of
/// Bounced off.
/// </param>
/// <param name="StaleWindowMonths">An update more than this many months after last play marks Stale-but-patched.</param>
/// <param name="UpdateCorrelationWindowDays">
/// How far apart a build push and an announcement may be and still count as one
/// "major update" (§4.5). Neither signal means anything alone: a depot push
/// fires on DRM bumps, localization files and one-line hotfixes, and
/// announcements are pure marketing half the time. Only the pair counts.
/// <para>Default 7 days. Studios do not ship the build and the patch notes
/// simultaneously — the announcement commonly lands a day or two either side of
/// the push (a teaser before, a write-up after), and content patches often
/// trickle out as several depot pushes across a release week. A week absorbs
/// that without reaching far enough to pair a patch with the *next* month's
/// unrelated announcement. Tunable, like every other threshold here: both raw
/// signals are stored, so retuning never re-fetches (§4.5).</para>
/// </param>
/// <param name="ShowNonGameEntries">
/// Whether the library view includes the entries Valve typed as something other
/// than a game — tools, applications, soundtracks, videos, hardware
/// (<see cref="NonGameEntries"/>).
///
/// <para><b>An option rather than a threshold</b>, and the only one here. It
/// rides on this record because this record is already the bucket query's
/// parameter object, and because the filter has to be applied inside that query
/// for the same reason the buckets are: the rail's counts and the grid's tiles
/// are both computed from the rows it returns, so filtering anywhere else would
/// let the two disagree.</para>
///
/// <para><b>Default false — hidden.</b> Steam carries non-game items, but this
/// application is about games; a user who wants their dedicated servers back
/// says so. Nothing is destroyed by the default: the filter drops rows from one
/// read, so flipping this changes the very next result with no re-sync, no
/// write and no delete.</para>
///
/// <para>Persisted by the UI under <see cref="ShowNonGameEntriesSettingKey"/>;
/// see <see cref="ParseShowNonGameEntries"/> for the one authoritative reading
/// of the stored text.</para>
/// </param>
public sealed record BucketThresholds(
    long BouncedFloorMinutes,
    long RetiredFloorMinutes,
    int StaleWindowMonths,
    int UpdateCorrelationWindowDays = 7,
    bool ShowNonGameEntries = false)
{
    /// <summary>
    /// The <c>settings</c> key the "show non-game entries" preference persists
    /// under, following the <c>module.thing</c> namespacing
    /// <see cref="Core.Repositories.ISettingsRepository"/> requires and the
    /// <c>display.dim_dormant_covers</c> toggle established.
    ///
    /// <para><c>library.</c> rather than <c>display.</c> because this changes
    /// what the library CONTAINS — the counts move with it — where the dimming
    /// toggle only changes how the same set of tiles is painted.</para>
    /// </summary>
    public const string ShowNonGameEntriesSettingKey = "library.show_non_game_entries";

    /// <summary>Conservative defaults; per-genre configuration comes later (§6.1).</summary>
    public static BucketThresholds Default { get; } = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 6_000,
        StaleWindowMonths: 6,
        UpdateCorrelationWindowDays: 7,
        ShowNonGameEntries: false);

    /// <summary>
    /// Reads the stored preference text. Anything that is not the literal
    /// <c>true</c> (case- and whitespace-insensitive) means hidden, which is
    /// both the default and the safe reading of a value that no longer parses.
    ///
    /// <para>Lives here so the default exists in exactly one place: the store
    /// itself returns whatever was written and takes no position on bad text
    /// (<see cref="Core.Repositories.ISettingsRepository"/>), so this takes
    /// one.</para>
    /// </summary>
    public static bool ParseShowNonGameEntries(string? stored)
        => bool.TryParse(stored?.Trim(), out var show) && show;

    /// <summary>
    /// The text to write back under <see cref="ShowNonGameEntriesSettingKey"/>,
    /// paired with <see cref="ParseShowNonGameEntries"/> so a round trip is
    /// guaranteed to survive.
    /// </summary>
    public static string FormatShowNonGameEntries(bool show) => show ? "true" : "false";
}

/// <summary>One row of the derived-bucket query: the bucket for a single ownership.</summary>
public sealed record OwnershipBucket
{
    public required long OwnershipId { get; init; }
    public required long ReleaseId { get; init; }
    public required long PlaytimeMinutes { get; init; }
    public DateTime? LastPlayedAt { get; init; }
    public required string Bucket { get; init; }

    /// <summary>
    /// How many owned demo releases this row supersedes
    /// (<see cref="DemoConsolidation"/>) — 0 for almost every row.
    ///
    /// <para>The demos themselves are absent from the result: owning both
    /// <c>Bastion</c> and <c>Bastion Demo</c> yields one row, this one, and the
    /// demo's tile disappears. A solitary demo is a normal row with a normal
    /// count of 0.</para>
    ///
    /// <para><b>A count, deliberately, and never a total.</b> The demo's
    /// minutes belong to the demo's own ownership and are still stored,
    /// unchanged and queryable, there. Adding them to
    /// <see cref="PlaytimeMinutes"/> would be §6.2's forbidden blend — two
    /// appids, two achievement sets, two facts — so this row reports only that
    /// something was folded in, never a merged number.</para>
    /// </summary>
    public int ConsolidatedDemoCount { get; init; }
}
