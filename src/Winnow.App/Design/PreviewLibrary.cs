using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.Design;

/// <summary>
/// The fabricated library the Avalonia previewer draws (TASK-150). Eight games
/// chosen so every rail bucket, the multi-store chip treatment, the unread
/// badge and the GOG patch-notes expander all have something to show.
///
/// <para>Nothing here touches a database, the filesystem or the network: the
/// records are the same domain types the SQLite read model returns, built in
/// memory, and the dates are relative to now so the intended buckets survive
/// the passage of time (a fixed 2024 date would silently re-bucket every game
/// as the years pass).</para>
///
/// <para>The bucket rows are folded through <see cref="GameGrouping.Of"/>, the
/// same function the real bucket query uses, so the preview can never show a
/// tile in a bucket its own playtime does not put it in.</para>
/// </summary>
internal static class PreviewLibrary
{
    // Work ids.
    public const long HollowKnightWork = 1;
    public const long DiscoElysiumWork = 2;
    public const long WitcherWork = 3;

    /// <summary>The GOG edition's own unresolved work, linked under <see cref="WitcherWork"/> in the read model.</summary>
    public const long WitcherGogWork = 30;
    public const long StardewWork = 4;
    public const long CelesteWork = 5;
    public const long BaldursGateWork = 6;
    public const long SlayTheSpireWork = 7;
    public const long Portal2Work = 8;

    // Release ids.
    public const long HollowKnightRelease = 101;
    public const long DiscoElysiumRelease = 102;
    public const long WitcherSteamRelease = 103;
    public const long WitcherGogRelease = 130;
    public const long StardewRelease = 104;
    public const long CelesteRelease = 105;
    public const long BaldursGateRelease = 106;
    public const long SlayTheSpireRelease = 107;
    public const long Portal2Release = 108;

    // Ownership ids.
    public const long HollowKnightOwnership = 201;
    public const long DiscoElysiumOwnership = 202;
    public const long WitcherSteamOwnership = 203;
    public const long WitcherGogOwnership = 230;
    public const long StardewOwnership = 204;
    public const long CelesteOwnership = 205;
    public const long BaldursGateOwnership = 206;
    public const long SlayTheSpireOwnership = 207;
    public const long Portal2Ownership = 208;

    public static IReadOnlyList<Work> Works { get; } =
    [
        new Work
        {
            Id = HollowKnightWork,
            Name = "Hollow Knight",
            FirstReleaseYear = 2017,
            Publisher = "Team Cherry",
            Summary = "A hand-drawn action adventure through a vast ruined kingdom of insects beneath a fading town.",
        },
        new Work
        {
            Id = DiscoElysiumWork,
            Name = "Disco Elysium",
            FirstReleaseYear = 2019,
            Publisher = "ZA/UM",
            Summary = "A role-playing game about a detective with a unique skill system at his disposal.",
        },
        new Work
        {
            Id = WitcherWork,
            Name = "The Witcher 3: Wild Hunt",
            FirstReleaseYear = 2015,
            Publisher = "CD Projekt",
            Summary = "A story-driven open world set in a visually stunning fantasy universe.",
        },
        new Work
        {
            Id = WitcherGogWork,
            Name = "The Witcher 3: Wild Hunt",
            FirstReleaseYear = 2015,
            Publisher = "CD Projekt",
        },
        new Work
        {
            Id = StardewWork,
            Name = "Stardew Valley",
            FirstReleaseYear = 2016,
            Publisher = "ConcernedApe",
            Summary = "An open-ended country-life RPG. Restore your grandfather's farm, grow crops, raise animals and befriend the valley.",
        },
        new Work
        {
            Id = CelesteWork,
            Name = "Celeste",
            FirstReleaseYear = 2018,
            Publisher = "Maddy Makes Games",
            Summary = "Help Madeline survive her inner demons on her journey to the top of Celeste Mountain.",
        },
        new Work
        {
            Id = BaldursGateWork,
            Name = "Baldur's Gate 3",
            FirstReleaseYear = 2023,
            Publisher = "Larian Studios",
            Summary = "A party-based RPG set in the Dungeons & Dragons universe.",
        },
        new Work
        {
            Id = SlayTheSpireWork,
            Name = "Slay the Spire",
            FirstReleaseYear = 2019,
            Publisher = "Mega Crit",
            Summary = "A deck-building roguelike. Craft a unique deck, encounter bizarre creatures, discover relics.",
        },
        new Work
        {
            Id = Portal2Work,
            Name = "Portal 2",
            FirstReleaseYear = 2011,
            Publisher = "Valve",
            Summary = "The sequel to the acclaimed puzzle game, with an extended single-player story and a co-operative campaign.",
        },
    ];

