#if DEBUG
using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Hoard.Resolve.Matching;
using Microsoft.Extensions.DependencyInjection;

namespace Hoard.App.Services;

/// <summary>
/// Debug-only sample library, invoked with <c>dotnet run -- --seed-sample</c>.
/// Inserts ~40 varied works/releases/ownerships/play_records spanning every
/// derived bucket and the full dormancy ramp — including titles whose update
/// events landed after last play, so "Patched since" counts — making the M0
/// view visually verifiable before real ingest is wired. Writes through the
/// repository layer only.
/// </summary>
public static class SampleDataSeeder
{
    private sealed record Sample(
        string Name,
        int Year,
        string Store,
        long PlaytimeMinutes,
        double? LastPlayedMonthsAgo,
        double? PatchedMonthsAfterPlay = null,
        bool EmptyPlayRecord = false);

    public static async Task SeedAsync(IServiceProvider services)
    {
        await SeedLibraryAsync(services);

        // Runs whether or not the library was seeded, so the merge confirm
        // queue is verifiable against a REAL scanned library — soft matching is
        // not wired into the sync yet, so a real library's queue is empty.
        await SeedMergeCandidatesAsync(services);
    }

    private static async Task SeedLibraryAsync(IServiceProvider services)
    {
        var works = services.GetRequiredService<IWorkRepository>();
        var releases = services.GetRequiredService<IReleaseRepository>();
        var ownerships = services.GetRequiredService<IOwnershipRepository>();
        var playRecords = services.GetRequiredService<IPlayRecordRepository>();
        var updateEvents = services.GetRequiredService<IUpdateEventRepository>();

        if ((await works.GetAllAsync()).Count > 0)
        {
            Console.WriteLine("--seed-sample: library is not empty, skipping.");
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var s in Samples)
        {
            var workId = await works.InsertAsync(new Work { Name = s.Name, FirstReleaseYear = s.Year });
            var releaseId = await releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = s.Name,
                Platform = "windows",
            });
            var ownershipId = await ownerships.InsertAsync(new Ownership
            {
                ReleaseId = releaseId,
                Store = s.Store,
                AccountRef = "sample",
                Installed = false,
            });

            DateTime? lastPlayed = s.LastPlayedMonthsAgo is { } months
                ? now.AddDays(-months * 30.4375)
                : null;

            if (lastPlayed is not null || s.EmptyPlayRecord)
            {
                await playRecords.InsertAsync(new PlayRecord
                {
                    OwnershipId = ownershipId,
                    PlaytimeMinutes = s.PlaytimeMinutes,
                    LastPlayedAt = lastPlayed,
                    Source = "sample",
                    ObservedAt = now,
                });
            }

