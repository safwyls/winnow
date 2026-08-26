using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// Migration 0007's three tables through <see cref="FacetRepository"/>.
///
/// <para>The facts under test are the ones the schema's comments claim: facets
/// are keyed on the NAME (so Valve's duplicate display names collapse), the
/// vocabulary is insert-only (so a saved live list keeps meaning what it meant),
/// tag RANK survives the round trip, and the two layers of §6's identity model
/// stay separate in storage while being unioned on read.</para>
/// </summary>
public class FacetRepositoryTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly FacetRepository _facets;
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;

    public FacetRepositoryTests()
    {
        _facets = new FacetRepository(_db.Factory);
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// The seed migration 0007 writes: Hoard's own game-mode vocabulary, with
    /// fixed ids, present in every database before anything is fetched.
    /// </summary>
    [Fact]
    public async Task Game_modes_are_seeded_by_the_migration()
    {
        var vocabulary = await _facets.GetVocabularyAsync();
        var modes = vocabulary.Where(f => f.Kind == FacetKinds.GameMode).ToArray();

        Assert.Equal(GameModes.All.Count, modes.Length);
        Assert.Equal(
            GameModes.All.Order(StringComparer.Ordinal),
            modes.Select(m => m.Slug).Order(StringComparer.Ordinal));

        // Fixed ids, so the seeded rows read the same in every database.
        Assert.Equal(1, modes.Single(m => m.Slug == GameModes.SinglePlayer).Id);
        Assert.Equal("Single-player", modes.Single(m => m.Slug == GameModes.SinglePlayer).Name);
    }

    [Fact]
    public async Task Mints_a_facet_on_first_sight_and_reuses_it_after()
    {
        var (workA, _) = await SeedAsync("Skyrim");
        var (workB, _) = await SeedAsync("Oblivion");

        await _facets.SetWorkFacetsAsync(workA, [new FacetAssignment(FacetKinds.Genre, "Role-playing (RPG)")]);
        await _facets.SetWorkFacetsAsync(workB, [new FacetAssignment(FacetKinds.Genre, "Role-playing (RPG)")]);

        var rpg = Assert.Single(
            await _facets.GetVocabularyAsync(),
            f => f.Kind == FacetKinds.Genre);

        Assert.Equal("Role-playing (RPG)", rpg.Name);
        Assert.Equal("role_playing_rpg", rpg.Slug);
    }

    /// <summary>
    /// Valve ships ids 55 and 56 with the same display name. Keyed on the name,
    /// they become one checkbox — which is the answer a filter panel wants, and
    /// the reason 0007 does not key on the provider's id.
    /// </summary>
    [Fact]
    public async Task Two_names_that_slugify_alike_become_one_facet()
    {
        var (_, release) = await SeedAsync("Elden Ring");

        await _facets.SetReleaseFacetsAsync(release, [
            new FacetAssignment(FacetKinds.Controller, "DualShock Controller Support"),
            new FacetAssignment(FacetKinds.Controller, "DualShock  Controller  Support"),
        ]);

        var controllers = (await _facets.GetVocabularyAsync())
            .Where(f => f.Kind == FacetKinds.Controller)
            .ToArray();

        Assert.Single(controllers);
        Assert.Single((await _facets.GetSnapshotAsync()).ByRelease[release].FacetIds);
    }

    /// <summary>Different kinds are different namespaces: "Action" is a genre and a theme.</summary>
    [Fact]
    public async Task The_same_name_under_two_kinds_is_two_facets()
    {
        var (work, _) = await SeedAsync("Thief II");

        await _facets.SetWorkFacetsAsync(work, [
            new FacetAssignment(FacetKinds.Genre, "Action"),
            new FacetAssignment(FacetKinds.Theme, "Action"),
        ]);

        Assert.Equal(2, (await _facets.GetVocabularyAsync()).Count(f => f.Name == "Action"));
    }

    [Fact]
    public async Task Tag_rank_ordering_is_preserved()
    {
        var (_, release) = await SeedAsync("Elden Ring");

        // Handed over out of order on purpose: rank is the stored fact, the
        // order of the list is not.
        await _facets.SetReleaseFacetsAsync(release, [
            new FacetAssignment(FacetKinds.Tag, "RPG", 4),
            new FacetAssignment(FacetKinds.Tag, "Souls-like", 1),
            new FacetAssignment(FacetKinds.Tag, "Dark Fantasy", 3),
            new FacetAssignment(FacetKinds.Tag, "Open World", 2),
        ]);

        var snapshot = await _facets.GetSnapshotAsync();
        var names = snapshot.ByRelease[release].FacetIds
            .Select(id => snapshot.ById[id].Name)
            .ToArray();

        Assert.Equal(["Souls-like", "Open World", "Dark Fantasy", "RPG"], names);
    }

    /// <summary>
    /// The two layers stay apart in storage — genres on the work, tags on the
    /// release — and are unioned only when a caller asks what is true of one
    /// tile. Two releases of the same work therefore share its genres and keep
    /// their own tags, which is exactly the Skyrim / Skyrim Special Edition case
    /// §6 exists to model.
    /// </summary>
    [Fact]
    public async Task Work_facets_reach_every_release_and_release_facets_stay_put()
    {
        var workId = await _works.InsertAsync(new Work { Name = "Skyrim" });
        var classic = await _releases.InsertAsync(new Release { WorkId = workId, Name = "Skyrim" });
        var special = await _releases.InsertAsync(new Release { WorkId = workId, Name = "Skyrim SE" });

        await _facets.SetWorkFacetsAsync(workId, [new FacetAssignment(FacetKinds.Genre, "RPG")]);
        await _facets.SetReleaseFacetsAsync(special, [new FacetAssignment(FacetKinds.Tag, "Modded", 1)]);

        var snapshot = await _facets.GetSnapshotAsync();
        var rpg = snapshot.Facets.Single(f => f.Kind == FacetKinds.Genre).Id;
        var modded = snapshot.Facets.Single(f => f.Kind == FacetKinds.Tag).Id;

        Assert.Contains(rpg, snapshot.ByRelease[classic].FacetIds);
        Assert.Contains(rpg, snapshot.ByRelease[special].FacetIds);
        Assert.DoesNotContain(modded, snapshot.ByRelease[classic].FacetIds);
        Assert.Contains(modded, snapshot.ByRelease[special].FacetIds);
    }

    /// <summary>
    /// The one facet both providers write. It must appear ONCE on a release that
    /// got it from both, which is what the reader's UNION (rather than UNION ALL)
    /// buys.
    /// </summary>
    [Fact]
    public async Task A_game_mode_written_at_both_layers_appears_once()
    {
        var (work, release) = await SeedAsync("Portal 2");

        await _facets.SetWorkFacetsAsync(work, [GameModes.Assignment(GameModes.CoOperative)]);
        await _facets.SetReleaseFacetsAsync(release, [GameModes.Assignment(GameModes.CoOperative)]);

        var facets = (await _facets.GetSnapshotAsync()).ByRelease[release];

        Assert.Single(facets.FacetIds);
        Assert.Equal([GameModes.CoOperative], facets.GameModes);
    }

    /// <summary>
    /// The closed vocabulary stays closed. An assignment built from the display
    /// name rather than the slug lands on nothing — "Co-op" folds to
    /// <c>co_op</c>, which is not this vocabulary's key — and it must be dropped
    /// rather than minting a seventh game mode beside the six 0007 seeded, which
    /// would split the count and quietly orphan every saved filter pointing at
    /// the original.
    /// </summary>
    [Fact]
    public async Task A_game_mode_that_is_not_one_of_the_six_is_dropped()
    {
        var (work, _) = await SeedAsync("Portal 2");

        await _facets.SetWorkFacetsAsync(work, [
            new FacetAssignment(FacetKinds.GameMode, "Co-op"),
            new FacetAssignment(FacetKinds.GameMode, "Asymmetric VR"),
        ]);

        Assert.Equal(GameModes.All.Count, (await _facets.GetVocabularyAsync())
            .Count(f => f.Kind == FacetKinds.GameMode));
        Assert.Empty((await _facets.GetSnapshotAsync()).Releases);
    }

    /// <summary>
    /// A re-write of the same set reports zero. This is what makes the backfill
    /// free on a warm run rather than merely harmless.
    /// </summary>
    [Fact]
    public async Task Rewriting_an_unchanged_set_writes_nothing()
    {
        var (work, release) = await SeedAsync("Hades");

        var genres = new[] { new FacetAssignment(FacetKinds.Genre, "Indie") };
        var tags = new[] { new FacetAssignment(FacetKinds.Tag, "Roguelike", 1) };

        Assert.True(await _facets.SetWorkFacetsAsync(work, genres) > 0);
        Assert.True(await _facets.SetReleaseFacetsAsync(release, tags) > 0);

        Assert.Equal(0, await _facets.SetWorkFacetsAsync(work, genres));
        Assert.Equal(0, await _facets.SetReleaseFacetsAsync(release, tags));
    }

    [Fact]
    public async Task A_changed_rank_is_a_change()
    {
        var (_, release) = await SeedAsync("Hades");

        await _facets.SetReleaseFacetsAsync(release, [new FacetAssignment(FacetKinds.Tag, "Roguelike", 3)]);
        Assert.True(await _facets.SetReleaseFacetsAsync(release, [new FacetAssignment(FacetKinds.Tag, "Roguelike", 1)]) > 0);

        var snapshot = await _facets.GetSnapshotAsync();
        Assert.Single(snapshot.ByRelease[release].FacetIds);
    }

    [Fact]
    public async Task Replacing_a_set_drops_what_is_no_longer_there()
    {
        var (work, _) = await SeedAsync("Hades");

        await _facets.SetWorkFacetsAsync(work, [
            new FacetAssignment(FacetKinds.Genre, "Indie"),
            new FacetAssignment(FacetKinds.Genre, "Adventure"),
        ]);

        await _facets.SetWorkFacetsAsync(work, [new FacetAssignment(FacetKinds.Genre, "Indie")]);

        var snapshot = await _facets.GetSnapshotAsync();
        var facetId = Assert.Single(snapshot.Releases).FacetIds.Single();
        Assert.Equal("Indie", snapshot.ById[facetId].Name);

        // The dropped genre keeps its vocabulary row: those ids are what a saved
        // live list refers to, so the table is insert-only.
        Assert.Contains(await _facets.GetVocabularyAsync(), f => f.Name == "Adventure");
    }

    [Fact]
    public async Task A_blank_name_mints_nothing()
    {
        var (work, _) = await SeedAsync("Hades");

        await _facets.SetWorkFacetsAsync(work, [
            new FacetAssignment(FacetKinds.Genre, "   "),
            new FacetAssignment(FacetKinds.Genre, "!!!"),
        ]);

        Assert.DoesNotContain(await _facets.GetVocabularyAsync(), f => f.Kind == FacetKinds.Genre);
    }

    /// <summary>
    /// A release nothing is known about is absent from the snapshot — and that
    /// is all it is. Nothing here can remove a tile from the library; the bucket
    /// query decides which rows exist.
    /// </summary>
    [Fact]
    public async Task A_release_with_no_facets_is_simply_absent()
    {
        var (work, described) = await SeedAsync("Hades");
        var (_, undescribed) = await SeedAsync("Unknown App 12345");

        await _facets.SetWorkFacetsAsync(work, [new FacetAssignment(FacetKinds.Genre, "Indie")]);

        var snapshot = await _facets.GetSnapshotAsync();

        Assert.Contains(described, snapshot.ByRelease.Keys);
        Assert.DoesNotContain(undescribed, snapshot.ByRelease.Keys);
        Assert.Empty(ReleaseFacets.Empty(undescribed).FacetIds);
    }

    // -- counts --------------------------------------------------------------

    /// <summary>
    /// Counts are taken over the caller's set, so the checkbox and the grid can
    /// never disagree — the same rule the non-game filter is documented under.
    /// </summary>
    [Fact]
    public async Task Counts_are_taken_over_the_releases_the_caller_names()
    {
        var (workA, releaseA) = await SeedAsync("Hades");
        var (workB, releaseB) = await SeedAsync("Dead Cells");
        var (workC, releaseC) = await SeedAsync("Celeste");

        foreach (var work in new[] { workA, workB, workC })
        {
            await _facets.SetWorkFacetsAsync(work, [new FacetAssignment(FacetKinds.Genre, "Indie")]);
        }

        var snapshot = await _facets.GetSnapshotAsync();

        Assert.Equal(3, Assert.Single(snapshot.CountsFor([releaseA, releaseB, releaseC])).ReleaseCount);

        // Hide one — say it was consolidated as a demo — and the count follows.
        Assert.Equal(2, Assert.Single(snapshot.CountsFor([releaseA, releaseB])).ReleaseCount);
    }

    [Fact]
    public async Task Facets_nothing_carries_are_left_out_of_the_counts()
    {
        var (work, release) = await SeedAsync("Hades");
        await _facets.SetWorkFacetsAsync(work, [new FacetAssignment(FacetKinds.Genre, "Indie")]);

        var snapshot = await _facets.GetSnapshotAsync();
        var counts = snapshot.CountsFor([release]);

        // The six seeded game modes are in the vocabulary and carried by nothing.
        Assert.Equal(GameModes.All.Count + 1, snapshot.Facets.Count);
        Assert.Single(counts);
    }

    [Fact]
    public async Task Counts_are_ordered_by_kind_then_commonest_first()
    {
        var (workA, releaseA) = await SeedAsync("Hades");
        var (workB, releaseB) = await SeedAsync("Dead Cells");

        await _facets.SetWorkFacetsAsync(workA, [
            new FacetAssignment(FacetKinds.Genre, "Indie"),
            new FacetAssignment(FacetKinds.Genre, "Adventure"),
        ]);
        await _facets.SetWorkFacetsAsync(workB, [new FacetAssignment(FacetKinds.Genre, "Indie")]);
        await _facets.SetReleaseFacetsAsync(releaseA, [new FacetAssignment(FacetKinds.Tag, "Roguelike", 1)]);

        var counts = (await _facets.GetSnapshotAsync()).CountsFor([releaseA, releaseB]);

        Assert.Equal(
            [("Indie", 2), ("Adventure", 1), ("Roguelike", 1)],
            counts.Select(c => (c.Facet.Name, c.ReleaseCount)));
    }

    private async Task<(long WorkId, long ReleaseId)> SeedAsync(string name)
    {
        var workId = await _works.InsertAsync(new Work { Name = name });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
        return (workId, releaseId);
    }
}