    public static IReadOnlyList<Release> Releases { get; } =
    [
        new Release { Id = HollowKnightRelease, WorkId = HollowKnightWork, Name = "Hollow Knight", Platform = "windows" },
        new Release { Id = DiscoElysiumRelease, WorkId = DiscoElysiumWork, Name = "Disco Elysium", Platform = "windows" },
        new Release { Id = WitcherSteamRelease, WorkId = WitcherWork, Name = "The Witcher 3: Wild Hunt", Platform = "windows" },
        new Release { Id = WitcherGogRelease, WorkId = WitcherGogWork, Name = "The Witcher 3: Wild Hunt", Platform = "windows" },
        new Release { Id = StardewRelease, WorkId = StardewWork, Name = "Stardew Valley", Platform = "windows" },
        new Release { Id = CelesteRelease, WorkId = CelesteWork, Name = "Celeste", Platform = "windows" },
        new Release { Id = BaldursGateRelease, WorkId = BaldursGateWork, Name = "Baldur's Gate 3", Platform = "windows" },
        new Release { Id = SlayTheSpireRelease, WorkId = SlayTheSpireWork, Name = "Slay the Spire", Platform = "windows" },
        new Release { Id = Portal2Release, WorkId = Portal2Work, Name = "Portal 2", Platform = "windows" },
    ];

    public static IReadOnlyList<Ownership> Ownerships { get; } =
    [
        new Ownership
        {
            Id = HollowKnightOwnership,
            ReleaseId = HollowKnightRelease,
            Store = "steam",
            Installed = true,
            InstallPath = @"C:\Games\Steam\steamapps\common\Hollow Knight",
            AcquiredAt = DaysAgo(700),
            LicenseType = "purchase",
            PricePaidCents = 1499,
        },
        new Ownership
        {
            Id = DiscoElysiumOwnership,
            ReleaseId = DiscoElysiumRelease,
            Store = "steam",
            AcquiredAt = DaysAgo(400),
            LicenseType = "purchase",
            PricePaidCents = 3999,
        },
        new Ownership
        {
            Id = WitcherSteamOwnership,
            ReleaseId = WitcherSteamRelease,
            Store = "steam",
            Installed = true,
            InstallPath = @"C:\Games\Steam\steamapps\common\The Witcher 3",
            AcquiredAt = DaysAgo(1200),
            LicenseType = "purchase",
            PricePaidCents = 999,
        },
        new Ownership
        {
            Id = WitcherGogOwnership,
            ReleaseId = WitcherGogRelease,
            Store = "gog",
            AcquiredAt = DaysAgo(1100),
            LicenseType = "purchase",
        },
        new Ownership
        {
            Id = StardewOwnership,
            ReleaseId = StardewRelease,
            Store = "gog",
            Installed = true,
            InstallPath = @"C:\Games\GOG\Stardew Valley",
            AcquiredAt = DaysAgo(900),
            LicenseType = "purchase",
            PricePaidCents = 1399,
        },
        new Ownership
        {
            Id = CelesteOwnership,
            ReleaseId = CelesteRelease,
            Store = "epic",
            AcquiredAt = DaysAgo(300),
            LicenseType = "giveaway",
        },
        new Ownership
        {
            Id = BaldursGateOwnership,
            ReleaseId = BaldursGateRelease,
            Store = "steam",
            Installed = true,
            InstallPath = @"C:\Games\Steam\steamapps\common\Baldurs Gate 3",
            AcquiredAt = DaysAgo(60),
            LicenseType = "purchase",
            PricePaidCents = 5999,
        },
        new Ownership
        {
            Id = SlayTheSpireOwnership,
            ReleaseId = SlayTheSpireRelease,
            Store = "steam",
            AcquiredAt = DaysAgo(1500),
            LicenseType = "bundle",
        },
        new Ownership
        {
            Id = Portal2Ownership,
            ReleaseId = Portal2Release,
            Store = "steam",
            Installed = true,
            InstallPath = @"C:\Games\Steam\steamapps\common\Portal 2",
            AcquiredAt = DaysAgo(2000),
            LicenseType = "purchase",
        },
    ];

