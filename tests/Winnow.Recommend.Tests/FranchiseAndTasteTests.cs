using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// Unit tests for the pure internals behind the shelf diversity cap and the
/// taste profile's prevalence cut — the pieces whose behaviour is arithmetic
/// and should be pinned to the decimal, without a database.
/// </summary>
public class FranchiseAndTasteTests
{
    [Theory]
    [InlineData("Half-Life 2: Deathmatch", "half_life")]
    [InlineData("Half-Life: Alyx", "half_life")]
    [InlineData("Left 4 Dead 2", "left_4_dead")]
    [InlineData("Sid Meier's Civilization IV: Colonization", "sid_meier_s_civilization")]
    [InlineData("Sid Meier's Civilization V", "sid_meier_s_civilization")]
    [InlineData("Batman™: Arkham Origins", "batman")]
    [InlineData("Infinity Blade: Awakening", "infinity_blade")]
    [InlineData("DiRT 4", "dirt")]
    [InlineData("Mega Man X", "mega_man")]
    public void Franchise_keys_group_the_families_the_measured_library_actually_has(
        string title, string expected)
        => Assert.Equal(expected, Franchise.KeyFor(title));

    [Theory]
    [InlineData("Portal")] // single token: never trimmed, even though it could look like anything
    [InlineData("V Rising")] // trailing token is a real word, not a numeral
    [InlineData("Mix")] // 'm' keeps it off the roman-numeral fold
    public void Titles_without_a_sequel_shape_keep_their_own_key(string title)
    {
        var key = Franchise.KeyFor(title);
        Assert.Equal(Facet.Slugify(title), key);
    }

    [Fact]
    public void Distinct_franchises_do_not_collide()
    {
        Assert.NotEqual(Franchise.KeyFor("Saga: Alpha"), Franchise.KeyFor("Sago: Alpha"));
        Assert.NotEqual(Franchise.KeyFor("Portal 2"), Franchise.KeyFor("Portal Knights"));
    }

    // ── The prevalence cut ─────────────────────────────────────────────────

    private static FacetSnapshot Snapshot(params (long ReleaseId, long[] FacetIds)[] releases)
    {
        var facets = releases
            .SelectMany(r => r.FacetIds)
            .Distinct()
            .Select(id => new Facet(id, FacetKinds.Genre, $"genre_{id}", $"Genre {id}"))
            .ToList();
        return new FacetSnapshot
        {
            Facets = facets,
            Releases = releases
                .Select(r => new ReleaseFacets(r.ReleaseId, r.FacetIds, []))
                .ToList(),
        };
    }

    private static OwnershipBucket Row(long releaseId, long minutes)
    {
        var row = new OwnershipBucket
        {
            OwnershipId = releaseId,
            ReleaseId = releaseId,
            // Nothing linked: a work resolves to itself, which is the pre-link
            // answer and the one this fixture is about.
            WorkId = releaseId,
            ResolvedWorkId = releaseId,
            PlaytimeMinutes = minutes,
            Bucket = minutes >= 120 ? LibraryBuckets.Bounced : LibraryBuckets.NeverPlayed,
            Game = null!,
        };

        // One entry, so the game IS the row and the shared rules put it in the
        // same bucket the row names.
        return row with { Game = GameGrouping.Of(releaseId, [row], null, BucketThresholds.Default) };
    }

    [Fact]
    public void A_facet_carried_by_most_of_the_library_stops_counting_as_taste()
    {
        // Facet 1 sits on every release (the library's furniture — measured
        // reality: "Action" rides two-thirds of the real library and
        // saturated the affinity metric). Facet 2 is distinctive. The floor
        // is lowered so a five-row fixture can trip the share.
        var tuning = new RecommendationTuning
        {
            TasteFacetPrevalenceFloor = 2,
            TasteFacetMaxPrevalence = 0.5,
        };
        var snapshot = Snapshot(
            (1, [1L, 2L]),
            (2, [1L]),
            (3, [1L]),
            (4, [1L]),
            (5, [1L, 2L]));
        var rows = new[] { Row(1, 3_000), Row(2, 1_000), Row(5, 0) };

        var profile = TasteProfile.Build(rows, snapshot, BucketThresholds.Default, tuning);

        // Release 5 shares BOTH facets with the committed rows; only the
        // distinctive one may testify, and it names itself.
        var (affinity, facetName) = profile.AffinityFor(5);
        Assert.Equal(1.0, affinity!.Value, 3);
        Assert.Equal("Genre 2", facetName);

        // Release 3 carries only the furniture facet: no affinity at all —
        // "matches your taste" via a descriptor half the library wears would
        // be a sentence measuring nothing.
        Assert.Equal((null, null), profile.AffinityFor(3));
    }

    [Fact]
    public void The_prevalence_floor_protects_small_libraries()
    {
        // Same shape, default tuning: five carriers is far under the floor of
        // eight, so nothing is generic and the shared facet still testifies —
        // in a 20-game library, five carriers of one genre is a small
        // collection, not genericity.
        var snapshot = Snapshot(
            (1, [1L]),
            (2, [1L]),
            (3, [1L]),
            (4, [1L]),
            (5, [1L]));
        var rows = new[] { Row(1, 3_000), Row(5, 0) };

        var profile = TasteProfile.Build(
            rows, snapshot, BucketThresholds.Default, RecommendationTuning.Default);

        var (affinity, _) = profile.AffinityFor(5);
        Assert.Equal(1.0, affinity!.Value, 3);
    }
}
