using System.Globalization;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

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
        Assert.True(library.Details.HasGap);

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
        Assert.Equal(
            ["v1.19.2 Patch", "Build 24678461", "Old news"],
            details.Updates.Select(u => u.Headline));

        // Two landed after the 2017 session; the 2024 one did too. All three
        // are after it, so all three are marks and the section says so.
        Assert.Equal("SINCE YOU PLAYED", details.UpdatesLabel);
        Assert.Equal("3 updates landed while you were away.", details.GapCaption);
        Assert.Equal(3, details.RailMarks.Count);

        // A build push has no reader-facing page, and a non-http scheme is not
        // something to hand to the shell.
        Assert.True(details.Updates[0].HasLink);
        Assert.False(details.Updates[1].HasLink);
        Assert.False(details.Updates[2].HasLink);
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

        Assert.Equal("1 update landed while you were away.", library.Details!.GapCaption);
    }

    /// <summary>
    /// The library is what tells each update row where the last session was, so
    /// the "since you played" claim is only made when it is true. Before this
    /// the panel listed every event a release had ever had under that heading.
    /// </summary>
    [Fact]
    public async Task An_update_older_than_the_last_session_is_history_not_a_missed_one()
    {
        using var fixture = new LibraryFixture();
        var releaseId = await fixture.SeedAsync("Recent", minutes: 600, lastPlayed: Now.AddDays(-2));
        await fixture.AddUpdateAsync(releaseId, UpdateEventKinds.Announcement,
            new DateTime(2023, 12, 12, 16, 38, 21, DateTimeKind.Utc),
            title: "December 12, 2023 Update",
            url: "https://store.steampowered.com/news/app/80/view/1");

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        var details = library.Details!;
        Assert.Equal("UPDATE HISTORY", details.UpdatesLabel);
        Assert.Equal("No updates recorded in that stretch.", details.GapCaption);
        Assert.Empty(details.RailMarks);
        Assert.Single(details.Updates);
        Assert.True(details.Updates[0].HasLink);
    }

    /// <summary>
    /// The Steam affordances come from the appid the load pass already read out
    /// of external_ids — the panel does not go back to the database for it, and
    /// it does not build a URL from anything else.
    /// </summary>
    [Fact]
    public async Task Opening_details_carries_the_steam_appid_through_to_real_links()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Empyrion", minutes: 2_220, lastPlayed: Now.AddYears(-2));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        var details = library.Details!;
        var appId = details.SteamAppId;

        Assert.NotNull(appId);
        Assert.Equal($"steam://install/{appId}", details.PrimaryAction!.Uri);
        Assert.Equal($"https://store.steampowered.com/app/{appId}/", details.Links[0].Uri);
        Assert.Equal($"https://store.steampowered.com/news/app/{appId}", details.Links[1].Uri);
    }

    /// <summary>
    /// §1's longitudinal series, read on open from the ownership's own history
    /// and stated as a sentence rather than drawn as a chart through one point.
    /// </summary>
    [Fact]
    public async Task The_panel_reports_the_playtime_history_winnow_has_recorded()
    {
        using var fixture = new LibraryFixture();
        var releaseId = await fixture.SeedAsync("Witchspire", minutes: 243, lastPlayed: Now.AddDays(-1));
        await fixture.AddSnapshotAsync(releaseId, 176, Now.AddDays(-3));
        await fixture.AddSnapshotAsync(releaseId, 243, Now.AddDays(-1));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        Assert.True(library.Details!.HasRecordLine);
        Assert.EndsWith("— up 1h 7m.", library.Details.RecordLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without a snapshot repository the panel simply says nothing about the
    /// record — it never invents a history it has not read.
    /// </summary>
    [Fact]
    public async Task No_snapshot_source_means_no_record_line()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Solo", minutes: 600, lastPlayed: Now.AddYears(-2));

        var library = fixture.CreateViewModel(withSnapshots: false);
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);

        Assert.False(library.Details!.HasRecordLine);
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
        Assert.False(details.HasIdentityLine);
        Assert.Null(details.Summary);
        Assert.Null(details.Publisher);

        // Nothing is a hole: the one band with no fact behind it says what is
        // going to fill it (§7).
        Assert.True(details.ShowEmptyBody);
        Assert.NotEmpty(details.EmptyBodyText);

        // Zero playtime and no date is the one case that really is "never".
        Assert.Equal("Never played", details.LastPlayedText);
        Assert.False(details.HasGap);
        Assert.Equal("You've never opened this.", details.NoGapText);
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
        Assert.False(details.ShowEmptyBody);
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
        Assert.False(library.Details.HasGap);
        Assert.Equal("Steam has no date for your last session.", library.Details.NoGapText);
        Assert.Equal("7m", library.Details.PlaytimeText);
    }

    // ── "All games" (the rail's first row) ───────────────────────────────────

    /// <summary>
    /// The state the app launches in now has a name and a row. Before this,
    /// "show me everything" was reachable only by clicking the bucket you were
    /// already on, and the rail showed no selection at all on launch.
    /// </summary>
    [Fact]
    public async Task All_games_is_selected_on_launch_and_counts_the_whole_library()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Zero Alpha", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Zero Beta", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Played Gamma", minutes: 5_000, lastPlayed: Now.AddDays(-2));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        Assert.Null(library.SelectedBucket);
        Assert.True(library.AllGames.IsSelected);
        Assert.Equal("ALL GAMES", library.AllGames.RailLabel);
        Assert.Equal(3, library.AllGames.Count);
        Assert.Equal("3", library.AllGames.CountText);
        Assert.Equal(3, library.VisibleTiles.Count);

        // Exactly one row in the rail carries the Volt edge, always.
        Assert.DoesNotContain(library.Buckets, b => b.IsSelected);
    }

    [Fact]
    public async Task Selecting_a_bucket_moves_the_rail_selection_off_all_games_and_back()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Zero Alpha", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Played Gamma", minutes: 5_000, lastPlayed: Now.AddDays(-2));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var neverPlayed = library.Buckets.Single(b => b.Name == "Never played");
        library.SelectBucketCommand.Execute(neverPlayed);

        Assert.Same(neverPlayed, library.SelectedBucket);
        Assert.False(library.AllGames.IsSelected);
        Assert.Single(library.Buckets, b => b.IsSelected);
        Assert.Equal(["Zero Alpha"], fixture.Titles(library));

        library.SelectBucketCommand.Execute(library.AllGames);

        Assert.Null(library.SelectedBucket);
        Assert.True(library.AllGames.IsSelected);
        Assert.DoesNotContain(library.Buckets, b => b.IsSelected);
        Assert.Equal(2, library.VisibleTiles.Count);
    }

    /// <summary>
    /// "All games" is a filter row, not a reset button: it clears the bucket and
    /// touches nothing else. A control that silently dropped the user's search
    /// or their sort order would be doing something they did not ask for.
    /// </summary>
    [Fact]
    public async Task All_games_clears_the_bucket_and_leaves_the_search_and_the_sort_alone()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Zero Alpha", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Zero Beta", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Zero Gamma", minutes: 0, lastPlayed: null);
        await fixture.SeedAsync("Played Zero Delta", minutes: 5_000, lastPlayed: Now.AddDays(-2));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        library.SelectBucketCommand.Execute(library.Buckets.Single(b => b.Name == "Never played"));
        library.Sort = LibrarySort.NameDescending;
        library.SearchText = "zero";

        Assert.Equal(["Zero Gamma", "Zero Beta", "Zero Alpha"], fixture.Titles(library));

        library.SelectBucketCommand.Execute(library.AllGames);

        Assert.Equal(LibrarySort.NameDescending, library.Sort);
        Assert.Equal("zero", library.SearchText);
        Assert.Equal(
            ["Zero Gamma", "Zero Beta", "Zero Alpha", "Played Zero Delta"],
            fixture.Titles(library));

        // And the count on the row is the library's, not the filtered set's —
        // the buckets count that way too, so the rail reads consistently.
        Assert.Equal(4, library.AllGames.Count);
    }

    /// <summary>
    /// Clicking "All games" while it is already selected is a no-op rather than
    /// a toggle into some third state — there is nothing below "no filter".
    /// </summary>
    [Fact]
    public async Task All_games_clicked_twice_stays_selected()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        library.SelectBucketCommand.Execute(library.AllGames);
        library.SelectBucketCommand.Execute(library.AllGames);

        Assert.Null(library.SelectedBucket);
        Assert.True(library.AllGames.IsSelected);
        Assert.Single(library.VisibleTiles);
    }

    /// <summary>
    /// The shell's rail command is the one the XAML binds; it also has to bring
    /// the library back from the merge queue, which is a screen no bucket — and
    /// no "all games" — describes.
    /// </summary>
    [Fact]
    public async Task All_games_returns_from_the_merge_queue()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var shell = new MainWindowViewModel(
            library,
            fixture.CreateMergeQueue(),
            DetachedStores.Create(),
            DetachedAppearance.Create(),
            DetachedFeed.Create(),
            DetachedAccountStats.Create())
        {
            IsMergeQueueVisible = true,
        };

        shell.SelectBucketCommand.Execute(library.AllGames);

        Assert.False(shell.IsMergeQueueVisible);
        Assert.True(shell.IsLibraryVisible);
        Assert.True(library.AllGames.IsSelected);
    }

    /// <summary>
    /// M8: the window opens on the Feed, and that must cost the library
    /// nothing. ALL GAMES is one rail click from the landing state — the same
    /// click, through the same command, that brings it back from every other
    /// screen.
    /// </summary>
    [Fact]
    public async Task The_app_opens_on_the_feed_and_all_games_is_one_click_away()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var shell = new MainWindowViewModel(
            library,
            fixture.CreateMergeQueue(),
            DetachedStores.Create(),
            DetachedAppearance.Create(),
            DetachedFeed.Create(),
            DetachedAccountStats.Create());

        // The landing state, before anything has been clicked.
        Assert.True(shell.IsFeedVisible);
        Assert.False(shell.IsLibraryVisible);

        shell.SelectBucketCommand.Execute(library.AllGames);

        Assert.False(shell.IsFeedVisible);
        Assert.True(shell.IsLibraryVisible);
        Assert.True(library.AllGames.IsSelected);

        // And the rail row navigates back, like every other rail row.
        shell.ShowFeedCommand.Execute(null);
        Assert.True(shell.IsFeedVisible);
        Assert.False(shell.IsLibraryVisible);

        // Clicking the row you are already on stays there: the rail marks where
        // you are, and there is no state below "the feed" to toggle into.
        shell.ShowFeedCommand.Execute(null);
        Assert.True(shell.IsFeedVisible);
        Assert.False(shell.IsLibraryVisible);
    }

    /// <summary>
    /// The rail's Volt edge means "this is where you are" (§12.2) and exactly
    /// one row ever carries it. FEED and ALL GAMES sit in the same section, so
    /// two lit rows there read as two selections, which is what the Feed's old
    /// visibility toggle produced on every launch.
    /// </summary>
    [Fact]
    public async Task The_feed_and_all_games_are_never_both_selected()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var shell = new MainWindowViewModel(
            library,
            fixture.CreateMergeQueue(),
            DetachedStores.Create(),
            DetachedAppearance.Create(),
            DetachedFeed.Create(),
            DetachedAccountStats.Create());

        // The landing state is the feed, so ALL GAMES is not where you are.
        Assert.True(shell.IsFeedVisible);
        Assert.False(library.AllGames.IsSelected);

        shell.SelectBucketCommand.Execute(library.AllGames);
        Assert.False(shell.IsFeedVisible);
        Assert.True(library.AllGames.IsSelected);

        shell.ShowFeedCommand.Execute(null);
        Assert.True(shell.IsFeedVisible);
        Assert.False(library.AllGames.IsSelected);

        // A bucket follows the same rule: leaving for the feed drops its
        // Volt edge while SelectedBucket stays set and the grid stays cut.
        var bucket = library.Buckets.First();
        shell.SelectBucketCommand.Execute(bucket);
        Assert.True(bucket.IsSelected);
        Assert.False(shell.IsFeedVisible);

        shell.ShowFeedCommand.Execute(null);
        Assert.True(shell.IsFeedVisible);
        Assert.False(bucket.IsSelected);
        Assert.False(library.AllGames.IsSelected);
        Assert.Same(bucket, library.SelectedBucket);

        // Coming back re-marks the row without the user re-picking it.
        shell.ShowLibraryCommand.Execute(null);
        Assert.True(bucket.IsSelected);
    }

    /// <summary>
    /// The three settings screens are one surface behind the rail's gear rather
    /// than three rail rows. The gear opens it, the sections switch between
    /// themselves without ever landing on nothing, and it reopens on the section
    /// it was left on.
    /// </summary>
    [Fact]
    public async Task The_gear_opens_the_settings_surface_and_its_sections_switch()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var shell = new MainWindowViewModel(
            library,
            fixture.CreateMergeQueue(),
            DetachedStores.Create(),
            DetachedAppearance.Create(),
            DetachedFeed.Create(),
            DetachedAccountStats.Create());

        Assert.False(shell.IsSettingsVisible);

        // The gear opens on the first section.
        await shell.ShowSettingsCommand.ExecuteAsync(null);
        Assert.True(shell.IsSettingsVisible);
        Assert.True(shell.IsStoresVisible);
        Assert.False(shell.IsFeedVisible);
        Assert.False(shell.IsLibraryVisible);

        // One section at a time, and picking the one on show keeps it.
        shell.ShowAppearanceCommand.Execute(null);
        Assert.True(shell.IsAppearanceVisible);
        Assert.False(shell.IsStoresVisible);
        Assert.True(shell.IsSettingsVisible);

        shell.ShowAppearanceCommand.Execute(null);
        Assert.True(shell.IsAppearanceVisible);
        Assert.True(shell.IsSettingsVisible);

        // Leaving is the rail's job, and it takes the whole surface.
        shell.SelectBucketCommand.Execute(library.AllGames);
        Assert.False(shell.IsSettingsVisible);
        Assert.False(shell.IsAppearanceVisible);
        Assert.True(library.AllGames.IsSelected);

        // And the gear comes back to the section it was left on.
        await shell.ShowSettingsCommand.ExecuteAsync(null);
        Assert.True(shell.IsAppearanceVisible);
        Assert.False(shell.IsStoresVisible);
        Assert.False(library.AllGames.IsSelected);
    }

    /// <summary>
    /// The Feed renders the library's own tiles rather than a second projection
    /// of the same games (<see cref="IGameTileSource"/>), so the lookup has to
    /// be the same object the wall is showing — a copy would be a second answer
    /// about a cover, an install state or a launch route.
    /// </summary>
    [Fact]
    public async Task The_tile_source_hands_back_the_walls_own_tile()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anything", minutes: 10, lastPlayed: Now.AddDays(-9));

        var library = fixture.CreateViewModel();

        Assert.False(((IGameTileSource)library).HasTiles);

        await library.LoadCommand.ExecuteAsync(null);

        var source = (IGameTileSource)library;
        var wall = library.VisibleTiles[0];

        Assert.True(source.HasTiles);
        Assert.Same(wall, source.TileForOwnership(wall.OwnershipId));
        Assert.Null(source.TileForOwnership(-1));
    }

    // ── Dimming (§8's toggle over the §5.1 ramp) ─────────────────────────────

    [Fact]
    public async Task Covers_dim_with_idle_time_by_default()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Recent", minutes: 600, lastPlayed: Now.AddDays(-3));
        await fixture.SeedAsync("Ancient", minutes: 600, lastPlayed: Now.AddYears(-4));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        Assert.True(library.Ramp.DimsDormantCovers);
        Assert.Equal(0.0, fixture.Tile(library, "Ancient").DormancyAlpha, 3);
        Assert.True(fixture.Tile(library, "Recent").DormancyAlpha > 0.9);
    }

    /// <summary>
    /// §8: "disable the dormancy ramp entirely for users who prefer uniform
    /// art." Every cover resolves to the vivid layer at full opacity, and the
    /// hover restore has nowhere left to travel.
    /// </summary>
    [Fact]
    public async Task Dimming_off_renders_every_cover_vivid_and_makes_the_hover_restore_a_no_op()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Recent", minutes: 600, lastPlayed: Now.AddDays(-3));
        await fixture.SeedAsync("Ancient", minutes: 600, lastPlayed: Now.AddYears(-4));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var display = new DisplaySettingsViewModel(library.Ramp);
        display.DimDormantCovers = false;

        foreach (var tile in library.VisibleTiles)
        {
            Assert.Equal(1.0, tile.DormancyAlpha, 6);
            Assert.Equal(1.0, tile.DisplayAlpha, 6);

            tile.IsPointerOver = true;
            Assert.Equal(1.0, tile.DisplayAlpha, 6);
            tile.IsPointerOver = false;
        }
    }

    /// <summary>
    /// The toggle flips the value the ramp RESOLVES to; it does not remove the
    /// ramp. The §5.1 curve is still computed by the same code, and turning the
    /// preference back on is a property write and a repaint — the tiles are the
    /// same instances, so nothing the cover cache handed them is disturbed and
    /// the pre-computed floor variants stay valid on disk.
    /// </summary>
    [Fact]
    public async Task Dimming_back_on_restores_the_ramp_without_rebuilding_a_tile()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Ancient", minutes: 600, lastPlayed: Now.AddYears(-4));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var tile = fixture.Tile(library, "Ancient");
        var display = new DisplaySettingsViewModel(library.Ramp);

        display.DimDormantCovers = false;
        Assert.Equal(1.0, tile.DormancyAlpha, 6);

        // The machinery is untouched underneath: the ramp still computes the
        // floor for this game, the ramp state is simply resolving past it.
        Assert.Equal(Dormancy.SatFloor, Dormancy.SaturationFor(tile.LastPlayedUtc, DateTime.UtcNow), 6);

        display.DimDormantCovers = true;

        Assert.Equal(0.0, tile.DormancyAlpha, 3);
        Assert.Same(tile, fixture.Tile(library, "Ancient"));
        Assert.Same(tile, library.VisibleTiles.Single());
    }

    /// <summary>A live toggle has to tell the wall, or the preference is a
    /// property nothing paints from.</summary>
    [Fact]
    public async Task Toggling_dimming_raises_the_tile_properties_the_view_binds()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Ancient", minutes: 600, lastPlayed: Now.AddYears(-4));

        var library = fixture.CreateViewModel();
        await library.LoadCommand.ExecuteAsync(null);

        var tile = library.VisibleTiles[0];
        var raised = new List<string?>();
        tile.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        library.Ramp.DimsDormantCovers = false;

        Assert.Contains(nameof(GameTileViewModel.DormancyAlpha), raised);
        Assert.Contains(nameof(GameTileViewModel.DisplayAlpha), raised);
    }

    // ── Dimming: persistence ─────────────────────────────────────────────────

    [Fact]
    public async Task Turning_dimming_off_writes_the_preference()
    {
        var settings = new FakeSettings();
        var ramp = new DormancyRamp();
        var display = new DisplaySettingsViewModel(ramp, settings);

        display.DimDormantCovers = false;
        await display.PendingSave;

        Assert.Equal("false", await settings.GetAsync(DormancyRamp.DimCoversSettingKey));
        Assert.Equal(1, settings.Writes);

        display.DimDormantCovers = true;
        await display.PendingSave;

        Assert.Equal("true", await settings.GetAsync(DormancyRamp.DimCoversSettingKey));
        Assert.Equal(2, settings.Writes);
    }

    [Fact]
    public async Task The_stored_preference_is_applied_on_load_and_not_written_back()
    {
        var settings = new FakeSettings();
        settings.Seed(DormancyRamp.DimCoversSettingKey, "false");

        var ramp = new DormancyRamp();
        var display = new DisplaySettingsViewModel(ramp, settings);

        Assert.True(display.DimDormantCovers);

        await display.LoadAsync();

        Assert.False(display.DimDormantCovers);
        Assert.False(ramp.DimsDormantCovers);

        // Reading a preference is not changing it: a load that wrote back would
        // rewrite the row on every launch.
        Assert.Equal(0, settings.Writes);
    }

    [Fact]
    public async Task An_unset_or_unreadable_preference_leaves_the_ramp_on()
    {
        var settings = new FakeSettings();
        var display = new DisplaySettingsViewModel(new DormancyRamp(), settings);

        await display.LoadAsync();
        Assert.True(display.DimDormantCovers);

        settings.Seed(DormancyRamp.DimCoversSettingKey, "yes please");
        await display.LoadAsync();
        Assert.True(display.DimDormantCovers);
    }

    /// <summary>
    /// With no store registered the toggle still works for the session. An
    /// unregistered preference costs persistence, never the control.
    /// </summary>
    [Fact]
    public async Task The_toggle_works_without_a_settings_store()
    {
        var ramp = new DormancyRamp();
        var display = new DisplaySettingsViewModel(ramp);

        await display.LoadAsync();
        display.DimDormantCovers = false;

        Assert.False(ramp.DimsDormantCovers);
        Assert.Equal(1.0, ramp.VividAlphaFor(null, DateTime.UtcNow), 6);
        await display.PendingSave;
    }

    /// <summary>
    /// The preference survives a restart, through the real repository and a real
    /// migrated database — the second view model is a fresh one over the same
    /// file, which is what the next launch is.
    /// </summary>
    [Fact]
    public async Task The_preference_survives_a_restart()
    {
        using var db = new TempDatabase();
        var settings = new SettingsRepository(db.Factory);

        var first = new DisplaySettingsViewModel(new DormancyRamp(), settings);
        first.DimDormantCovers = false;
        await first.PendingSave;

        var ramp = new DormancyRamp();
        var next = new DisplaySettingsViewModel(ramp, settings);
        await next.LoadAsync();

        Assert.False(next.DimDormantCovers);
        Assert.False(ramp.DimsDormantCovers);
        Assert.Equal(1.0, ramp.VividAlphaFor(null, DateTime.UtcNow), 6);
    }

    /// <summary>
    /// §8 asks for both settings. They are orthogonal: reduced motion decides
    /// whether the restore animates, dimming decides whether there is anything
    /// to animate. Neither reads or overwrites the other.
    /// </summary>
    [Fact]
    public async Task Reduced_motion_and_dimming_do_not_fight()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Ancient", minutes: 600, lastPlayed: Now.AddYears(-4));

        var ramp = new DormancyRamp { ReducedMotion = true };
        var library = fixture.CreateViewModel(ramp);
        await library.LoadCommand.ExecuteAsync(null);

        var tile = library.VisibleTiles[0];
        Assert.True(tile.SnapDormancy);
        Assert.Equal(0.0, tile.DormancyAlpha, 3);

        var display = new DisplaySettingsViewModel(ramp);
        display.DimDormantCovers = false;

        // Dimming off did not disturb the motion preference, and the tile is
        // uniform either way.
        Assert.True(ramp.ReducedMotion);
        Assert.True(tile.SnapDormancy);
        Assert.Equal(1.0, tile.DisplayAlpha, 6);

        ramp.ReducedMotion = false;

        Assert.False(ramp.DimsDormantCovers);
        Assert.False(tile.SnapDormancy);
        Assert.Equal(1.0, tile.DisplayAlpha, 6);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    /// <summary>In-memory <see cref="ISettingsRepository"/>: the toggle's
    /// contract is "what you wrote is what you read", nothing more.</summary>
    private sealed class FakeSettings : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public int Writes { get; private set; }

        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            _values[key] = value;
            Writes++;
            return Task.CompletedTask;
        }

        public void Seed(string key, string value) => _values[key] = value;
    }

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
            Snapshots = new PlaytimeSnapshotRepository(_db.Factory);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IOwnershipRepository Ownerships { get; }

        public IPlayRecordRepository Plays { get; }

        public IUpdateEventRepository Updates { get; }

        public ILibraryQueryRepository Queries { get; }

        public IPlaytimeSnapshotRepository Snapshots { get; }

        /// <summary>No cover cache: the library must compose on procedural art alone.</summary>
        public LibraryViewModel CreateViewModel(DormancyRamp? ramp = null, bool withSnapshots = true)
            => new(
                Queries, Ownerships, Releases, Works, Updates,
                covers: null,
                ramp: ramp,
                snapshots: withSnapshots ? Snapshots : null);

        public MergeQueueViewModel CreateMergeQueue()
        {
            var links = new IdentityLinkRepository(_db.Factory);
            var refusals = new ExpansionRefusalRepository(_db.Factory);
            return new MergeQueueViewModel(
                new MergeCandidateRepository(_db.Factory),
                Releases,
                Works,
                links,
                Ownerships,
                new LibraryExpansionScan(Releases, links, refusals),
                refusals,
                new LibraryQueryRepository(_db.Factory));
        }

        public IEnumerable<string> Titles(LibraryViewModel library)
            => library.VisibleTiles.Select(t => t.Title);

        public GameTileViewModel Tile(LibraryViewModel library, string title)
            => library.VisibleTiles.Single(t => t.Title == title);

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

        /// <summary>Adds one reading to the ownership's longitudinal series.</summary>
        public async Task AddSnapshotAsync(long releaseId, long minutes, DateTime observedAt)
        {
            var ownership = (await Ownerships.GetByReleaseAsync(releaseId)).Single();
            await Snapshots.InsertAsync(new PlaytimeSnapshot
            {
                OwnershipId = ownership.Id,
                PlaytimeMinutes = minutes,
                ObservedAt = observedAt,
            });
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