    public static IReadOnlyList<ExternalId> ExternalIds { get; } =
    [
        new ExternalId { ReleaseId = HollowKnightRelease, Provider = ExternalIdProviders.Steam, ProviderId = "367520" },
        new ExternalId { ReleaseId = DiscoElysiumRelease, Provider = ExternalIdProviders.Steam, ProviderId = "632470" },
        new ExternalId { ReleaseId = WitcherSteamRelease, Provider = ExternalIdProviders.Steam, ProviderId = "292030" },
        new ExternalId { ReleaseId = WitcherGogRelease, Provider = ExternalIdProviders.Gog, ProviderId = "1207664643" },
        new ExternalId { ReleaseId = StardewRelease, Provider = ExternalIdProviders.Gog, ProviderId = "1453375253" },
        new ExternalId { ReleaseId = CelesteRelease, Provider = ExternalIdProviders.Epic, ProviderId = "celeste-preview" },
        new ExternalId { ReleaseId = BaldursGateRelease, Provider = ExternalIdProviders.Steam, ProviderId = "1086940" },
        new ExternalId { ReleaseId = SlayTheSpireRelease, Provider = ExternalIdProviders.Steam, ProviderId = "646570" },
        new ExternalId { ReleaseId = Portal2Release, Provider = ExternalIdProviders.Steam, ProviderId = "620" },
    ];

    /// <summary>
    /// Update signals, newest mattering most: Hollow Knight patched ten days
    /// ago (after its last session — the unread badge's case) and Stardew
    /// patched two months back, nine months after play stopped, which is the
    /// Stale-but-patched bucket's whole story.
    /// </summary>
    public static IReadOnlyList<UpdateEvent> UpdateEvents { get; } =
    [
        new UpdateEvent
        {
            Id = 1,
            ReleaseId = HollowKnightRelease,
            Kind = UpdateEventKinds.Announcement,
            OccurredAt = DaysAgo(10),
            Title = "Silksong is out now",
            Url = "https://store.steampowered.com/news/app/367520",
        },
        new UpdateEvent
        {
            Id = 2,
            ReleaseId = HollowKnightRelease,
            Kind = UpdateEventKinds.BuildPush,
            OccurredAt = DaysAgo(12),
            BuildId = "15284321",
        },
        new UpdateEvent
        {
            Id = 3,
            ReleaseId = StardewRelease,
            Kind = UpdateEventKinds.Announcement,
            OccurredAt = DaysAgo(60),
            Title = "Patch 1.6.15 — patch notes",
            Url = "https://www.gog.com/game/stardew_valley",
        },
        new UpdateEvent
        {
            Id = 4,
            ReleaseId = StardewRelease,
            Kind = UpdateEventKinds.BuildPush,
            OccurredAt = DaysAgo(62),
            BuildId = "gog-2.4.0.15",
        },
    ];

    /// <summary>
    /// The cached GOG product answer for Stardew, keyed exactly as the real
    /// cache keys it, so the details modal's patch-notes expander has prose
    /// to unfold.
    /// </summary>
    public static IReadOnlyDictionary<string, StorefrontDetails> Storefronts { get; } =
        new Dictionary<string, StorefrontDetails>(StringComparer.Ordinal)
        {
            ["gog:1453375253"] = new(
                "https://www.gog.com/game/stardew_valley",
                "Patch 1.6.15 — Fixed several multiplayer desyncs, corrected item pricing at the festival stalls and added the meadowlands farm layout."),
        };

    /// <summary>
    /// Stardew's cumulative playtime series: the details modal's record line
    /// reads it to say when play started and when it stopped.
    /// </summary>
    public static IReadOnlyList<PlaytimeSnapshot> Snapshots { get; } =
    [
        new PlaytimeSnapshot { Id = 1, OwnershipId = StardewOwnership, PlaytimeMinutes = 0, ObservedAt = DaysAgo(400) },
        new PlaytimeSnapshot { Id = 2, OwnershipId = StardewOwnership, PlaytimeMinutes = 120, ObservedAt = DaysAgo(330) },
        new PlaytimeSnapshot { Id = 3, OwnershipId = StardewOwnership, PlaytimeMinutes = 340, ObservedAt = DaysAgo(270) },
    ];

