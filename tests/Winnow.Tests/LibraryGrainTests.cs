using System.Globalization;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Filters;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The library grid at its new grain (TASK-70.6): one tile per game, with a
/// chip for each store it is owned on. Every earlier stage of TASK-70 exists
/// so that this one could be made by resolving a link rather than by deleting
/// a row.
///
/// <para>The two claims that matter most are opposites. A library with nothing
/// linked must render exactly the grid it rendered before, tile for tile; and
/// a library with one link must render one tile fewer, with every count on
/// screen still agreeing with its own definition.</para>
/// </summary>
public sealed class LibraryGrainTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    // ── Nothing linked: nothing moves ───────────────────────────────────────

    /// <summary>
    /// The safety claim. With no links the grid is tile for tile the grid it
    /// was, including the case the collapse is about: one game bought on two
    /// stores, unlinked, is still two tiles because nobody has said they are
    /// the same game.
    /// </summary>
    [Fact]
    public async Task An_unlinked_library_is_tile_for_tile_the_library_it_was()
    {
        using var fixture = new GrainFixture();
        await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-400), store: "epic");
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Hades", minutes: 8_000, lastPlayed: Now.AddDays(-5), store: "gog");

        var rows = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        var library = await fixture.LoadAsync();

        Assert.Equal(rows.Count, library.VisibleTiles.Count);
        Assert.Equal(rows.Count, library.AllGames.Count);

        // Every tile is exactly one ownership, and it is the ownership the row
        // named. Nothing borrowed, nothing folded.
        Assert.Equal(
            rows.Select(r => r.OwnershipId).Order(),
            library.VisibleTiles.Select(t => t.OwnershipId).Order());
        Assert.All(library.VisibleTiles, tile => Assert.Single(tile.Entries));
        Assert.All(library.VisibleTiles, tile => Assert.Single(tile.StoreChips));
        Assert.All(library.VisibleTiles, tile => Assert.False(tile.IsMultiStore));

        // And each tile's headline is its own row's figures, not a sum of
        // anything.
        foreach (var row in rows)
        {
            var tile = library.VisibleTiles.Single(t => t.OwnershipId == row.OwnershipId);
            Assert.Equal(row.PlaytimeMinutes, tile.PlaytimeMinutes);
            Assert.Equal(row.LastPlayedAt, tile.LastPlayedUtc);
            Assert.Equal(row.Bucket, tile.Bucket);
        }
    }

    // ── One link: one tile fewer ────────────────────────────────────────────

    /// <summary>
    /// The fix the user asked for. The chips list every store the game is
    /// owned on, in entry order (primary first), and no more.
    /// </summary>
    [Fact]
    public async Task A_linked_pair_is_one_tile_whose_chips_are_exactly_its_stores()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-400), store: "epic");
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);

        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);
        var library = await fixture.LoadAsync();

        Assert.Equal(2, library.VisibleTiles.Count);

        var prey = library.VisibleTiles.Single(t => t.Title == "Prey");
        Assert.Equal(["steam", "epic"], prey.Stores);
        Assert.Equal(["STEAM", "EPIC"], prey.StoreChips);
        Assert.Equal(["S", "E"], prey.StoreInitials);
        Assert.True(prey.IsMultiStore);

        var dishonored = library.VisibleTiles.Single(t => t.Title == "Dishonored");
        Assert.Equal(["STEAM"], dishonored.StoreChips);
        Assert.False(dishonored.IsMultiStore);
    }

    /// <summary>
    /// A third store joins the same game. The chip row grows by one and gains
    /// nothing else; two entries on one store produce one chip, because the
    /// chip answers where the game can be reached, not how many licences are
    /// held.
    /// </summary>
    [Fact]
    public async Task Chips_list_every_store_and_never_one_store_twice()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var second = await fixture.SeedAsync("Prey Classic", minutes: 10, lastPlayed: null);
        var gog = await fixture.SeedAsync("Prey (GOG)", minutes: 5, lastPlayed: null, store: "gog");

        await fixture.LinkAsync(parent: steam.WorkId, child: second.WorkId);
        await fixture.LinkAsync(parent: steam.WorkId, child: gog.WorkId);

        var library = await fixture.LoadAsync();

        var tile = Assert.Single(library.VisibleTiles);
        Assert.Equal(3, tile.Entries.Count);
        Assert.Equal(["steam", "gog"], tile.Stores);
        Assert.Equal(["STEAM", "GOG"], tile.StoreChips);
    }

    // ── The headline figure, and the F10 hazard ─────────────────────────────

    /// <summary>
    /// Playtime sums across stores (user decision, 2026-08-31). The sum and
    /// the date both come from <c>CoveragePlaytime.Across</c>, so the grid
    /// headline and the modal's TOTAL cannot differ. The fixture gives the
    /// higher playtime the older date; a headline that borrowed one store's
    /// date would visibly show the wrong one.
    /// </summary>
    [Fact]
    public async Task The_headline_sums_and_carries_the_groups_own_date()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-400));
        var epic = await fixture.SeedAsync("Prey", minutes: 40, lastPlayed: Now.AddDays(-10), store: "epic");
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var library = await fixture.LoadAsync();
        var tile = Assert.Single(library.VisibleTiles);

        // The same figures the factory derives from the same entries.
        var expected = CoveragePlaytime.Across(tile.Entries);
        Assert.Equal(expected.PlaytimeMinutes, tile.PlaytimeMinutes);
        Assert.Equal(expected.LastPlayedAt, tile.LastPlayedUtc);

        Assert.Equal(340, tile.PlaytimeMinutes);

        // Not the date belonging to the 300-minute entry.
        Assert.Equal(Now.AddDays(-10), tile.LastPlayedUtc);
        Assert.NotEqual(Now.AddDays(-400), tile.LastPlayedUtc);

        // And each entry keeps its own pair, uncrossed.
        var steamEntry = tile.Entries.Single(e => e.Store == "steam");
        Assert.Equal(300, steamEntry.PlaytimeMinutes);
        Assert.Equal(Now.AddDays(-400), steamEntry.LastPlayedAt);
    }

    /// <summary>
    /// AC #4. The bucket is asserted against the rule, not against a hard-coded
    /// number. Two entries below the refund line make one game above it; the
    /// assertion is that the tile agrees with
    /// <c>LibraryBucketRules.Classify</c> over the summed figures.
    /// </summary>
    [Fact]
    public async Task The_bucket_of_a_cross_store_game_is_the_rule_applied_to_the_sum()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 70, lastPlayed: Now.AddDays(-300));
        var epic = await fixture.SeedAsync("Prey", minutes: 70, lastPlayed: Now.AddDays(-200), store: "epic");

        var before = await fixture.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.All(before, row => Assert.Equal(LibraryBuckets.Active, row.Bucket));

        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);
        var library = await fixture.LoadAsync();
        var tile = Assert.Single(library.VisibleTiles);

        var expected = LibraryBucketRules.Classify(
            tile.PlaytimeMinutes, tile.LastPlayedUtc, tile.Game.MajorUpdateAt, BucketThresholds.Default);

        Assert.Equal(expected, tile.Bucket);

        // Which for this fixture means the game leaves the pile neither of its
        // entries was in.
        Assert.Equal(LibraryBuckets.Bounced, tile.Bucket);
    }

    /// <summary>
    /// Dormancy follows the game's last-played, not the primary's. Dormancy
    /// answers when you last touched this game; a tile faded on the primary's
    /// own date would ghost a game played two days ago on the other store.
    /// </summary>
    [Fact]
    public async Task Dormancy_follows_the_groups_last_played_and_not_the_primarys()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddYears(-4));
        var epic = await fixture.SeedAsync("Prey", minutes: 10, lastPlayed: Now.AddDays(-2), store: "epic");

        var beforeLink = await fixture.LoadAsync();
        var dormant = beforeLink.VisibleTiles.Single(t => t.Store == "steam");

        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);
        var library = await fixture.LoadAsync();
        var tile = Assert.Single(library.VisibleTiles);

        Assert.True(tile.DormancyAlpha > dormant.DormancyAlpha,
            "a game played two days ago on the other store must not stay ghosted");
    }

    // ── Expansions do not collapse ──────────────────────────────────────────

    /// <summary>
    /// An <c>expansion_of</c> link groups for display only and collapses
    /// nothing (user decision, 2026-08-31). Two tiles, two counts, two
    /// playtimes, and the unplayed expansion of a played-out parent still
    /// reachable.
    /// </summary>
    [Fact]
    public async Task An_expansion_link_does_not_collapse_a_tile()
    {
        using var fixture = new GrainFixture();
        var civ = await fixture.SeedAsync("Civilization IV", minutes: 12_000, lastPlayed: Now.AddYears(-2));
        var bts = await fixture.SeedAsync("Beyond the Sword", minutes: 0, lastPlayed: null);

        await fixture.LinkAsync(parent: civ.WorkId, child: bts.WorkId, IdentityLinkKinds.ExpansionOf);
        var library = await fixture.LoadAsync();

        Assert.Equal(2, library.VisibleTiles.Count);
        Assert.Equal(2, library.AllGames.Count);
        Assert.All(library.VisibleTiles, tile => Assert.Single(tile.Entries));

        var expansion = library.VisibleTiles.Single(t => t.Title == "Beyond the Sword");
        Assert.Equal(0, expansion.PlaytimeMinutes);
        Assert.Equal(LibraryBuckets.NeverPlayed, expansion.Bucket);
    }

    // ── Counts ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Every count on screen against its own definition, after a link.
    /// All Games and the rail buckets count tiles on the game's bucket. The
    /// Platforms screen and the filter panel's platform options count tiles
    /// that include a store — one relation asked twice, therefore the same
    /// answer twice. The per-store figures sum to more than All Games, by
    /// exactly the number of extra store memberships; §11.2's per-tile rule
    /// survives the change of grain.
    /// </summary>
    [Fact]
    public async Task Every_count_on_screen_agrees_with_its_own_definition()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-400), store: "epic");
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Hades", minutes: 8_000, lastPlayed: Now.AddDays(-5), store: "gog");

        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);
        var library = await fixture.LoadAsync();

        // The grid and its total.
        Assert.Equal(3, library.VisibleTiles.Count);
        Assert.Equal(library.VisibleTiles.Count, library.AllGames.Count);
        Assert.Equal(library.VisibleTiles.Count, library.TotalCount);

        // Every rail bucket is the number of TILES in it.
        foreach (var bucket in library.Buckets)
        {
            Assert.Equal(
                library.VisibleTiles.Count(t => t.Bucket == bucket.Key),
                bucket.Count);
        }

        // The Platforms screen and the panel's PLATFORM group are one relation.
        var byStore = library.TitlesByStore();
        var platform = library.Filters.Groups.Single(g => g.Key == FilterPanelViewModel.StoreKey);

        foreach (var option in platform.AllOptions)
        {
            Assert.Equal(byStore.GetValueOrDefault(option.Key), option.Count);
            Assert.Equal(
                library.VisibleTiles.Count(t => t.Stores.Contains(option.Key)),
                option.Count);
        }

        // Stated rather than discovered: the per-store figures add up to MORE
        // than All Games, by exactly the number of extra store memberships.
        Assert.Equal(2, byStore["steam"]);
        Assert.Equal(1, byStore["epic"]);
        Assert.Equal(1, byStore["gog"]);
        Assert.Equal(
            library.AllGames.Count + library.VisibleTiles.Sum(t => t.Stores.Count - 1),
            byStore.Values.Sum());
    }

    /// <summary>
    /// A store cut keeps a game owned on either store, which is what makes
    /// the platform count above the tile count the cut would show.
    /// </summary>
    [Fact]
    public async Task A_store_cut_keeps_a_game_owned_on_either_store()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-400), store: "epic");
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);

        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);
        var library = await fixture.LoadAsync();

        var platform = library.Filters.Groups.Single(g => g.Key == FilterPanelViewModel.StoreKey);
        var epicOption = platform.AllOptions.Single(o => o.Key == "epic");

        Assert.Equal(1, epicOption.Count);
        epicOption.IsChecked = true;

        var tile = Assert.Single(library.VisibleTiles);
        Assert.Equal("Prey", tile.Title);
        Assert.Equal(epicOption.Count, library.VisibleTiles.Count);
    }

    // ── Selection, keyboard and the modal ───────────────────────────────────

    /// <summary>
    /// Selection, keyboard and the modal all work from a collapsed tile.
    /// Arrowing walks the visible tiles; selection lands on a collapsed tile
    /// like any other; the modal it opens is the game's, showing the primary
    /// entry's facts with the covered titles beside them. The modal's TOTAL
    /// is the tile's headline, from the same factory over the same entries.
    /// </summary>
    [Fact]
    public async Task Selection_keyboard_and_the_modal_all_work_from_a_collapsed_tile()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey Deluxe", minutes: 90, lastPlayed: Now.AddDays(-400), store: "epic");
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var library = await fixture.LoadAsync();
        library.Sort = LibrarySort.NameAscending;

        // Keyboard navigation reaches every tile and stops at the ends.
        Assert.Equal(0, library.MoveSelection(1));
        Assert.Equal(1, library.MoveSelection(1));
        Assert.Equal(1, library.MoveSelection(1));

        var collapsed = library.VisibleTiles.Single(t => t.IsMultiStore);
        library.SelectTile(collapsed);
        Assert.True(collapsed.IsSelected);
        Assert.Same(collapsed, Assert.Single(library.SelectedTiles));

        await library.OpenDetailsCommand.ExecuteAsync(collapsed);

        var details = library.Details;
        Assert.NotNull(details);
        Assert.Same(collapsed, details!.Tile);
        Assert.Equal("Prey", details.Title);
        Assert.Equal(["STEAM", "EPIC"], details.StoreChips);

        // The modal's TOTAL is the tile's headline, from the same factory --
        // compared as the text both surfaces render, because that is what the
        // user can hold the two against.
        Assert.NotNull(details.Coverage);
        Assert.True(details.Coverage!.HasCoverage);
        Assert.True(details.Coverage.IsComposite);
        Assert.Equal(
            GameTileViewModel.BuildPlaytimeText(collapsed.PlaytimeMinutes),
            details.Coverage.TotalPlaytimeText);
    }

    /// <summary>
    /// A collapsed tile answers to every ownership and release it folded. The
    /// feed records the ownership it surfaced and the journal records the
    /// ownership it watched; neither knows the tile moved.
    /// </summary>
    [Fact]
    public async Task A_collapsed_tile_answers_to_every_ownership_and_release_it_folded()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-400), store: "epic");
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var library = await fixture.LoadAsync();
        var tile = Assert.Single(library.VisibleTiles);

        Assert.Same(tile, library.TileForOwnership(steam.OwnershipId));
        Assert.Same(tile, library.TileForOwnership(epic.OwnershipId));
        Assert.Same(tile, library.TileForRelease(steam.ReleaseId));
        Assert.Same(tile, library.TileForRelease(epic.ReleaseId));
    }

    /// <summary>
    /// Update events are read for every entry and compared against the game's
    /// last-played, so the badge and the modal cannot disagree. The fixture
    /// patches the Epic entry while the Steam entry is primary — exactly the
    /// case a primary-only read would miss.
    /// </summary>
    [Fact]
    public async Task The_modal_lists_updates_from_every_entry_of_the_game()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddYears(-3));
        var epic = await fixture.SeedAsync("Prey", minutes: 10, lastPlayed: Now.AddYears(-3), store: "epic");
        await fixture.SeedMajorUpdateAsync(epic.ReleaseId, Now.AddMonths(-2), "Epic build");
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var library = await fixture.LoadAsync();
        var tile = Assert.Single(library.VisibleTiles);

        Assert.Equal(LibraryBuckets.StaleButPatched, tile.Bucket);
        Assert.True(tile.HasUnread);

        await library.OpenDetailsCommand.ExecuteAsync(tile);
        Assert.NotEmpty(library.Details!.Updates);
    }

    // ── Play, and what the tile promises ────────────────────────────────────

    /// <summary>
    /// Play acts on the copy that is on disk, whichever store sold it. A tile
    /// that offered the primary's launch route while the other store held the
    /// installed copy would name an action it cannot perform.
    /// </summary>
    [Fact]
    public async Task Play_acts_on_the_copy_that_is_actually_installed()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync(
            "Prey", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic", installed: true);
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var library = await fixture.LoadAsync();
        var tile = Assert.Single(library.VisibleTiles);

        Assert.Equal(steam.OwnershipId, tile.OwnershipId);
        Assert.Equal(epic.OwnershipId, tile.PlayableEntry.OwnershipId);
        Assert.True(tile.IsOnDisk);
    }

    /// <summary>
    /// §8. The resting mark on a multi-store tile is one letter per store, so
    /// the words must exist where a screen reader can reach them. The
    /// automation name distinguishes a collapsed tile and names its stores.
    /// </summary>
    [Fact]
    public async Task The_automation_name_distinguishes_a_collapsed_tile_and_names_its_stores()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var library = await fixture.LoadAsync();

        var collapsed = library.VisibleTiles.Single(t => t.IsMultiStore);
        var single = library.VisibleTiles.Single(t => !t.IsMultiStore);

        Assert.Contains("Steam", collapsed.AutomationName, StringComparison.Ordinal);
        Assert.Contains("Epic", collapsed.AutomationName, StringComparison.Ordinal);
        Assert.NotEqual(collapsed.Title, collapsed.AutomationName);
        Assert.Equal(single.Title, single.AutomationName);
    }

    // ── The wall ────────────────────────────────────────────────────────────

    /// <summary>
    /// The wall's geometry is a closed form in the width and the density; the
    /// item count only chooses how many rows to draw. A row is charged for
    /// the gutters between its cells and never for a trailing one — the
    /// disagreement §5.4 records <c>UniformGridLayout</c> could not be talked
    /// out of, and the reason <c>CoverWall</c> exists at all.
    /// </summary>
    [Theory]
    [InlineData(1200, 108)]
    [InlineData(1200, 148)]
    [InlineData(1200, 200)]
    [InlineData(1600, 108)]
    [InlineData(1920, 148)]
    [InlineData(3440, 200)]
    [InlineData(437, 148)]
    [InlineData(101, 108)]
    public void A_row_of_the_wall_fills_the_width_it_was_given(double width, double density)
    {
        const double spacing = 16;
        var (columns, cellWidth, cellHeight) = CoverWall.GeometryFor(width, density, spacing, 1.5);

        var used = (columns * cellWidth) + ((columns - 1) * spacing);

        Assert.True(columns >= 1);
        Assert.True(used <= width, $"{columns} x {cellWidth} overflowed {width}.");
        Assert.True(width - used < columns + 1,
            $"{width - used}px of slack at {width} is a lost column.");
        Assert.Equal(Math.Floor(cellWidth * 1.5), cellHeight);
    }

    /// <summary>
    /// Collapsing the grid changes the extent by whole rows and nothing else.
    /// Fewer items means fewer rows on the same cell size and the same gutter;
    /// the wall's height never drops by a fraction of a row.
    /// </summary>
    [Theory]
    [InlineData(1200, 148)]
    [InlineData(3440, 200)]
    [InlineData(1600, 108)]
    public void Fewer_tiles_shortens_the_wall_by_whole_rows(double width, double density)
    {
        const double spacing = 16;
        var (columns, _, cellHeight) = CoverWall.GeometryFor(width, density, spacing, 1.5);

        var full = CoverWall.ExtentFor(1_012, columns, cellHeight, spacing);
        var collapsed = CoverWall.ExtentFor(1_011, columns, cellHeight, spacing);

        var lostRows = ((1_012 + columns - 1) / columns) - ((1_011 + columns - 1) / columns);
        Assert.InRange(lostRows, 0, 1);
        Assert.Equal(lostRows * (cellHeight + spacing), full - collapsed, precision: 6);

        Assert.Equal(0, CoverWall.ExtentFor(0, columns, cellHeight, spacing));
        Assert.Equal(cellHeight, CoverWall.ExtentFor(1, columns, cellHeight, spacing));
    }

    // ── Lists ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>list_items</c> stays per release on purpose; adding a game to a list
    /// is an act on the entry the user picked. A list holding both entries of
    /// one game shows one row and counts one.
    /// </summary>
    [Fact]
    public async Task A_list_holding_both_entries_of_one_game_shows_one_row()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");
        await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        var listId = await fixture.Lists.InsertAsync(new GameList { Name = "Co-op night" });
        await fixture.Lists.AddItemAsync(new ListItem
        {
            ListId = listId, ReleaseId = steam.ReleaseId, Position = 0,
        });
        await fixture.Lists.AddItemAsync(new ListItem
        {
            ListId = listId, ReleaseId = epic.ReleaseId, Position = 1,
        });

        var library = await fixture.LoadAsync();
        var list = library.Lists.Lists.Single(l => l.Id == listId);

        Assert.Equal(1, list.Count);

        library.OpenListCommand.Execute(list);
        var tile = Assert.Single(library.VisibleTiles);
        Assert.Equal("Prey", tile.Title);
    }

    /// <summary>
    /// A collapsed tile sorts and moves by the list row it actually holds.
    /// The list can hold the entry that is not this tile's primary (the user
    /// added the Epic copy; the Steam copy is the kept title), so list order,
    /// the move buttons and the move itself must key on that row. Keying on
    /// the primary alone sorted the tile to the end and left it unmovable.
    /// </summary>
    [Fact]
    public async Task A_collapsed_tile_sorts_and_moves_by_the_entry_the_list_holds()
    {
        using var fixture = new GrainFixture();
        var steam = await fixture.SeedAsync("Prey", minutes: 300, lastPlayed: Now.AddDays(-30));
        var epic = await fixture.SeedAsync("Prey", minutes: 90, lastPlayed: Now.AddDays(-40), store: "epic");
        var other = await fixture.SeedAsync("Dishonored", minutes: 0, lastPlayed: null);
        await fixture.LinkAsync(parent: steam.WorkId, child: epic.WorkId);

        // The list holds the EPIC entry of Prey, which is not its primary.
        var listId = await fixture.Lists.InsertAsync(new GameList { Name = "Co-op night" });
        await fixture.Lists.AddItemAsync(new ListItem
        {
            ListId = listId, ReleaseId = epic.ReleaseId, Position = 0,
        });
        await fixture.Lists.AddItemAsync(new ListItem
        {
            ListId = listId, ReleaseId = other.ReleaseId, Position = 1,
        });

        var library = await fixture.LoadAsync();
        var list = library.Lists.Lists.Single(l => l.Id == listId);
        library.OpenListCommand.Execute(list);

        Assert.Equal(LibrarySort.ListOrder, library.Sort);
        Assert.Equal(["Prey", "Dishonored"], library.VisibleTiles.Select(t => t.Title));

        var prey = library.VisibleTiles[0];
        library.SelectTile(prey);
        Assert.True(library.CanMoveDownInList);
        Assert.False(library.CanMoveUpInList);

        await library.MoveInListAsync(1);

        Assert.Equal(["Dishonored", "Prey"], library.VisibleTiles.Select(t => t.Title));
    }

    // ── Fixture ─────────────────────────────────────────────────────────────

    private sealed class GrainFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 700_000;

        public GrainFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Ownerships = new OwnershipRepository(_db.Factory);
            Plays = new PlayRecordRepository(_db.Factory);
            Updates = new UpdateEventRepository(_db.Factory);
            Queries = new LibraryQueryRepository(_db.Factory);
            Links = new IdentityLinkRepository(_db.Factory);
            Lists = new GameListRepository(_db.Factory);
        }

        public WorkRepository Works { get; }

        public ReleaseRepository Releases { get; }

        public OwnershipRepository Ownerships { get; }

        public PlayRecordRepository Plays { get; }

        public UpdateEventRepository Updates { get; }

        public LibraryQueryRepository Queries { get; }

        public IIdentityLinkRepository Links { get; }

        public IGameListRepository Lists { get; }

        public void Dispose() => _db.Dispose();

        public async Task<LibraryViewModel> LoadAsync()
        {
            var library = new LibraryViewModel(
                Queries, Ownerships, Releases, Works, Updates,
                covers: null,
                lists: Lists,
                identityLinks: Links);

            await library.LoadCommand.ExecuteAsync(null);
            return library;
        }

        public Task LinkAsync(long parent, long child, string kind = IdentityLinkKinds.SameGame)
            => Links.LinkAsync(new IdentityLinkRequest
            {
                ParentWorkId = parent,
                ChildWorkIds = [child],
                Kind = kind,
            });

        public async Task<SeededEntry> SeedAsync(
            string title,
            long minutes,
            DateTime? lastPlayed,
            string store = "steam",
            bool installed = false)
        {
            var workId = await Works.InsertAsync(new Work { Name = title, FirstReleaseYear = 2017 });
            var releaseId = await Releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = title,
                Platform = "windows",
            });

            // A Steam appid per release, so each has a cover key of its own and
            // a borrowed one is visibly a different key rather than two nulls.
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

            return new SeededEntry(workId, releaseId, ownershipId);
        }

        /// <summary>A correlated build push and announcement: one major update.</summary>
        public async Task SeedMajorUpdateAsync(long releaseId, DateTime occurredAt, string title)
        {
            await Updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = releaseId,
                Kind = UpdateEventKinds.BuildPush,
                OccurredAt = occurredAt,
                Title = title,
            });

            await Updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = releaseId,
                Kind = UpdateEventKinds.Announcement,
                OccurredAt = occurredAt.AddDays(1),
                Title = title,
            });
        }

        public sealed record SeededEntry(long WorkId, long ReleaseId, long OwnershipId);
    }
}
