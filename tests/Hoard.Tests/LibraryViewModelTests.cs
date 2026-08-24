using System.Globalization;
using Hoard.App.ViewModels;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The library view model: sort order, the grid/list toggle, and the detail
/// modal's state.
///
/// <para>Like the merge-queue tests these run against a real migrated SQLite
/// file and the real repositories — the view model is constructed directly and
/// every assertion is on its properties, so no Avalonia application,
/// dispatcher or rendering is involved.</para>
/// </summary>
public sealed class LibraryViewModelTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    // ── Sort (§4: remembered per session, and view-agnostic) ─────────────────

    [Fact]
    public async Task Default_order_is_dormant_longest_and_never_opened_leads_it()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Recent", minutes: 600, lastPlayed: Now.AddDays(-3));
        await fixture.SeedAsync("Ancient", minutes: 600, lastPlayed: Now.AddYears(-4));
        await fixture.SeedAsync("Untouched", minutes: 0, lastPlayed: null);

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        Assert.Equal(LibrarySort.DormantLongest, library.Sort);
        Assert.Equal("Dormant longest", library.SortLabel);
        Assert.Equal(["Untouched", "Ancient", "Recent"], fixture.Titles(library));
    }

    [Fact]
    public async Task Recently_played_is_the_default_order_reversed()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Recent", minutes: 600, lastPlayed: Now.AddDays(-3));
        await fixture.SeedAsync("Ancient", minutes: 600, lastPlayed: Now.AddYears(-4));
        await fixture.SeedAsync("Untouched", minutes: 0, lastPlayed: null);

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        library.Sort = LibrarySort.RecentlyPlayed;

        Assert.Equal(["Recent", "Ancient", "Untouched"], fixture.Titles(library));
    }

    [Fact]
    public async Task Playtime_sorts_both_ways()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Middling", minutes: 600, lastPlayed: Now.AddDays(-40));
        await fixture.SeedAsync("Marathon", minutes: 30_000, lastPlayed: Now.AddDays(-40));
        await fixture.SeedAsync("Glance", minutes: 12, lastPlayed: Now.AddDays(-40));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        library.Sort = LibrarySort.PlaytimeHighToLow;
        Assert.Equal(["Marathon", "Middling", "Glance"], fixture.Titles(library));

        library.Sort = LibrarySort.PlaytimeLowToHigh;
        Assert.Equal(["Glance", "Middling", "Marathon"], fixture.Titles(library));
    }

    [Fact]
    public async Task Name_sorts_both_ways_and_ignores_case()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("banjo", minutes: 10, lastPlayed: Now.AddDays(-9));
        await fixture.SeedAsync("Anvil", minutes: 10, lastPlayed: Now.AddDays(-8));
        await fixture.SeedAsync("cobalt", minutes: 10, lastPlayed: Now.AddDays(-7));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        library.Sort = LibrarySort.NameAscending;
        Assert.Equal(["Anvil", "banjo", "cobalt"], fixture.Titles(library));

        library.Sort = LibrarySort.NameDescending;
        Assert.Equal(["cobalt", "banjo", "Anvil"], fixture.Titles(library));
    }

    /// <summary>
    /// The list's column headers and the command bar's menu are two controls
    /// over one piece of state. A header that is already active flips
    /// direction; one that is not takes its own most useful direction first.
    /// </summary>
    [Fact]
    public async Task Column_headers_toggle_the_shared_sort_state()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        library.SortByPlaytimeCommand.Execute(null);
        Assert.Equal(LibrarySort.PlaytimeHighToLow, library.Sort);
        Assert.True(library.ShowPlaytimeSortDown);
        Assert.False(library.ShowPlaytimeSortUp);

        library.SortByPlaytimeCommand.Execute(null);
        Assert.Equal(LibrarySort.PlaytimeLowToHigh, library.Sort);
        Assert.True(library.ShowPlaytimeSortUp);

        library.SortByTitleCommand.Execute(null);
        Assert.Equal(LibrarySort.NameAscending, library.Sort);
        library.SortByTitleCommand.Execute(null);
        Assert.Equal(LibrarySort.NameDescending, library.Sort);

        // Idle starts at the product's own default rather than at "recent".
        library.SortByIdleCommand.Execute(null);
        Assert.Equal(LibrarySort.DormantLongest, library.Sort);
        library.SortByIdleCommand.Execute(null);
        Assert.Equal(LibrarySort.RecentlyPlayed, library.Sort);

        // Exactly one indicator is lit across all three columns.
        Assert.Equal(1, new[]
        {
            library.ShowTitleSortUp, library.ShowTitleSortDown,
            library.ShowPlaytimeSortUp, library.ShowPlaytimeSortDown,
            library.ShowIdleSortUp, library.ShowIdleSortDown,
        }.Count(on => on));
    }

    [Fact]
    public async Task The_menu_marks_the_active_order_and_the_button_names_it()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var playtimeHigh = library.SortOptions.Single(o => o.Sort == LibrarySort.PlaytimeHighToLow);
        library.SelectSortCommand.Execute(playtimeHigh);

        Assert.Equal(LibrarySort.PlaytimeHighToLow, library.Sort);
        Assert.Equal("Playtime high→low", library.SortLabel);
        Assert.Single(library.SortOptions, o => o.IsSelected);
        Assert.True(playtimeHigh.IsSelected);
    }

    /// <summary>
    /// Sorting and filtering are independent axes. Confusing them is how a sort
    /// control ends up silently hiding rows.
    /// </summary>
    [Fact]
    public async Task Sort_applies_within_a_bucket_filter_and_a_search()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Zero Alpha", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Zero Beta", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Played Gamma", minutes: 5_000, lastPlayed: Now.AddDays(-2));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var neverOpened = library.Buckets.Single(b => b.Name == "Never played");
        library.SelectBucketCommand.Execute(neverOpened);
        library.Sort = LibrarySort.NameDescending;

        Assert.Equal(["Zero Beta", "Zero Alpha"], fixture.Titles(library));

        library.SearchText = "alpha";
        Assert.Equal(["Zero Alpha"], fixture.Titles(library));

        // And the order survives a re-filter rather than snapping back.
        library.SearchText = string.Empty;
        Assert.Equal(LibrarySort.NameDescending, library.Sort);
        Assert.Equal(["Zero Beta", "Zero Alpha"], fixture.Titles(library));
    }

    // ── View switching (§4: grid default, list a toggle) ─────────────────────

    [Fact]
    public async Task The_view_mode_toggles_and_shows_exactly_one_view()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        Assert.True(library.IsGridView);
        Assert.True(library.ShowGrid);
        Assert.False(library.ShowList);

        library.ShowListViewCommand.Execute(null);
        Assert.False(library.IsGridView);
        Assert.False(library.ShowGrid);
        Assert.True(library.ShowList);

        library.ShowGridViewCommand.Execute(null);
        Assert.True(library.ShowGrid);
        Assert.False(library.ShowList);
    }

    /// <summary>
    /// The empty state replaces BOTH views, not just the grid — otherwise the
    /// list renders a header over nothing while the message is elsewhere.
    /// </summary>
    [Fact]
    public async Task An_empty_result_replaces_the_list_view_too()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        library.ShowListViewCommand.Execute(null);
        library.SearchText = "nothing matches this";

        Assert.True(library.ShowEmpty);
        Assert.False(library.ShowList);
        Assert.False(library.ShowGrid);
        Assert.Equal("No titles match “nothing matches this”.", library.EmptyMessage);
    }

    [Fact]
    public async Task Selection_survives_the_view_toggle_and_only_one_item_holds_it()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("First", minutes: 10, lastPlayed: Now.AddYears(-3));
        await fixture.SeedAsync("Second", minutes: 10, lastPlayed: Now.AddYears(-2));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        library.SelectTile(library.VisibleTiles[0]);
        library.ShowListViewCommand.Execute(null);

        Assert.Same(library.VisibleTiles[0], library.SelectedTile);
        Assert.Single(library.VisibleTiles, t => t.IsSelected);

        library.SelectTile(library.VisibleTiles[1]);
        Assert.Single(library.VisibleTiles, t => t.IsSelected);
        Assert.True(library.VisibleTiles[1].IsSelected);
        Assert.False(library.VisibleTiles[0].IsSelected);
    }

    [Fact]
    public async Task Filtering_away_the_selection_clears_it()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Kept", minutes: 10, lastPlayed: Now.AddYears(-3));
        await fixture.SeedAsync("Dropped", minutes: 10, lastPlayed: Now.AddYears(-2));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        library.SelectTile(library.VisibleTiles.Single(t => t.Title == "Dropped"));

        library.SearchText = "Kept";

        Assert.Null(library.SelectedTile);
        Assert.DoesNotContain(library.VisibleTiles, t => t.IsSelected);
        Assert.Equal(0, library.SelectedCount);
    }

    // ── Detail modal (§5.3's other half) ─────────────────────────────────────

    [Fact]
    public async Task Nothing_is_open_until_the_detail_command_runs()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        Assert.Null(library.Details);
        Assert.False(library.IsDetailsOpen);
    }

    [Fact]
    public async Task Opening_details_selects_the_game_and_closing_leaves_it_selected()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Empyrion", minutes: 2_220, lastPlayed: new DateTime(2017, 1, 2, 8, 0, 0, DateTimeKind.Utc));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        var tile = library.VisibleTiles[0];

        await library.OpenDetailsCommand.ExecuteAsync(tile);

        Assert.True(library.IsDetailsOpen);
        Assert.Same(tile, library.Details!.Tile);
        Assert.Same(tile, library.SelectedTile);
        Assert.Equal("Empyrion", library.Details.Title);
        Assert.Equal("STEAM", library.Details.StoreBadge);
        Assert.Equal("37h", library.Details.PlaytimeText);
        Assert.True(library.Details.HasLastPlayedDate);

        library.CloseDetailsCommand.Execute(null);

        Assert.Null(library.Details);
        Assert.False(library.IsDetailsOpen);
        Assert.Same(tile, library.SelectedTile);
    }

    /// <summary>
    /// With no argument the command opens whatever the keyboard has selected —
    /// this is what <c>Enter</c> is wired to.
    /// </summary>
    [Fact]
    public async Task Opening_details_with_no_argument_uses_the_current_selection()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("First", minutes: 10, lastPlayed: Now.AddYears(-3));
        await fixture.SeedAsync("Second", minutes: 10, lastPlayed: Now.AddYears(-2));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        library.SelectTile(library.VisibleTiles[1]);

        await library.OpenDetailsCommand.ExecuteAsync(null);

        Assert.Same(library.VisibleTiles[1], library.Details?.Tile);
    }

    [Fact]
    public async Task Opening_details_with_nothing_selected_opens_nothing()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        await library.OpenDetailsCommand.ExecuteAsync(null);

        Assert.Null(library.Details);
    }

    /// <summary>
    /// §5.2: the badge exists so the user can go read what changed. Newest
    /// first, and only the rows that have a page offer a link.
    /// </summary>
    [Fact]
    public async Task Update_events_are_listed_newest_first_and_only_announcements_link_out()
    {
        using var fixture = new LibraryFixture();
        var releaseId = await fixture.SeedAsync(
            "Empyrion", minutes: 2_220, lastPlayed: new DateTime(2017, 1, 2, 8, 0, 0, DateTimeKind.Utc));

        await fixture.AddUpdateAsync(releaseId, UpdateEventKinds.Announcement,
            new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc),
            title: "v1.19.2 Patch", url: "https://store.steampowered.com/news/app/383120/view/1");
        await fixture.AddUpdateAsync(releaseId, UpdateEventKinds.BuildPush,
            new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc),
            buildId: "24678461");
        await fixture.AddUpdateAsync(releaseId, UpdateEventKinds.Announcement,
            new DateTime(2024, 3, 1, 9, 0, 0, DateTimeKind.Utc),
            title: "Old news", url: "javascript:alert(1)");

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        var details = library.Details!;
        Assert.True(details.HasUpdates);
        Assert.True(details.HasBody);
        Assert.Equal("3 updates since you played", details.UpdatesHeading);
        Assert.Equal(
            ["v1.19.2 Patch", "Build 24678461", "Old news"],
            details.Updates.Select(u => u.Headline));

        // A build push has no reader-facing page, and a non-http scheme is not
        // something to hand to the shell.
        Assert.True(details.Updates[0].HasUrl);
        Assert.False(details.Updates[1].HasUrl);
        Assert.False(details.Updates[2].HasUrl);
    }

    [Fact]
    public async Task One_update_is_singular()
    {
        using var fixture = new LibraryFixture();
        var releaseId = await fixture.SeedAsync("Solo", minutes: 600, lastPlayed: Now.AddYears(-2));
        await fixture.AddUpdateAsync(releaseId, UpdateEventKinds.BuildPush, Now.AddDays(-1));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        Assert.Equal("1 update since you played", library.Details!.UpdatesHeading);
    }

    /// <summary>
    /// Enrichment fills year, publisher and summary in behind a library the
    /// user is already browsing, so null is the normal state for a long while.
    /// Nothing may invent a stand-in for it.
    /// </summary>
    [Fact]
    public async Task Missing_metadata_renders_as_absent_rows_rather_than_placeholders()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Bare", minutes: 0, lastPlayed: null);

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        var details = library.Details!;
        Assert.False(details.HasReleaseYear);
        Assert.False(details.HasPublisher);
        Assert.False(details.HasSummary);
        Assert.False(details.HasUpdates);
        Assert.False(details.HasBody);
        Assert.Null(details.Summary);
        Assert.Null(details.Publisher);

        // Zero playtime and no date is the one case that really is "never".
        Assert.Equal("Never played", details.LastPlayedText);
        Assert.False(details.HasLastPlayedDate);
        Assert.Equal("—", details.PlaytimeText);
        Assert.Equal("—", details.IdleText);
        Assert.Equal("Never played", details.BucketLabel);
        Assert.Equal("Not installed", details.InstallText);
        Assert.False(details.HasInstallPath);
    }

    [Fact]
    public async Task Enriched_metadata_is_bound_when_it_lands()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync(
            "Factorio", minutes: 16_080, lastPlayed: Now.AddMonths(-21),
            year: 2020, summary: "You crash-land on an alien planet.",
            installed: true, installPath: @"D:\SteamLibrary\steamapps\common\Factorio");

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        var details = library.Details!;
        Assert.True(details.HasReleaseYear);
        Assert.Equal("2020", details.ReleaseYearText);
        Assert.True(details.HasSummary);
        Assert.True(details.HasBody);
        Assert.Equal("Installed", details.InstallText);
        Assert.True(details.HasInstallPath);
        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Factorio", details.InstallPath);
        Assert.Equal("268h", details.PlaytimeText);

        // Idle is measured against the wall clock at load, so pin the years
        // rather than the month the fixture's fixed "now" happens to land on.
        Assert.StartsWith("1y ", details.IdleText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Playtime with no last-played stamp is common in Steam's local files.
    /// Calling that "Never played" would contradict the hours beside it.
    /// </summary>
    [Fact]
    public async Task Playtime_without_a_date_reads_as_unrecorded_not_as_never()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Ambiguous", minutes: 7, lastPlayed: null);

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        Assert.Equal("Not recorded", library.Details!.LastPlayedText);
        Assert.False(library.Details.HasLastPlayedDate);
        Assert.Equal("7m", library.Details.PlaytimeText);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private sealed class LibraryFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 400000;

        public LibraryFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Ownerships = new OwnershipRepository(_db.Factory);
            Plays = new PlayRecordRepository(_db.Factory);
            Updates = new UpdateEventRepository(_db.Factory);
            Queries = new LibraryQueryRepository(_db.Factory);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IOwnershipRepository Ownerships { get; }

        public IPlayRecordRepository Plays { get; }

        public IUpdateEventRepository Updates { get; }

        public ILibraryQueryRepository Queries { get; }

        /// <summary>No cover cache: the library must compose on procedural art alone.</summary>
        public LibraryViewModel CreateViewModel()
            => new(Queries, Ownerships, Releases, Works, Updates);

        public IEnumerable<string> Titles(LibraryViewModel library)
            => library.VisibleTiles.Select(t => t.Title);

        /// <summary>Seeds one owned game and returns its release id.</summary>
        public async Task<long> SeedAsync(
            string title,
            long minutes,
            DateTime? lastPlayed,
            int? year = null,
            string? summary = null,
            bool installed = false,
            string? installPath = null)
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = title,
                FirstReleaseYear = year,
                Summary = summary,
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
                Store = "steam",
                Installed = installed,
                InstallPath = installPath,
            });

            await Plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = minutes,
                LastPlayedAt = lastPlayed,
                Source = "steam_localconfig",
                ObservedAt = Now,
            });

            return releaseId;
        }

        public Task AddUpdateAsync(
            long releaseId,
            string kind,
            DateTime occurredAt,
            string? title = null,
            string? url = null,
            string? buildId = null)
            => Updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = releaseId,
                Kind = kind,
                OccurredAt = occurredAt,
                Title = title,
                Url = url,
                BuildId = buildId,
            });

        public void Dispose() => _db.Dispose();
    }
}