    public static StorefrontDetails StardewStorefront => Storefronts["gog:1453375253"];

    /// <summary>
    /// The read model's snapshot, folded through the same
    /// <see cref="GameGrouping.Of"/> call the real bucket query uses, with the
    /// GOG Witcher row resolved onto the Steam work the way a live
    /// <c>same_game</c> link would do it.
    /// </summary>
    public static LibrarySnapshot Snapshot()
    {
        var thresholds = BucketThresholds.Default;

        // One spec per ownership row. The Witcher GOG row carries its own
        // WorkId but resolves onto work 3, which is what makes the tile draw
        // one card with two store chips.
        RowSpec[] specs =
        [
            new(HollowKnightOwnership, HollowKnightRelease, HollowKnightWork, HollowKnightWork,
                1540, DaysAgo(40), MajorUpdateAt: DaysAgo(10), Unread: 1),
            new(DiscoElysiumOwnership, DiscoElysiumRelease, DiscoElysiumWork, DiscoElysiumWork,
                0, null, null, 0),
            new(WitcherSteamOwnership, WitcherSteamRelease, WitcherWork, WitcherWork,
                7200, DaysAgo(60), null, 0),
            new(WitcherGogOwnership, WitcherGogRelease, WitcherGogWork, WitcherWork,
                1000, DaysAgo(365), null, 0),
            new(StardewOwnership, StardewRelease, StardewWork, StardewWork,
                340, DaysAgo(270), MajorUpdateAt: DaysAgo(60), Unread: 1),
            new(CelesteOwnership, CelesteRelease, CelesteWork, CelesteWork,
                45, DaysAgo(20), null, 0),
            new(BaldursGateOwnership, BaldursGateRelease, BaldursGateWork, BaldursGateWork,
                90, DaysAgo(5), null, 0),
            new(SlayTheSpireOwnership, SlayTheSpireRelease, SlayTheSpireWork, SlayTheSpireWork,
                0, null, null, 0),
            new(Portal2Ownership, Portal2Release, Portal2Work, Portal2Work,
                6500, DaysAgo(730), null, 0),
        ];

        var rows = new List<OwnershipBucket>(specs.Length);
        foreach (var group in specs.GroupBy(spec => spec.ResolvedWorkId))
        {
            var members = group.ToList();
            var game = GameGrouping.Of(
                group.Key,
                members,
                members.Max(spec => spec.MajorUpdateAt),
                members.Sum(spec => spec.Unread),
                thresholds);

            foreach (var spec in members)
            {
                // The row's own bucket is folded the same way the game's is —
                // one entry, its own figures — because LibraryBucketRules is
                // not public and should not need to be.
                var own = GameGrouping.Of(
                    spec.WorkId, [spec], spec.MajorUpdateAt, spec.Unread, thresholds);

                rows.Add(new OwnershipBucket
                {
                    OwnershipId = spec.OwnershipId,
                    ReleaseId = spec.ReleaseId,
                    WorkId = spec.WorkId,
                    ResolvedWorkId = spec.ResolvedWorkId,
                    PlaytimeMinutes = spec.PlaytimeMinutes,
                    LastPlayedAt = spec.LastPlayedAt,
                    MajorUpdateAt = spec.MajorUpdateAt,
                    Bucket = own.Bucket,
                    Game = game,
                });
            }
        }

        return new LibrarySnapshot(rows, Works, Ownerships, Releases, ExternalIds, [], []);
    }

    /// <summary>UTC, back-dated. Relative on purpose — see the type's remarks.</summary>
    internal static DateTime DaysAgo(double days) => DateTime.UtcNow.AddDays(-days);

    /// <summary>What a bucket row is folded from, before the <see cref="GameGrouping"/> exists to point at.</summary>
    private sealed record RowSpec(
        long OwnershipId,
        long ReleaseId,
        long WorkId,
        long ResolvedWorkId,
        long PlaytimeMinutes,
        DateTime? LastPlayedAt,
        DateTime? MajorUpdateAt,
        int Unread) : IPlayedEntry;
}
