using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class RatingCapTests : IDisposable
{
    private static readonly BucketThresholds Base = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 3000,
        StaleWindowMonths: 3,
        UpdateCorrelationWindowDays: 7,
        ShowNonGameEntries: false,
        ShowExplicitContent: false,
        MaturityCap: BucketThresholds.NoMaturityCap);

    private static readonly DateTime Observed = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly WorkMaturityRepository _maturity;
    private readonly LibraryQueryRepository _library;

    public RatingCapTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _maturity = new WorkMaturityRepository(_db.Factory);
        _library = new LibraryQueryRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    // ── The scale ───────────────────────────────────────────────────────────

    [Fact]
    public void The_scale_ascends_and_excludes_unrated()
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

        Assert.DoesNotContain(MaturityTier.Unrated, MaturityTiers.Ordered);
    }

    [Fact]
    public void A_tier_is_within_its_own_cap_and_every_higher_one()
    {
        foreach (var tier in MaturityTiers.Ordered)
        {
            foreach (var cap in MaturityTiers.Ordered)
            {
                Assert.Equal(tier <= cap, MaturityTiers.IsWithinCap(tier, cap));
            }
        }
    }

    [Fact]
    public void Unrated_is_within_every_cap()
    {
        foreach (var cap in MaturityTiers.Ordered)
        {
            Assert.True(MaturityTiers.IsWithinCap(MaturityTier.Unrated, cap));
        }
    }

    [Fact]
    public void Every_cap_step_round_trips_through_storage()
    {
        foreach (var cap in MaturityTiers.Ordered)
        {
            var stored = BucketThresholds.FormatMaturityCap(cap);
            Assert.NotEqual(string.Empty, stored);
            Assert.Equal(cap, BucketThresholds.ParseMaturityCap(stored));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not_a_tier")]
    public void An_unreadable_stored_cap_falls_back_to_no_cap(string? stored)
        => Assert.Equal(BucketThresholds.NoMaturityCap, BucketThresholds.ParseMaturityCap(stored));

    // ── The Steam descriptors (AC7) ─────────────────────────────────────────

    [Theory]
    [InlineData(MaturityDescriptors.NudityOrSexualContent, MaturityTier.Mature)]
    [InlineData(MaturityDescriptors.ViolenceOrGore, MaturityTier.Mature)]
    [InlineData(MaturityDescriptors.GeneralMatureContent, MaturityTier.Mature)]
    [InlineData(MaturityDescriptors.FrequentNudityOrSexualContent, MaturityTier.Restricted18)]
    [InlineData(MaturityDescriptors.AdultOnlySexualContent, MaturityTier.AdultsOnly)]
    public void Every_steam_descriptor_carries_a_tier(string descriptor, MaturityTier expected)
    {
        Assert.Equal(expected, MaturityTiers.ForDescriptor(descriptor));
        Assert.NotEqual(MaturityTier.Unrated, MaturityTiers.Highest(null, descriptor));
    }

    // The point of AC7: before this, a nudity-heavy game read as Unrated and
    // sat inside every cap. Descriptor 4 is Valve's own step up from "some",
    // and it now clears a Mature cap.
    [Fact]
    public void Frequent_nudity_is_caught_by_a_mature_cap_without_reaching_the_explicit_gate()
    {
        var descriptor = MaturityDescriptors.FrequentNudityOrSexualContent;

        Assert.False(MaturityTiers.IsWithinCap(
            MaturityTiers.Highest(null, descriptor), MaturityTier.Mature));
        Assert.True(MaturityTiers.IsWithinCap(
            MaturityTiers.Highest(null, descriptor), MaturityTier.Restricted18));
        Assert.False(MaturityRules.IsExplicit(null, descriptor));
    }

    // The guard TASK-101 left behind, restated from the descriptor side: the
    // four new tiers must not widen the adults-only gate.
    [Fact]
    public void Giving_the_other_descriptors_tiers_did_not_widen_the_explicit_gate()
    {
        Assert.Equal(
            [MaturityDescriptors.AdultOnlySexualContent],
            MaturityRules.ExplicitDescriptors);
        Assert.Equal(MaturityTier.AdultsOnly, MaturityRules.ExplicitTier);
    }

    // ── The cap and the 18+ toggle compose (AC4) ────────────────────────────

    [Fact]
    public void The_toggle_is_the_ceiling_of_the_cap()
    {
        // Toggle off: the top step is unreachable, whatever the cap says.
        Assert.Equal(
            MaturityTier.Restricted18,
            (Base with { MaturityCap = MaturityTier.AdultsOnly }).EffectiveMaturityCap);

        // Toggle on: the cap stands as chosen.
        Assert.Equal(
            MaturityTier.AdultsOnly,
            (Base with { ShowExplicitContent = true, MaturityCap = MaturityTier.AdultsOnly })
                .EffectiveMaturityCap);

        // A cap below the ceiling is never raised by the toggle.
        Assert.Equal(
            MaturityTier.Teen,
            (Base with { MaturityCap = MaturityTier.Teen }).EffectiveMaturityCap);
        Assert.Equal(
            MaturityTier.Teen,
            (Base with { ShowExplicitContent = true, MaturityCap = MaturityTier.Teen })
                .EffectiveMaturityCap);
    }

    [Fact]
    public void The_clamp_is_reported_only_when_the_toggle_is_actually_holding_the_cap_down()
    {
        Assert.True(BucketThresholds.IsCapClampedByAdultSetting(
            MaturityTier.AdultsOnly, showExplicitContent: false));
        Assert.False(BucketThresholds.IsCapClampedByAdultSetting(
            MaturityTier.AdultsOnly, showExplicitContent: true));
        Assert.False(BucketThresholds.IsCapClampedByAdultSetting(
            MaturityTier.Restricted18, showExplicitContent: false));
    }

    [Fact]
    public async Task An_adult_game_is_hidden_by_the_toggle_and_by_a_low_cap_alike()
    {
        var (adult, _, _) = await SeedGameAsync("Nameless Game");
        var (plain, _, _) = await SeedGameAsync("Outer Wilds");
        await RateAsync(adult, MaturityRatingCodes.EsrbAdultsOnly);

        // Toggle off, no cap: hidden by the toggle.
        Assert.Equal([plain], await VisibleAsync(Base));

        // Toggle on, no cap: shown.
        Assert.Equal(
            [adult, plain],
            (await VisibleAsync(Base with { ShowExplicitContent = true })).Order());

        // Toggle on, cap at Teen: hidden by the cap instead.
        Assert.Equal(
            [plain],
            await VisibleAsync(Base with
            {
                ShowExplicitContent = true,
                MaturityCap = MaturityTier.Teen,
            }));

        // Both restrictive: still hidden, and the two do not fight.
        Assert.Equal([plain], await VisibleAsync(Base with { MaturityCap = MaturityTier.Teen }));
    }

    [Fact]
    public async Task The_cap_hides_a_game_the_toggle_never_touches()
    {
        var (violent, _, _) = await SeedGameAsync("Nameless Game");
        var (gentle, _, _) = await SeedGameAsync("Outer Wilds");
        await RateAsync(violent, MaturityRatingCodes.Pegi18);

        // The toggle is off and the game is still shown: pegi:18 is not adult.
        Assert.Equal(
            [violent, gentle],
            (await VisibleAsync(Base)).Order());
        Assert.Equal(0, await _library.CountHiddenByExplicitFilterAsync(Base));

        // A Mature cap is what catches it.
        var capped = Base with { MaturityCap = MaturityTier.Mature };
        Assert.Equal([gentle], await VisibleAsync(capped));
        Assert.Equal(1, await _library.CountHiddenByRatingCapAsync(capped));
    }

    [Fact]
    public async Task The_cap_count_excludes_games_the_toggle_is_already_hiding()
    {
        var (adult, _, _) = await SeedGameAsync("Nameless Game");
        var (violent, _, _) = await SeedGameAsync("Outer Wilds");
        await RateAsync(adult, MaturityRatingCodes.EsrbAdultsOnly);
        await RateAsync(violent, MaturityRatingCodes.Pegi18);

        // Toggle off, cap at Teen. Two games are off screen, but only one of
        // them is the cap's doing — the other was gone before the cap spoke.
        var capped = Base with { MaturityCap = MaturityTier.Teen };
        Assert.Empty(await VisibleAsync(capped));
        Assert.Equal(1, await _library.CountHiddenByRatingCapAsync(capped));
        Assert.Equal(1, await _library.CountHiddenByExplicitFilterAsync(Base));
    }

    [Fact]
    public async Task No_cap_hides_nothing_and_counts_nothing()
    {
        var (violent, _, _) = await SeedGameAsync("Nameless Game");
        await RateAsync(violent, MaturityRatingCodes.Pegi18);

        Assert.Equal([violent], await VisibleAsync(Base));
        Assert.Equal(0, await _library.CountHiddenByRatingCapAsync(Base));
    }

    // ── The cap over the library (AC2, AC3) ─────────────────────────────────

    [Fact]
    public async Task A_game_with_no_maturity_evidence_survives_the_lowest_cap()
    {
        var (unknown, _, _) = await SeedGameAsync("Nameless Game");

        foreach (var cap in MaturityTiers.Ordered)
        {
            Assert.Equal([unknown], await VisibleAsync(Base with { MaturityCap = cap }));
        }
    }

    [Fact]
    public async Task Each_step_of_the_cap_admits_exactly_the_tiers_at_or_below_it()
    {
        var (everyone, _, _) = await SeedGameAsync("Everyone Game");
        var (teen, _, _) = await SeedGameAsync("Teen Game");
        var (mature, _, _) = await SeedGameAsync("Mature Game");
        var (restricted, _, _) = await SeedGameAsync("Restricted Game");

        await RateAsync(everyone, "esrb:e");
        await RateAsync(teen, "esrb:t");
        await RateAsync(mature, MaturityRatingCodes.EsrbMature);
        await RateAsync(restricted, MaturityRatingCodes.Pegi18);

        Assert.Equal(
            [everyone],
            (await VisibleAsync(Base with { MaturityCap = MaturityTier.Everyone })).Order());
        Assert.Equal(
            [everyone, teen],
            (await VisibleAsync(Base with { MaturityCap = MaturityTier.Teen })).Order());
        Assert.Equal(
            [everyone, teen, mature],
            (await VisibleAsync(Base with { MaturityCap = MaturityTier.Mature })).Order());
        Assert.Equal(
            [everyone, teen, mature, restricted],
            (await VisibleAsync(Base with { MaturityCap = MaturityTier.Restricted18 })).Order());
    }

    [Fact]
    public async Task The_highest_token_decides_when_two_sources_disagree()
    {
        var (workId, _, _) = await SeedGameAsync("Nameless Game");

        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.Igdb,
            Ratings = "esrb:t",
            ObservedAt = Observed,
        });
        await _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.SteamStore,
            Descriptors = MaturityDescriptors.FrequentNudityOrSexualContent,
            ObservedAt = Observed,
        });

        Assert.Empty(await VisibleAsync(Base with { MaturityCap = MaturityTier.Teen }));
        Assert.Equal([workId], await VisibleAsync(Base with { MaturityCap = MaturityTier.Restricted18 }));
    }

    [Fact]
    public async Task The_cap_takes_the_whole_linked_game_not_one_store_entry()
    {
        var (parent, _, _) = await SeedGameAsync("Nameless Game", store: "steam");
        var (child, _, _) = await SeedGameAsync("Nameless Game", store: "gog");

        await new IdentityLinkRepository(_db.Factory).LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parent,
            ChildWorkIds = [child],
        });

        await RateAsync(child, MaturityRatingCodes.Pegi18);

        Assert.Empty(await _library.GetOwnershipBucketsAsync(
            Base with { MaturityCap = MaturityTier.Mature }));
        Assert.Equal(2, (await _library.GetOwnershipBucketsAsync(Base)).Count);
    }

    [Fact]
    public async Task A_variant_is_capped_with_the_parent_it_samples()
    {
        var (parent, _, _) = await SeedGameAsync("Nameless Game");
        var (variant, _, _) = await SeedGameAsync("Unrelated Trial");

        await new IdentityLinkRepository(_db.Factory).LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parent,
            ChildWorkIds = [variant],
            Kind = IdentityLinkKinds.VariantOf,
        });

        await RateAsync(parent, MaturityRatingCodes.Pegi18);

        Assert.Empty(await _library.GetOwnershipBucketsAsync(
            Base with { MaturityCap = MaturityTier.Mature }));
    }

    // The rows the grid draws, the rows the list view draws and the rows every
    // rail count is computed from are one result set, so capping it caps all
    // three at once. This is the whole reason the cap lives in the bucket query.
    [Fact]
    public async Task The_capped_rows_are_the_rows_every_count_is_taken_from()
    {
        var (shown, _, _) = await SeedGameAsync("Outer Wilds");
        var (hidden, _, _) = await SeedGameAsync("Nameless Game");
        await RateAsync(hidden, MaturityRatingCodes.Pegi18);

        var rows = await _library.GetOwnershipBucketsAsync(
            Base with { MaturityCap = MaturityTier.Mature });

        Assert.Equal([shown], rows.Select(r => r.ResolvedWorkId));
        Assert.DoesNotContain(hidden, rows.Select(r => r.ResolvedWorkId));
        Assert.All(rows, row => Assert.False(string.IsNullOrEmpty(row.Bucket)));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<long>> VisibleAsync(BucketThresholds thresholds)
        => [.. (await _library.GetOwnershipBucketsAsync(thresholds)).Select(r => r.ResolvedWorkId)];

    private Task RateAsync(long workId, string ratings)
        => _maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.Igdb,
            Ratings = ratings,
            ObservedAt = Observed,
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
