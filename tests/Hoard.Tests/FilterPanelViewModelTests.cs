using System.Globalization;
using Hoard.App.ViewModels;
using Hoard.App.ViewModels.Filters;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The filter panel and the cut bar above the grid.
///
/// <para>Three things are worth pinning down and everything else follows from
/// them. <b>Composition:</b> options inside a group widen the result and groups
/// narrow it — get that backwards and every count in the panel is a lie.
/// <b>Residual counts:</b> the number beside an option is what you would get if
/// you ticked it, taken with every OTHER group applied and this group's own
/// selections lifted. <b>The rail is part of the filter:</b> the bucket is an
/// AND term like any other, appears in the cut bar as a chip, and is saved into
/// a live list.</para>
///
/// <para>Like the other view-model tests these run against a real migrated
/// SQLite file and the real repositories; no Avalonia application, dispatcher or
/// rendering is involved.</para>
/// </summary>
public sealed class FilterPanelViewModelTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    // ── Composition ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_options_in_one_group_widen_the_result()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);
        await fixture.SeedAsync("Hades", genres: ["Action"]);
        await fixture.SeedAsync("Civilization VI", genres: ["Strategy"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");

        Assert.Equal(["Disco Elysium"], fixture.Titles(library));

        fixture.Check(library, FilterPanelViewModel.GenreKey, "Action");

        Assert.Equal(["Disco Elysium", "Hades"], fixture.Titles(library).Order());
    }

    [Fact]
    public async Task Two_groups_narrow_the_result()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Deep Rock Galactic", genres: ["Shooter"], modes: [GameModes.CoOperative]);
        await fixture.SeedAsync("DOOM", genres: ["Shooter"], modes: [GameModes.SinglePlayer]);
        await fixture.SeedAsync("Overcooked", genres: ["Puzzle"], modes: [GameModes.CoOperative]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "Shooter");
        fixture.CheckMode(library, GameModes.CoOperative);

        Assert.Equal(["Deep Rock Galactic"], fixture.Titles(library));
    }

    [Fact]
    public async Task The_rail_bucket_is_one_more_and_term()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Bounced shooter", minutes: 300, genres: ["Shooter"]);
        await fixture.SeedAsync("Unplayed shooter", minutes: 0, genres: ["Shooter"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "Shooter");
        Assert.Equal(2, library.VisibleTiles.Count);

        library.SelectBucketCommand.Execute(
            library.Buckets.Single(b => b.Key == LibraryBuckets.Bounced));

        Assert.Equal(["Bounced shooter"], fixture.Titles(library));
    }

    [Fact]
    public async Task The_year_range_excludes_a_release_whose_year_is_unknown()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Dated", year: 2015);
        await fixture.SeedAsync("Undated");

        var library = await fixture.LoadAsync();
        library.Filters.YearFromText = "2010";
        library.Filters.YearToText = "2020";

        Assert.Equal(["Dated"], fixture.Titles(library));
    }

    // ── Residual counts ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_count_is_what_the_option_would_leave_you_with()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Co-op RPG", genres: ["RPG"], modes: [GameModes.CoOperative]);
        await fixture.SeedAsync("Solo RPG", genres: ["RPG"], modes: [GameModes.SinglePlayer]);
        await fixture.SeedAsync("Solo shooter", genres: ["Shooter"], modes: [GameModes.SinglePlayer]);

        var library = await fixture.LoadAsync();
        Assert.Equal(2, fixture.Count(library, FilterPanelViewModel.GenreKey, "RPG"));

        // With single player in force, "RPG" is worth one title, not two.
        fixture.CheckMode(library, GameModes.SinglePlayer);

        Assert.Equal(1, fixture.Count(library, FilterPanelViewModel.GenreKey, "RPG"));
        Assert.Equal(1, fixture.Count(library, FilterPanelViewModel.GenreKey, "Shooter"));
    }

    [Fact]
    public async Task A_groups_own_selection_does_not_zero_its_siblings()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);
        await fixture.SeedAsync("Hades", genres: ["Action"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");

        // The whole reason a residual count lifts its own group: otherwise
        // ticking one genre reads as "every other genre owns nothing", and the
        // panel becomes a dead end after a single click.
        Assert.Equal(1, fixture.Count(library, FilterPanelViewModel.GenreKey, "Action"));
        Assert.Equal(1, fixture.Count(library, FilterPanelViewModel.GenreKey, "RPG"));
    }

    [Fact]
    public async Task An_option_that_would_empty_the_grid_says_so_and_stops_being_a_target()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Co-op RPG", genres: ["RPG"], modes: [GameModes.CoOperative]);
        await fixture.SeedAsync("Solo shooter", genres: ["Shooter"], modes: [GameModes.SinglePlayer]);

        var library = await fixture.LoadAsync();
        fixture.CheckMode(library, GameModes.CoOperative);

        var shooter = fixture.Option(library, FilterPanelViewModel.GenreKey, "Shooter");
        Assert.Equal(0, shooter.Count);
        Assert.False(shooter.IsAvailable);

        // A ticked option stays live whatever its count says: the way out of an
        // empty result has to be the control that caused it.
        var rpg = fixture.Option(library, FilterPanelViewModel.GenreKey, "RPG");
        rpg.IsChecked = true;
        fixture.Mode(library, GameModes.CoOperative).IsChecked = false;
        fixture.CheckMode(library, GameModes.SinglePlayer);

        Assert.Empty(library.VisibleTiles);
        Assert.Equal(0, rpg.Count);
        Assert.True(rpg.IsAvailable);
    }

    [Fact]
    public async Task A_dimension_with_no_data_draws_no_group()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Untagged");

        var library = await fixture.LoadAsync();

        Assert.DoesNotContain(library.Filters.VisibleGroups, g => g.Key == FilterPanelViewModel.GenreKey);
        Assert.DoesNotContain(library.Filters.VisibleGroups, g => g.Key == FilterPanelViewModel.TagKey);
        Assert.False(library.Filters.HasDescriptorGroups);

        // ON DISK survives: both of its options always exist, so it can always
        // cut. STORE does not, because its one option is true of every title —
        // "Steam 926" beside a 926-title library is a fact restated as a control
        // that cannot change anything.
        Assert.Contains(library.Filters.VisibleGroups, g => g.Key == FilterPanelViewModel.InstalledKey);
        Assert.DoesNotContain(library.Filters.VisibleGroups, g => g.Key == FilterPanelViewModel.StoreKey);
    }

    [Fact]
    public async Task A_store_group_appears_as_soon_as_a_second_store_does()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Hades");
        await fixture.SeedAsync("Cyberpunk 2077", store: "gog");

        var library = await fixture.LoadAsync();

        Assert.Contains(library.Filters.VisibleGroups, g => g.Key == FilterPanelViewModel.StoreKey);
        Assert.Equal(
            ["GOG", "Steam"],
            library.Filters.Groups.Single(g => g.Key == FilterPanelViewModel.StoreKey)
                .Options.Select(o => o.Label));
    }

    // ── The cut bar ─────────────────────────────────────────────────────────

    [Fact]
    public async Task The_cut_bar_states_the_library_and_what_is_left_of_it()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);
        await fixture.SeedAsync("Hades", genres: ["Action"]);
        await fixture.SeedAsync("Celeste", genres: ["Platformer"]);

        var library = await fixture.LoadAsync();
        Assert.False(library.IsCut);

        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");

        Assert.True(library.IsCut);
        Assert.Equal("3 → 1", library.CutText);
        Assert.Equal("1", library.VisibleCountText);
    }

    [Fact]
    public async Task Every_rule_is_a_chip_and_every_chip_takes_itself_off()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Disco Elysium", minutes: 300, genres: ["RPG"]);
        await fixture.SeedAsync("Hades", minutes: 300, genres: ["Action"]);

        var library = await fixture.LoadAsync();
        library.SelectBucketCommand.Execute(
            library.Buckets.Single(b => b.Key == LibraryBuckets.Bounced));
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");

        // The rail's bucket leads, because it is the pile you are standing in
        // and this is the one place the rail and the panel are stated as one
        // filter.
        Assert.Equal(["Bounced off", "RPG"], library.CutChips.Select(c => c.Label));
        Assert.Equal("BUCKET", library.CutChips[0].Dimension);
        Assert.Equal("GENRE", library.CutChips[1].Dimension);

        library.CutChips[1].RemoveCommand.Execute(null);
        Assert.Equal(2, library.VisibleTiles.Count);

        library.CutChips.Single().RemoveCommand.Execute(null);
        Assert.Null(library.SelectedBucket);
        Assert.False(library.IsCut);
    }

    [Fact]
    public async Task Clearing_the_panel_leaves_the_rail_where_it_was()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Disco Elysium", minutes: 300, genres: ["RPG"]);
        await fixture.SeedAsync("Hades", minutes: 300, genres: ["Action"]);

        var library = await fixture.LoadAsync();
        library.SelectBucketCommand.Execute(
            library.Buckets.Single(b => b.Key == LibraryBuckets.Bounced));
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");

        library.Filters.ClearCommand.Execute(null);

        Assert.False(library.Filters.HasSelection);
        Assert.NotNull(library.SelectedBucket);
        Assert.Equal(2, library.VisibleTiles.Count);
    }

    [Fact]
    public async Task An_empty_result_from_a_filter_is_a_direction()
    {
        using var fixture = new PanelFixture();
        await fixture.SeedAsync("Co-op RPG", genres: ["RPG"], modes: [GameModes.CoOperative]);
        await fixture.SeedAsync("Solo shooter", genres: ["Shooter"], modes: [GameModes.SinglePlayer]);

        var library = await fixture.LoadAsync();

        // Reached through the controls rather than by writing the filter state,
        // so the message is the one the user would actually see. "Shooter" is
        // ticked while its residual count is already zero, which is exactly the
        // case that has to stay clickable.
        fixture.CheckMode(library, GameModes.CoOperative);
        fixture.Check(library, FilterPanelViewModel.GenreKey, "Shooter");

        Assert.Empty(library.VisibleTiles);
        Assert.Equal("No titles match these filters. Drop one to widen the cut.", library.EmptyMessage);
    }

    /// <summary>
    /// Seeds a library with facets and hands back a wired-up
    /// <see cref="LibraryViewModel"/>.
    /// </summary>
    private sealed class PanelFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 810000;

        public PanelFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Ownerships = new OwnershipRepository(_db.Factory);
            Plays = new PlayRecordRepository(_db.Factory);
            Updates = new UpdateEventRepository(_db.Factory);
            Queries = new LibraryQueryRepository(_db.Factory);
            Facets = new FacetRepository(_db.Factory);
            GameLists = new GameListRepository(_db.Factory);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IOwnershipRepository Ownerships { get; }

        public IPlayRecordRepository Plays { get; }

        public IUpdateEventRepository Updates { get; }

        public ILibraryQueryRepository Queries { get; }

        public IFacetRepository Facets { get; }

        public IGameListRepository GameLists { get; }

        public LibraryViewModel Create()
            => new(Queries, Ownerships, Releases, Works, Updates,
                covers: null, ramp: null, snapshots: null,
                facets: Facets, lists: GameLists);

        public async Task<LibraryViewModel> LoadAsync()
        {
            var library = Create();
            await library.LoadCommand.ExecuteAsync(null);
            return library;
        }

        public IEnumerable<string> Titles(LibraryViewModel library)
            => library.VisibleTiles.Select(t => t.Title);

        public FilterOptionViewModel Option(LibraryViewModel library, string group, string label)
            => library.Filters.Groups
                .Single(g => g.Key == group)
                .AllOptions.Single(o => o.Label == label);

        public void Check(LibraryViewModel library, string group, string label)
            => Option(library, group, label).IsChecked = true;

        /// <summary>By slug, not by label: migration 0007 owns the display name.</summary>
        public FilterOptionViewModel Mode(LibraryViewModel library, string slug)
            => library.Filters.Groups
                .Single(g => g.Key == FilterPanelViewModel.ModeKey)
                .AllOptions.Single(o => o.Key == slug);

        public void CheckMode(LibraryViewModel library, string slug)
            => Mode(library, slug).IsChecked = true;

        public int Count(LibraryViewModel library, string group, string label)
            => Option(library, group, label).Count;

        public async Task<long> SeedAsync(
            string title,
            long minutes = 0,
            DateTime? lastPlayed = null,
            int? year = null,
            bool installed = false,
            string store = "steam",
            string[]? genres = null,
            string[]? tags = null,
            string[]? modes = null)
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = title,
                FirstReleaseYear = year,
            });

            var releaseId = await Releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = title,
                Platform = "windows",
            });

            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = (++_appId).ToString(CultureInfo.InvariantCulture),
            });

            var ownershipId = await Ownerships.InsertAsync(new Ownership
            {
                ReleaseId = releaseId,
                Store = store,
                Installed = installed,
            });

            await Plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = minutes,
                LastPlayedAt = lastPlayed,
                Source = "steam_localconfig",
                ObservedAt = Now,
            });

            // Genres and game modes are facts about the Work; store tags belong
            // to one appid and go on the Release. Same split the backfill uses.
            List<FacetAssignment> work = [];
            foreach (var genre in genres ?? [])
            {
                work.Add(new FacetAssignment(FacetKinds.Genre, genre));
            }

            foreach (var mode in modes ?? [])
            {
                work.Add(new FacetAssignment(FacetKinds.GameMode, ModeName(mode)));
            }

            if (work.Count > 0)
            {
                await Facets.SetWorkFacetsAsync(workId, work);
            }

            if (tags is { Length: > 0 })
            {
                await Facets.SetReleaseFacetsAsync(
                    releaseId,
                    [.. tags.Select((t, i) => new FacetAssignment(FacetKinds.Tag, t, i + 1))]);
            }

            return releaseId;
        }

        /// <summary>Migration 0007 seeded the game-mode vocabulary by slug; this reverses it.</summary>
        private static string ModeName(string slug) => slug switch
        {
            GameModes.SinglePlayer => "Single player",
            GameModes.Multiplayer => "Multiplayer",
            GameModes.CoOperative => "Co-operative",
            GameModes.SplitScreen => "Split screen",
            GameModes.Mmo => "MMO",
            GameModes.BattleRoyale => "Battle royale",
            _ => slug,
        };

        public void Dispose() => _db.Dispose();
    }
}
