using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Model;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Migration 0024's <c>work_maturity</c> table, the <c>MaturityTiers</c> scale
/// and the <c>MaturityRules</c> read-time verdict. The table stores evidence —
/// rating tokens and descriptor tokens, verbatim, one row per (work, source) —
/// and never a verdict. Whether a work is explicit is decided at read time by
/// <c>MaturityRules.IsExplicit</c>, the same pattern <c>NonGameEntries</c> uses,
/// so the vocabulary can be retuned without a migration.
///
/// <para>Explicit means the <c>AdultsOnly</c> tier: rating tokens <c>esrb:ao</c>
/// and <c>acb:x18</c>, or the <c>adult_only_sexual_content</c> descriptor. The
/// broad 18+ board ratings (<c>pegi:18</c>, <c>usk:18</c>, <c>cero:z</c>,
/// <c>acb:r18</c>, <c>classind:18</c>, <c>grac:18</c>, <c>acb:rc</c>) are stored
/// evidence at the <c>Restricted18</c> tier — a maturity level, not an adults-only
/// flag. A work with no maturity row is never explicit.</para>
/// </summary>
public sealed class ExplicitContentTests : IDisposable
{
    private static readonly BucketThresholds Hiding = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 3000,
        StaleWindowMonths: 3,
        UpdateCorrelationWindowDays: 7,
        ShowNonGameEntries: false,
        ShowExplicitContent: false);

    private static readonly BucketThresholds Showing = Hiding with { ShowExplicitContent = true };

    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly WorkMaturityRepository _maturity;
    private readonly LibraryQueryRepository _library;

    public ExplicitContentTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _maturity = new WorkMaturityRepository(_db.Factory);
        _library = new LibraryQueryRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    // ── The rule ────────────────────────────────────────────────────────────

    [Fact]
    public void No_maturity_data_is_never_explicit()
    {
        Assert.False(MaturityRules.IsExplicit(null, null));
        Assert.False(MaturityRules.IsExplicit(string.Empty, "   "));
    }

    [Theory]
    [InlineData(MaturityRatingCodes.EsrbAdultsOnly)]
    [InlineData(MaturityRatingCodes.AcbX18)]
    public void An_adults_only_rating_is_explicit(string code)
        => Assert.True(MaturityRules.IsExplicit(code, null));

    // The ratings TASK-101 moved out of the explicit gate: legitimate 18+
    // board tiers that mainstream violent games routinely carry. They remain
    // stored evidence at Restricted18.
    [Theory]
    [InlineData(MaturityRatingCodes.Pegi18)]
    [InlineData(MaturityRatingCodes.Usk18)]
    [InlineData(MaturityRatingCodes.CeroZ)]
    [InlineData(MaturityRatingCodes.AcbR18)]
    [InlineData(MaturityRatingCodes.ClassInd18)]
    [InlineData(MaturityRatingCodes.Grac18)]
    [InlineData("acb:rc")]
    public void A_broad_eighteen_plus_board_rating_is_not_explicit(string code)
    {
        Assert.False(MaturityRules.IsExplicit(code, null));
        Assert.Equal(MaturityTier.Restricted18, MaturityTiers.Highest(code, null));
    }

    [Theory]
    [InlineData(MaturityRatingCodes.EsrbMature)]
    [InlineData(MaturityRatingCodes.Pegi16)]
    [InlineData("esrb:t")]
    public void A_rating_below_the_eighteen_plus_tier_is_not_explicit(string code)
        => Assert.False(MaturityRules.IsExplicit(code, null));

    // The guard against silently re-widening the gate: if a code or
    // descriptor is added to the adults-only tier, this test fails and
    // forces the change to be deliberate.
    [Fact]
    public void The_explicit_set_is_exactly_the_adults_only_signals()
    {
        Assert.Equal(
            [MaturityRatingCodes.AcbX18, MaturityRatingCodes.EsrbAdultsOnly],
            MaturityRules.ExplicitRatingCodes);
        Assert.Equal(
            [MaturityDescriptors.AdultOnlySexualContent],
            MaturityRules.ExplicitDescriptors);
        Assert.Equal(MaturityTier.AdultsOnly, MaturityRules.ExplicitTier);
    }

    [Fact]
    public void Only_the_adult_only_descriptor_is_explicit()
    {
        Assert.True(MaturityRules.IsExplicit(null, MaturityDescriptors.AdultOnlySexualContent));
        Assert.False(MaturityRules.IsExplicit(null, MaturityDescriptors.NudityOrSexualContent));
        Assert.False(MaturityRules.IsExplicit(null, MaturityDescriptors.GeneralMatureContent));
        Assert.False(MaturityRules.IsExplicit(null, MaturityDescriptors.ViolenceOrGore));
    }

    [Fact]
    public void One_explicit_token_among_many_is_enough()
        => Assert.True(MaturityRules.IsExplicit(
            $"{MaturityRatingCodes.EsrbMature},{MaturityRatingCodes.Pegi18},{MaturityRatingCodes.EsrbAdultsOnly}",
            MaturityDescriptors.ViolenceOrGore));

    [Fact]
    public void Tokens_are_matched_case_insensitively_and_around_whitespace()
        => Assert.True(MaturityRules.IsExplicit(" ESRB:AO , pegi:16 ", null));

    [Fact]
    public void Unknown_tokens_are_carried_without_being_read_as_explicit()
        => Assert.False(MaturityRules.IsExplicit("bbfc:18", "loud_noises"));

    [Fact]
    public void Join_and_split_round_trip_and_drop_blanks_and_duplicates()
    {
        var joined = MaturityRules.Join(["esrb:ao", " esrb:ao ", "  ", "pegi:18"]);

        Assert.Equal("esrb:ao,pegi:18", joined);
        Assert.Equal(["esrb:ao", "pegi:18"], MaturityRules.Split(joined));
        Assert.Null(MaturityRules.Join([]));
        Assert.Empty(MaturityRules.Split(null));
    }

    // ── The tier scale ──────────────────────────────────────────────────────

    [Fact]
    public void The_scale_is_ordered_from_everyone_to_adults_only()
    {
        Assert.Equal(
            [
                MaturityTier.Everyone,
                MaturityTier.Preteen,
                MaturityTier.Teen,
                MaturityTier.Mature,
                MaturityTier.Restricted18,
                MaturityTier.AdultsOnly,
            ],
            MaturityTiers.Ordered);

        for (var i = 1; i < MaturityTiers.Ordered.Count; i++)
        {
            Assert.True(MaturityTiers.Ordered[i - 1] < MaturityTiers.Ordered[i]);
        }

        Assert.True(MaturityTier.Unrated < MaturityTiers.Ordered[0]);
    }

    [Theory]
    [InlineData("esrb:e", MaturityTier.Everyone)]
    [InlineData("esrb:e10", MaturityTier.Preteen)]
    [InlineData("esrb:t", MaturityTier.Teen)]
    [InlineData("esrb:m", MaturityTier.Mature)]
    [InlineData("esrb:ao", MaturityTier.AdultsOnly)]
    [InlineData("pegi:7", MaturityTier.Everyone)]
    [InlineData("pegi:12", MaturityTier.Teen)]
    [InlineData("pegi:16", MaturityTier.Mature)]
    [InlineData("pegi:18", MaturityTier.Restricted18)]
    [InlineData("acb:ma15", MaturityTier.Teen)]
    [InlineData("acb:r18", MaturityTier.Restricted18)]
    [InlineData("acb:x18", MaturityTier.AdultsOnly)]
    [InlineData("cero:z", MaturityTier.Restricted18)]
    [InlineData("usk:18", MaturityTier.Restricted18)]
    [InlineData("grac:18", MaturityTier.Restricted18)]
    [InlineData("classind:18", MaturityTier.Restricted18)]
    [InlineData("esrb:rp", MaturityTier.Unrated)]
    [InlineData("grac:testing", MaturityTier.Unrated)]
    [InlineData("bbfc:18", MaturityTier.Unrated)]
    public void A_rating_token_maps_to_its_tier(string token, MaturityTier expected)
        => Assert.Equal(expected, MaturityTiers.ForRating(token));

    [Fact]
    public void The_highest_tier_across_the_evidence_wins()
    {
        Assert.Equal(MaturityTier.Unrated, MaturityTiers.Highest(null, null));
        Assert.Equal(
            MaturityTier.Restricted18,
            MaturityTiers.Highest("esrb:m,pegi:18,acb:ma15", MaturityDescriptors.ViolenceOrGore));
        Assert.Equal(
            MaturityTier.AdultsOnly,
            MaturityTiers.Highest("esrb:m,pegi:18", MaturityDescriptors.AdultOnlySexualContent));
    }

    [Theory]
    [InlineData(MaturityDescriptors.AdultOnlySexualContent, MaturityTier.AdultsOnly)]
    [InlineData(MaturityDescriptors.FrequentNudityOrSexualContent, MaturityTier.Restricted18)]
    [InlineData(MaturityDescriptors.NudityOrSexualContent, MaturityTier.Mature)]
    [InlineData(MaturityDescriptors.GeneralMatureContent, MaturityTier.Mature)]
    [InlineData(MaturityDescriptors.ViolenceOrGore, MaturityTier.Mature)]
    public void Every_descriptor_reaches_the_scale_and_only_one_reaches_the_top(
        string token, MaturityTier expected)
    {
        Assert.Equal(expected, MaturityTiers.ForDescriptor(token));
        Assert.Equal(
            token == MaturityDescriptors.AdultOnlySexualContent,
            MaturityRules.IsExplicit(null, token));
    }

    // An unrated work passes every cap: "no data" must never hide a game
    // from a rating-cap filter, because the absence of a rating is not
    // evidence of maturity.
    [Fact]
    public void An_unrated_work_is_within_every_cap()
    {
        foreach (var cap in MaturityTiers.Ordered)
        {
            Assert.True(MaturityTiers.IsWithinCap(MaturityTier.Unrated, cap));
        }

        Assert.True(MaturityTiers.IsWithinCap(MaturityTier.Teen, MaturityTier.Mature));
        Assert.True(MaturityTiers.IsWithinCap(MaturityTier.Mature, MaturityTier.Mature));
        Assert.False(MaturityTiers.IsWithinCap(MaturityTier.Restricted18, MaturityTier.Mature));
    }

    // Completeness check: every token IgdbAgeRatingTokens can produce must
    // have a tier in MaturityTiers, so a new IGDB board or category cannot
    // silently fall into Unrated and escape the scale.
    [Fact]
    public void Every_rating_token_the_igdb_reader_can_emit_has_a_tier()
    {
        string[] unratedByDesign = ["esrb:rp", "grac:testing"];

        for (var rating = 1; rating <= 39; rating++)
        {
            var token = IgdbAgeRatingTokens.FromLegacyRating(rating);
            Assert.NotNull(token);

            var tier = MaturityTiers.ForRating(token);
            if (unratedByDesign.Contains(token))
            {
                Assert.Equal(MaturityTier.Unrated, tier);
            }
            else
            {
                Assert.NotEqual(MaturityTier.Unrated, tier);
            }
        }

        Assert.Equal(MaturityRatingCodes.AcbX18, IgdbAgeRatingTokens.FromLabels("ACB", "X18PLUS"));
        Assert.Equal(MaturityTier.AdultsOnly, MaturityTiers.ForRating(MaturityRatingCodes.AcbX18));
    }

    // ── Storage ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_maturity_row_round_trips()
    {
        var (workId, _, _) = await SeedGameAsync("Nameless Game");
        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.Igdb,
            Ratings = MaturityRatingCodes.Pegi18,
            Descriptors = null,
            ObservedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        var stored = Assert.Single(await _maturity.GetForWorkAsync(workId));

        Assert.Equal(MaturitySources.Igdb, stored.Source);
        Assert.Equal(MaturityRatingCodes.Pegi18, stored.Ratings);
        Assert.False(stored.IsExplicit);
        Assert.False(await _maturity.IsExplicitAsync(workId));
        Assert.Equal(MaturityTier.Restricted18, MaturityTiers.Highest(stored.Ratings, stored.Descriptors));
    }

    [Fact]
    public async Task An_adults_only_row_round_trips_and_reads_as_explicit()
    {
        var (workId, _, _) = await SeedGameAsync("Nameless Game");
        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.Igdb,
            Ratings = MaturityRatingCodes.EsrbAdultsOnly,
            ObservedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        var stored = Assert.Single(await _maturity.GetForWorkAsync(workId));

        Assert.Equal(MaturityRatingCodes.EsrbAdultsOnly, stored.Ratings);
        Assert.True(stored.IsExplicit);
        Assert.True(await _maturity.IsExplicitAsync(workId));
    }

    [Fact]
    public async Task Each_source_keeps_its_own_row_and_re_reading_replaces_only_its_own()
    {
        var (workId, _, _) = await SeedGameAsync("Nameless Game");
        var observed = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.Igdb,
            Ratings = MaturityRatingCodes.EsrbMature,
            ObservedAt = observed,
        });
        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.SteamStore,
            Descriptors = MaturityDescriptors.AdultOnlySexualContent,
            ObservedAt = observed,
        });

        Assert.Equal(2, (await _maturity.GetForWorkAsync(workId)).Count);
        Assert.True(await _maturity.IsExplicitAsync(workId));

        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.Igdb,
            Ratings = MaturityRatingCodes.Pegi16,
            ObservedAt = observed.AddDays(1),
        });

        var rows = await _maturity.GetForWorkAsync(workId);
        Assert.Equal(2, rows.Count);
        Assert.Equal(
            MaturityDescriptors.AdultOnlySexualContent,
            rows.Single(r => r.Source == MaturitySources.SteamStore).Descriptors);
    }

    // ── The filter ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_game_with_no_maturity_row_is_shown_either_way()
    {
        var (workId, _, _) = await SeedGameAsync("Nameless Game");

        Assert.Equal([workId], (await _library.GetOwnershipBucketsAsync(Hiding))
            .Select(r => r.ResolvedWorkId));
        Assert.Equal([workId], (await _library.GetOwnershipBucketsAsync(Showing))
            .Select(r => r.ResolvedWorkId));
        Assert.Equal(0, await _library.CountHiddenByExplicitFilterAsync(Hiding));
    }

    // Reproduces the rating profile of GTA V, Doom Eternal, The Witcher 3
    // and Cyberpunk 2077: esrb:m plus pegi:18, usk:18 and the other broad
    // 18+ board tiers, with violence and mature-content descriptors. None
    // of these signals reaches the adults-only tier.
    [Fact]
    public async Task A_mainstream_violent_game_is_shown_when_the_setting_is_off()
    {
        var (workId, _, _) = await SeedGameAsync("Nameless Game");
        var observed = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.Igdb,
            Ratings = MaturityRules.Join(
            [
                MaturityRatingCodes.EsrbMature,
                MaturityRatingCodes.Pegi18,
                MaturityRatingCodes.Usk18,
                MaturityRatingCodes.AcbR18,
                MaturityRatingCodes.CeroZ,
                MaturityRatingCodes.ClassInd18,
                MaturityRatingCodes.Grac18,
            ]),
            ObservedAt = observed,
        });
        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.SteamStore,
            Descriptors = MaturityRules.Join(
            [
                MaturityDescriptors.ViolenceOrGore,
                MaturityDescriptors.GeneralMatureContent,
                MaturityDescriptors.NudityOrSexualContent,
            ]),
            ObservedAt = observed,
        });

        Assert.False(await _maturity.IsExplicitAsync(workId));
        Assert.Equal([workId], (await _library.GetOwnershipBucketsAsync(Hiding))
            .Select(r => r.ResolvedWorkId));
        Assert.Equal(0, await _library.CountHiddenByExplicitFilterAsync(Hiding));

        var stored = await _maturity.GetForWorkAsync(workId);
        Assert.Contains(MaturityRatingCodes.Pegi18, MaturityRules.Split(
            stored.Single(r => r.Source == MaturitySources.Igdb).Ratings));
        Assert.Equal(
            MaturityTier.Restricted18,
            MaturityTiers.Highest(
                stored.Single(r => r.Source == MaturitySources.Igdb).Ratings,
                stored.Single(r => r.Source == MaturitySources.SteamStore).Descriptors));
    }

    [Fact]
    public async Task An_adults_only_rating_still_hides_the_game_when_the_setting_is_off()
    {
        var (adultWorkId, _, _) = await SeedGameAsync("Nameless Game");
        var (otherWorkId, _, _) = await SeedGameAsync("Outer Wilds");

        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = adultWorkId,
            Source = MaturitySources.Igdb,
            Ratings = MaturityRatingCodes.EsrbAdultsOnly,
            ObservedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        Assert.Equal([otherWorkId], (await _library.GetOwnershipBucketsAsync(Hiding))
            .Select(r => r.ResolvedWorkId));
        Assert.Equal(2, (await _library.GetOwnershipBucketsAsync(Showing)).Count);
        Assert.Equal(1, await _library.CountHiddenByExplicitFilterAsync(Hiding));
    }

    [Fact]
    public async Task An_explicit_game_is_hidden_when_the_setting_is_off_and_shown_when_it_is_on()
    {
        var (explicitWorkId, _, _) = await SeedGameAsync("Nameless Game");
        var (otherWorkId, _, _) = await SeedGameAsync("Outer Wilds");
        await FlagExplicitAsync(explicitWorkId);

        Assert.Equal([otherWorkId], (await _library.GetOwnershipBucketsAsync(Hiding))
            .Select(r => r.ResolvedWorkId));
        Assert.Equal(2, (await _library.GetOwnershipBucketsAsync(Showing)).Count);
        Assert.Equal(1, await _library.CountHiddenByExplicitFilterAsync(Hiding));
    }

    [Fact]
    public async Task One_flagged_entry_hides_the_whole_linked_game()
    {
        var (parentWorkId, _, _) = await SeedGameAsync("Nameless Game", store: "steam");
        var (childWorkId, _, _) = await SeedGameAsync("Nameless Game", store: "gog");

        await new IdentityLinkRepository(_db.Factory).LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parentWorkId,
            ChildWorkIds = [childWorkId],
        });

        await FlagExplicitAsync(childWorkId);

        Assert.Empty(await _library.GetOwnershipBucketsAsync(Hiding));
        Assert.Equal(2, (await _library.GetOwnershipBucketsAsync(Showing)).Count);
    }

    [Fact]
    public async Task A_variant_of_an_explicit_game_is_hidden_with_it()
    {
        var (parentWorkId, _, _) = await SeedGameAsync("Nameless Game");
        var (variantWorkId, _, _) = await SeedGameAsync("Unrelated Trial");

        await new IdentityLinkRepository(_db.Factory).LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parentWorkId,
            ChildWorkIds = [variantWorkId],
            Kind = IdentityLinkKinds.VariantOf,
        });

        await FlagExplicitAsync(parentWorkId);

        Assert.Empty(await _library.GetOwnershipBucketsAsync(Hiding));
    }

    private Task FlagExplicitAsync(long workId)
        => _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.SteamStore,
            Descriptors = MaturityDescriptors.AdultOnlySexualContent,
            ObservedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        });

    private async Task<(long WorkId, long ReleaseId, long OwnershipId)> SeedGameAsync(
        string name, string store = "steam")
    {
        var workId = await _works.InsertAsync(new Work { Name = name });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = store,
        });

        return (workId, releaseId, ownershipId);
    }
}