            if (s.PatchedMonthsAfterPlay is { } offset && lastPlayed is { } played)
            {
                var patchedAt = played.AddDays(offset * 30.4375);
                await updateEvents.InsertAsync(new UpdateEvent
                {
                    ReleaseId = releaseId,
                    Kind = UpdateEventKinds.BuildPush,
                    BuildId = "sample-build",
                    OccurredAt = patchedAt,
                    Title = "Sample content update",
                });
                await updateEvents.InsertAsync(new UpdateEvent
                {
                    ReleaseId = releaseId,
                    Kind = UpdateEventKinds.Announcement,
                    OccurredAt = patchedAt.AddHours(2),
                    Title = "Sample update notes",
                });
            }
        }

        Console.WriteLine($"--seed-sample: inserted {Samples.Length} sample titles.");
    }

    // ── Merge confirm queue (design-system §6) ───────────────────────────────

    /// <summary>One side of a seeded soft-match pair, as a store feed would supply it.</summary>
    /// <param name="Title">Raw store title, exactly as it appears in the feed.</param>
    /// <param name="SteamAppId">Real appid, so the cover cache fetches the real capsule.</param>
    /// <param name="Year">Null models the common M1 state: appdetails backfill has not reached this row yet.</param>
    private sealed record MergeSide(string Title, string SteamAppId, int? Year, string? Publisher);

    /// <summary>
    /// Debug-only pending merge candidates, written through the real
    /// <see cref="SoftMatcher"/> so <c>score</c> and <c>signals_json</c> are
    /// byte-for-byte what the resolver would have produced. Seeding a
    /// hand-written payload would let the screen render a breakdown the matcher
    /// can never actually emit.
    ///
    /// <para>The three pairs are chosen to be instructive rather than merely
    /// plausible:</para>
    /// <list type="bullet">
    ///   <item><b>Witcher 3 / Witcher 3 GOTY</b> — the genuine near-match. Same
    ///     game, one is a content bundle. Everything corroborates, so it lands
    ///     in the priority band and is offered first.</item>
    ///   <item><b>Prey / Prey</b> — §5.3's canonical trap, in the shape that
    ///     actually reaches the queue. Identical titles, but the 2006 record has
    ///     no year and no publisher yet, so the year signal never fires and the
    ///     far-year penalty that would have discarded the pair never applies.
    ///     The score is 0.65 on title alone; the breakdown says so in as many
    ///     words, and the two covers say the rest. A user must be able to answer
    ///     "Different games" here, and the screen has to make that answer
    ///     obvious.</item>
    ///   <item><b>Deus Ex: Human Revolution / Director's Cut</b> — the honest
    ///     judgement call, with evidence pulling both ways: publisher agrees,
    ///     the years do not, and one side is an edition bundle.</item>
    /// </list>
    ///
    /// <para>Re-running is safe: pairs already in the table are left alone in
    /// whatever status they carry, so a pair answered "Different games" stays
    /// rejected rather than being resurrected by the next seed.</para>
    /// </summary>
    private static async Task SeedMergeCandidatesAsync(IServiceProvider services)
    {
        var works = services.GetRequiredService<IWorkRepository>();
        var releases = services.GetRequiredService<IReleaseRepository>();
        var candidates = services.GetRequiredService<IMergeCandidateRepository>();

        var matcher = new SoftMatcher();
        var queued = 0;
        var skipped = 0;

        foreach (var (left, right) in MergePairs)
        {
            var leftRelease = await FindOrCreateReleaseAsync(works, releases, left);
            var rightRelease = await FindOrCreateReleaseAsync(works, releases, right);

            var low = Math.Min(leftRelease, rightRelease);
            var high = Math.Max(leftRelease, rightRelease);
            if (low == high || await candidates.FindByPairAsync(low, high) is not null)
            {
                skipped++;
                continue;
            }

            var lowSide = low == leftRelease ? left : right;
            var highSide = low == leftRelease ? right : left;

            var score = matcher.Score(Subject(low, lowSide), Subject(high, highSide));
            if (!score.ShouldQueue)
            {
                // The matcher declined the pair. Forcing the row in anyway would
                // put something on screen that the real pipeline can never put
                // there, which is worse than an empty demo.
                Console.WriteLine(
                    $"--seed-sample: matcher discarded {left.Title} / {right.Title} "
                    + $"at {score.Score:F2}{(score.VetoReason is null ? string.Empty : $" (veto: {score.VetoReason})")}.");
                skipped++;
                continue;
            }

            await candidates.InsertAsync(new MergeCandidate
            {
                LeftReleaseId = low,
                RightReleaseId = high,
                Score = score.Score,
                SignalsJson = SoftMatchSignalsJson.Serialize(score),
                Status = MergeCandidateStatuses.Pending,
            });
            queued++;
        }

        Console.WriteLine($"--seed-sample: queued {queued} merge candidates ({skipped} already present or discarded).");
    }

    private static MatchSubject Subject(long releaseId, MergeSide side)
        => new()
        {
            ReleaseId = releaseId,
            Title = side.Title,
            ReleaseYear = side.Year,
            Publisher = side.Publisher,
        };

    /// <summary>
    /// Reuses the release the real scan already created for this appid when
    /// there is one — the point of running this against the real library is that
    /// the pair is about the user's own rows — and mints one otherwise.
    /// No ownership row is created: the queue asks about releases, and inventing
    /// ownerships would misreport how many games the library actually holds.
    /// </summary>
    private static async Task<long> FindOrCreateReleaseAsync(
        IWorkRepository works, IReleaseRepository releases, MergeSide side)
    {
        var existing = await releases.FindByExternalIdAsync(ExternalIdProviders.Steam, side.SteamAppId);
        if (existing is not null)
        {
            return existing.Id;
        }

        var workId = await works.InsertAsync(new Work
        {
            Name = side.Title,
            FirstReleaseYear = side.Year,
        });

        var releaseId = await releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = side.Title,
            Platform = "windows",
        });

        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = side.SteamAppId,
        });

        return releaseId;
    }

    private static readonly (MergeSide Left, MergeSide Right)[] MergePairs =
    [
        // Genuine near-match: same game, one side is a content bundle. Bundle
        // disagreement is a small penalty, never a veto — this is exactly the
        // merge a user plausibly wants, and it is their call.
        (new MergeSide("The Witcher 3: Wild Hunt", "292030", 2015, "CD PROJEKT RED"),
         new MergeSide("The Witcher 3: Wild Hunt - Game of the Year Edition", "499450", 2016, "CD PROJEKT RED")),

        // The dangerous pair. Two different games, identical titles, and no
        // corroborating metadata on the older record to separate them.
        (new MergeSide("Prey", "480490", 2017, "Bethesda Softworks"),
         new MergeSide("Prey", "3970", null, null)),

        // Evidence both ways: publisher agrees, the years do not, one side is an
        // edition bundle. A real judgement call rather than an obvious answer.
        (new MergeSide("Deus Ex: Human Revolution", "28050", 2011, "Square Enix"),
         new MergeSide("Deus Ex: Human Revolution - Director's Cut", "238010", 2013, "Square Enix")),
    ];

    // Bucket maths (BucketThresholds.Default): never_touched = 0 min;
    // bounced < 120 min; retired >= 6000 min; stale_but_patched needs an
    // update event more than 6 months after last play; the rest are active.
    // Dormancy ages span the whole §5.1 ramp (weeks → 4 years → never).
    private static readonly Sample[] Samples =
    [
        // ── Never played (10) — zero playtime; mixed "no record" / "0-min record".
        new("Tunic", 2022, "steam", 0, null),
        new("Disco Elysium", 2019, "gog", 0, null, EmptyPlayRecord: true),
        new("Return of the Obra Dinn", 2018, "steam", 0, null),
        new("Pentiment", 2022, "steam", 0, null, EmptyPlayRecord: true),
        new("Citizen Sleeper", 2022, "epic", 0, null),
        new("Cave Story+", 2011, "steam", 0, null),
        new("Owlboy", 2016, "gog", 0, null, EmptyPlayRecord: true),
        new("Iconoclasts", 2018, "steam", 0, null),
        new("The Pedestrian", 2020, "epic", 0, null),
        new("A Short Hike", 2019, "steam", 0, null, EmptyPlayRecord: true),

        // ── Bounced off (8) — under 2 h, spread across the dormancy ramp.
        new("Kenshi", 2013, "gog", 23, 38),                    // 23 min, ~3y2mo
        new("Noita", 2020, "steam", 95, 35),
        new("Outer Wilds", 2019, "epic", 110, 26),
        new("Baba Is You", 2019, "steam", 45, 18),
        new("Hades", 2020, "epic", 80, 9),
        new("Loop Hero", 2021, "steam", 65, 4),
        new("Dorfromantik", 2022, "steam", 40, 1.5),
        new("Inscryption", 2021, "gog", 105, 0.4),

        // ── Played out / retired (8) — 100 h+, spread across the ramp.
        new("RimWorld", 2018, "steam", 312 * 60, 8),
        new("Factorio", 2020, "steam", 190 * 60, 16),
        new("Terraria", 2011, "steam", 204 * 60, 32),
        new("Stardew Valley", 2016, "gog", 156 * 60, 18),
        new("Deep Rock Galactic", 2020, "steam", 127 * 60, 19),
        new("Euro Truck Simulator 2", 2012, "steam", 140 * 60, 44),
        new("Crusader Kings III", 2020, "steam", 230 * 60, 2),
        new("Path of Exile", 2013, "steam", 410 * 60, 27),

        // ── Patched since (6) — meaningful playtime, then an update landed
        //    well past the 6-month stale window after last play.
        new("Vintage Story", 2016, "gog", 41 * 60, 11, PatchedMonthsAfterPlay: 8),
        new("Oxygen Not Included", 2019, "steam", 52 * 60, 14, PatchedMonthsAfterPlay: 12),
        new("Valheim", 2021, "steam", 96 * 60, 21, PatchedMonthsAfterPlay: 15),
        new("Dyson Sphere Program", 2021, "steam", 37 * 60, 24, PatchedMonthsAfterPlay: 20),
        new("Project Zomboid", 2013, "steam", 88 * 60, 25, PatchedMonthsAfterPlay: 18),
        new("Satisfactory", 2020, "epic", 64 * 60, 29, PatchedMonthsAfterPlay: 24),

        // ── Active (8) — played past the bounce line, nothing stale.
        new("Slay the Spire", 2019, "steam", 89 * 60, 27),
        new("Hollow Knight", 2017, "steam", 62 * 60, 40),
        new("Subnautica", 2018, "steam", 44 * 60, 30),
        new("Grim Dawn", 2016, "gog", 71 * 60, 42),
        new("Celeste", 2018, "steam", 19 * 60, 13),
        new("Against the Storm", 2023, "epic", 55 * 60, 0.3),
        new("Balatro", 2024, "steam", 61 * 60, 0.1),
        new("Caves of Qud", 2024, "steam", 33 * 60, 5),
    ];
}
#endif
