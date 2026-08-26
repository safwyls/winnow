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
/// The two kinds of list, as the rail and the cut bar drive them.
///
/// <para>The pair of tests that matter most are the two that state the
/// difference the section headings are announcing: a <b>list</b> holds what you
/// put in it and does not change when the library does, and a <b>live list</b>
/// holds a rule and finds its members again every time. Everything else here —
/// order, removal, rename, delete — is about a manual list, because a live list
/// has none of those problems and a manual one is all of them.</para>
/// </summary>
public sealed class ListsViewModelTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    // ── The empty state is a direction ──────────────────────────────────────

    [Fact]
    public async Task With_no_lists_the_rail_says_how_to_make_one()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Hades");

        var library = await fixture.LoadAsync();

        Assert.True(library.Lists.HasNoLists);
        Assert.True(library.Lists.ShowListsHeader);
        Assert.False(library.Lists.HasLiveLists);
        Assert.Equal(
            "No lists yet. Select titles and choose Add to list, or filter the library and save the result as a live list.",
            library.Lists.EmptyMessageText);
    }

    // ── Manual lists ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_list_is_built_from_the_selection_and_opens_in_the_order_it_was_built()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");
        var celeste = await fixture.SeedAsync("Celeste");
        await fixture.SeedAsync("Tunic");

        var library = await fixture.LoadAsync();
        var list = await library.Lists.CreateListAsync("Friday night", [hades, celeste]);
        Assert.NotNull(list);

        library.OpenListCommand.Execute(list);

        // Not alphabetical: a hand-built list opens in the order it was built,
        // and the sort menu gains the row that names that order.
        Assert.Equal(["Hades", "Celeste"], fixture.Titles(library));
        Assert.Equal(LibrarySort.ListOrder, library.Sort);
        Assert.Contains(library.SortOptions, o => o.Sort == LibrarySort.ListOrder);
    }

    [Fact]
    public async Task Leaving_a_list_puts_the_previous_order_back()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");

        var library = await fixture.LoadAsync();
        library.Sort = LibrarySort.NameAscending;

        var list = await library.Lists.CreateListAsync("Friday night", [hades]);
        library.OpenListCommand.Execute(list);
        Assert.Equal(LibrarySort.ListOrder, library.Sort);

        library.CloseListCommand.Execute(null);

        Assert.Equal(LibrarySort.NameAscending, library.Sort);
        Assert.DoesNotContain(library.SortOptions, o => o.Sort == LibrarySort.ListOrder);
        Assert.Null(library.Lists.Open);
    }

    [Fact]
    public async Task Adding_the_same_title_twice_does_not_move_it()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");
        var celeste = await fixture.SeedAsync("Celeste");

        var library = await fixture.LoadAsync();
        var list = await library.Lists.CreateListAsync("Friday night", [hades, celeste]);

        await library.Lists.AddToListAsync(list!, [hades]);

        Assert.Equal([hades, celeste], list!.ReleaseIds);
    }

    [Fact]
    public async Task A_title_can_be_moved_and_removed_and_the_move_survives_a_reload()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");
        var celeste = await fixture.SeedAsync("Celeste");
        var tunic = await fixture.SeedAsync("Tunic");

        var library = await fixture.LoadAsync();
        var list = await library.Lists.CreateListAsync("Friday night", [hades, celeste, tunic]);
        library.OpenListCommand.Execute(list);

        Assert.True(await library.Lists.MoveAsync(list!, tunic, -1));
        Assert.Equal([hades, tunic, celeste], list!.ReleaseIds);

        // Off the end is a no-op rather than a wrap: a list that teleports its
        // top row to the bottom is a list nobody trusts.
        Assert.False(await library.Lists.MoveAsync(list, hades, -1));

        await library.Lists.RemoveFromListAsync(list, [celeste]);
        Assert.Equal([hades, tunic], list.ReleaseIds);

        var reloaded = await fixture.LoadAsync();
        Assert.Equal([hades, tunic], reloaded.Lists.Lists.Single().ReleaseIds);
    }

    [Fact]
    public async Task The_move_buttons_go_dead_at_the_ends_of_the_list()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");
        var celeste = await fixture.SeedAsync("Celeste");
        var tunic = await fixture.SeedAsync("Tunic");

        var library = await fixture.LoadAsync();
        var list = await library.Lists.CreateListAsync("Friday night", [hades, celeste, tunic]);
        library.OpenListCommand.Execute(list);

        library.SelectedTiles = [.. library.VisibleTiles.Where(t => t.ReleaseId == hades)];
        Assert.False(library.CanMoveUpInList);
        Assert.True(library.CanMoveDownInList);

        library.SelectedTiles = [.. library.VisibleTiles.Where(t => t.ReleaseId == celeste)];
        Assert.True(library.CanMoveUpInList);
        Assert.True(library.CanMoveDownInList);

        library.SelectedTiles = [.. library.VisibleTiles.Where(t => t.ReleaseId == tunic)];
        Assert.True(library.CanMoveUpInList);
        Assert.False(library.CanMoveDownInList);
    }

    [Fact]
    public async Task Walking_the_grid_with_the_arrows_arms_add_to_list()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Hades");
        await fixture.SeedAsync("Celeste");

        var library = await fixture.LoadAsync();
        Assert.False(library.HasSelection);

        // §8's keyboard floor reaching the one control the lists feature hangs
        // off. This lived in the pointer handler once, and arrowing across the
        // wall left the button hidden.
        library.MoveSelection(1);

        Assert.True(library.HasSelection);
        Assert.Single(library.SelectedTiles);
        Assert.True(library.BeginAddToListCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_manual_list_does_not_change_when_the_library_does()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades", genres: ["Action"]);

        var library = await fixture.LoadAsync();
        var list = await library.Lists.CreateListAsync("Friday night", [hades]);
        Assert.NotNull(list);

        await fixture.SeedAsync("Dead Cells", genres: ["Action"]);
        var reloaded = await fixture.LoadAsync();

        Assert.Equal(1, reloaded.Lists.Lists.Single().Count);
    }

    [Fact]
    public async Task A_lists_count_drops_when_one_of_its_titles_leaves_the_library()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");
        var soundtrack = await fixture.SeedAsync("Hades Soundtrack", appType: "music");

        var library = await fixture.LoadAsync();
        var list = await library.Lists.CreateListAsync("Friday night", [hades, soundtrack]);
        library.OpenListCommand.Execute(list);
        Assert.Equal(2, list!.ReleaseIds.Count);

        // The soundtrack is filtered out of the library by default (§6.1's
        // non-game rule), so it has no tile — and a count that included it would
        // be a number the grid can never show.
        Assert.Equal(1, list.Count);
        Assert.Equal(["Hades"], fixture.Titles(library));
    }

    // ── Live lists ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_live_list_is_saved_from_the_cut_and_reopens_onto_the_same_rules()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", minutes: 300, genres: ["RPG"]);
        await fixture.SeedAsync("Hades", minutes: 300, genres: ["Action"]);

        var library = await fixture.LoadAsync();
        library.SelectBucketCommand.Execute(
            library.Buckets.Single(b => b.Key == LibraryBuckets.Bounced));
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");

        library.BeginSaveLiveListCommand.Execute(null);
        Assert.NotNull(library.Prompt);
        Assert.Equal("Name this live list", library.Prompt!.Question);

        // The suggestion is the rules read out, and the rail's bucket is part of
        // them — the panel and the rail are one filter.
        Assert.Equal("Bounced off · RPG", library.Prompt.Text);

        library.Prompt.Text = "Unfinished RPGs";
        await library.Prompt.ConfirmCommand.ExecuteAsync(null);

        var live = library.Lists.LiveLists.Single();
        Assert.Equal("Unfinished RPGs", live.Name);
        Assert.True(live.IsLive);
        Assert.Empty(live.ReleaseIds);
        Assert.Equal(["bounced"], live.Filter.Buckets);
        Assert.Equal(1, live.Count);

        // Reopening puts the rules back into the controls that made them.
        var reloaded = await fixture.LoadAsync();
        reloaded.OpenListCommand.Execute(reloaded.Lists.LiveLists.Single());

        Assert.Equal(LibraryBuckets.Bounced, reloaded.SelectedBucket?.Key);
        Assert.True(reloaded.Filters.IsOpen);
        Assert.Equal(["Disco Elysium"], fixture.Titles(reloaded));
        Assert.False(reloaded.IsLiveListEdited);
    }

    [Fact]
    public async Task A_live_list_picks_up_a_title_the_library_gained()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");
        var live = await library.Lists.CreateLiveListAsync("Every RPG", library.Filters.ToFilter());
        library.OpenListCommand.Execute(live);
        Assert.Equal(1, live!.Count);

        await fixture.SeedAsync("Pillars of Eternity", genres: ["RPG"]);
        var reloaded = await fixture.LoadAsync();

        // Nothing was written to list_items and nothing needed to be: the rule
        // found the new title on its own.
        Assert.Equal(2, reloaded.Lists.LiveLists.Single().Count);
    }

    [Fact]
    public async Task Editing_an_open_live_list_offers_update_and_revert_by_name()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);
        await fixture.SeedAsync("Hades", genres: ["Action"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");
        var live = await library.Lists.CreateLiveListAsync("Every RPG", library.Filters.ToFilter());
        library.OpenListCommand.Execute(live);
        Assert.False(library.IsLiveListEdited);

        fixture.Check(library, FilterPanelViewModel.GenreKey, "Action");
        Assert.True(library.IsLiveListEdited);

        library.RevertLiveListCommand.Execute(null);
        Assert.False(library.IsLiveListEdited);
        Assert.Equal(["Disco Elysium"], fixture.Titles(library));

        fixture.Check(library, FilterPanelViewModel.GenreKey, "Action");
        await library.UpdateLiveListCommand.ExecuteAsync(null);
        Assert.False(library.IsLiveListEdited);

        var reloaded = await fixture.LoadAsync();
        Assert.Equal(2, reloaded.Lists.LiveLists.Single().Count);
    }

    // ── Leaving a live list ─────────────────────────────────────────────────
    //
    // A live list adds no AND term of its own: opening one POURS its rules into
    // the rail and the panel (§12.2), which is what makes the two kinds of list
    // visibly different and what makes a live list editable in place.
    //
    // The bug these tests exist for is the other half of that bargain. The
    // poured-in rules were indistinguishable from rules the user had set by
    // hand, so clicking "All games" cleared the bucket and left the list's
    // genre, mode and tag terms silently applied — and the user believed they
    // were looking at their whole library while a live list was still cutting
    // it. A list is a PLACE. Leaving takes what the place contributed.

    [Fact]
    public async Task Leaving_a_live_list_by_all_games_takes_its_rules_with_it()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);
        await fixture.SeedAsync("Hades", genres: ["Action"]);
        await fixture.SeedAsync("Tunic", genres: ["Adventure"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");
        var live = await library.Lists.CreateLiveListAsync("Every RPG", library.Filters.ToFilter());

        // Enter it from a clean library, the way the rail does.
        library.SelectBucketCommand.Execute(library.AllGames);
        library.OpenListCommand.Execute(live);
        Assert.Equal(["Disco Elysium"], fixture.Titles(library));
        Assert.False(library.Filters.ToFilter().IsEmpty);

        library.SelectBucketCommand.Execute(library.AllGames);

        // Every rule the list contributed is gone, and the grid says so.
        Assert.Null(library.Lists.Open);
        Assert.True(library.Filters.ToFilter().IsEmpty);
        Assert.Equal(0, library.Filters.ActiveCount);
        Assert.Null(library.SelectedBucket);
        Assert.Equal(string.Empty, library.SearchText);
        Assert.Empty(library.CutChips);
        Assert.False(library.IsCut);
        Assert.Equal(3, library.VisibleTiles.Count);

        // And the rail agrees: "All games" is where you are, and nothing else is.
        Assert.True(library.AllGames.IsSelected);
        Assert.DoesNotContain(library.Buckets, b => b.IsSelected || b.IsRule);
        Assert.DoesNotContain(library.Lists.All, l => l.IsSelected);
    }

    [Fact]
    public async Task Leaving_a_live_list_by_a_bucket_leaves_only_that_bucket()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", minutes: 300, genres: ["RPG"]);
        await fixture.SeedAsync("Pillars of Eternity", minutes: 0, genres: ["RPG"]);
        await fixture.SeedAsync("Hades", minutes: 300, genres: ["Action"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");
        library.SearchText = "eternity";

        // Saved through the cut bar, so the rule set is the one the screen was
        // describing — the typed word included (§11.3).
        library.BeginSaveLiveListCommand.Execute(null);
        library.Prompt!.Text = "Unstarted RPGs";
        await library.Prompt.ConfirmCommand.ExecuteAsync(null);

        Assert.Same(library.Lists.LiveLists.Single(), library.Lists.Open);
        Assert.Equal(["Pillars of Eternity"], fixture.Titles(library));

        var bounced = library.Buckets.Single(b => b.Key == LibraryBuckets.Bounced);
        library.SelectBucketCommand.Execute(bounced);

        // The bucket the user just clicked, and nothing else — not the genre,
        // not the word the list had in its search box.
        Assert.Null(library.Lists.Open);
        Assert.Same(bounced, library.SelectedBucket);
        Assert.True(library.Filters.ToFilter().IsEmpty);
        Assert.Equal(string.Empty, library.SearchText);
        Assert.Equal(["Bounced off"], library.CutChips.Select(c => c.Label));
        Assert.Equal(["Disco Elysium", "Hades"], fixture.Titles(library).Order());

        // One Volt edge in the rail, on the row the user clicked.
        Assert.True(bounced.IsSelected);
        Assert.False(bounced.IsRule);
        Assert.False(library.AllGames.IsSelected);
    }

    [Fact]
    public async Task Opening_a_second_live_list_does_not_inherit_the_first_ones_rules()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);
        await fixture.SeedAsync("Hades", genres: ["Action"]);

        var library = await fixture.LoadAsync();

        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");
        var rpgs = await library.Lists.CreateLiveListAsync("RPGs", library.Filters.ToFilter());
        library.Filters.ClearCommand.Execute(null);

        fixture.Check(library, FilterPanelViewModel.GenreKey, "Action");
        var action = await library.Lists.CreateLiveListAsync("Action", library.Filters.ToFilter());
        library.Filters.ClearCommand.Execute(null);

        library.OpenListCommand.Execute(rpgs);
        Assert.Equal(["Disco Elysium"], fixture.Titles(library));

        library.OpenListCommand.Execute(action);

        // Not the intersection of the two — the second list, on its own.
        Assert.Same(action, library.Lists.Open);
        Assert.Equal(["Hades"], fixture.Titles(library));
        Assert.Equal(action!.Filter, library.Filters.ToFilter());
        Assert.False(library.IsLiveListEdited);
        Assert.Single(library.Lists.All, l => l.IsSelected);
    }

    /// <summary>
    /// The rail row toggles, which is how the user found the bug: clicking the
    /// live list you are already in reads as "turn this off", and it has to
    /// actually turn it off.
    /// </summary>
    [Fact]
    public async Task Clicking_the_open_live_list_again_leaves_it_and_its_rules()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);
        await fixture.SeedAsync("Hades", genres: ["Action"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");
        var live = await library.Lists.CreateLiveListAsync("RPGs", library.Filters.ToFilter());
        library.Filters.ClearCommand.Execute(null);

        var shell = new MainWindowViewModel(library, fixture.CreateMergeQueue());
        shell.SelectListCommand.Execute(live);
        Assert.Same(live, library.Lists.Open);

        shell.SelectListCommand.Execute(live);

        Assert.Null(library.Lists.Open);
        Assert.True(library.Filters.ToFilter().IsEmpty);
        Assert.Equal(2, library.VisibleTiles.Count);
    }

    /// <summary>
    /// The rules stay editable while you are standing in the list — that is
    /// §12.2's whole point — and the cut bar says, rule by rule, which of them
    /// the list brought and which the user has added on top.
    /// </summary>
    [Fact]
    public async Task Inside_a_live_list_the_bar_says_which_rules_are_the_lists()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", minutes: 300, genres: ["RPG"]);
        await fixture.SeedAsync("Hades", minutes: 300, genres: ["Action"]);

        var library = await fixture.LoadAsync();
        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");
        var live = await library.Lists.CreateLiveListAsync("RPGs", library.Filters.ToFilter());
        library.Filters.ClearCommand.Execute(null);

        library.OpenListCommand.Execute(live);

        // The place leads the bar and names its kind; the rule it brought is
        // marked as the list's, not as a selection the user made.
        var context = library.CutChips[0];
        Assert.Equal(FilterChipOrigin.Context, context.Origin);
        Assert.Equal("RPGs", context.Label);
        Assert.Equal("LIVE LIST", context.Dimension);
        Assert.Equal("Leave this list", context.RemoveTip);

        var brought = library.CutChips.Single(c => c.Label == "RPG");
        Assert.Equal(FilterChipOrigin.List, brought.Origin);
        Assert.False(brought.IsUserRule);
        Assert.Equal("GENRE: RPG — from this live list", brought.Description);

        // Now the user adds one of their own, in place, and it reads as theirs.
        library.SelectBucketCommand.Execute(
            library.Buckets.Single(b => b.Key == LibraryBuckets.Bounced));
        Assert.Null(library.Lists.Open);

        library.OpenListCommand.Execute(live);
        fixture.Check(library, FilterPanelViewModel.GenreKey, "Action");

        var added = library.CutChips.Single(c => c.Label == "Action");
        Assert.Equal(FilterChipOrigin.Unsaved, added.Origin);
        Assert.True(added.IsUserRule);
        Assert.Equal("GENRE: Action — yours, not saved to this list", added.Description);
        Assert.True(library.IsLiveListEdited);

        // And it is still an edit, not a fork: Update writes it to the list.
        await library.UpdateLiveListCommand.ExecuteAsync(null);
        Assert.False(library.IsLiveListEdited);
        Assert.All(
            library.CutChips.Where(c => !c.IsContext),
            c => Assert.Equal(FilterChipOrigin.List, c.Origin));
    }

    /// <summary>
    /// While a list is open, the rail's Volt edge is on the list. A bucket in
    /// force is drawn as a rule instead — the same fill, no Volt — because two
    /// rows claiming to be where you are is what let the poured-in rules read
    /// as the user's own.
    /// </summary>
    [Fact]
    public async Task Inside_a_live_list_the_rail_marks_the_list_and_not_the_bucket()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Disco Elysium", minutes: 300, genres: ["RPG"]);

        var library = await fixture.LoadAsync();
        library.SelectBucketCommand.Execute(
            library.Buckets.Single(b => b.Key == LibraryBuckets.Bounced));
        var live = await library.Lists.CreateLiveListAsync("Bounced RPGs", library.Filters.ToFilter() with
        {
            Buckets = [LibraryBuckets.Bounced],
        });

        library.SelectBucketCommand.Execute(library.AllGames);
        library.OpenListCommand.Execute(live);

        var bounced = library.Buckets.Single(b => b.Key == LibraryBuckets.Bounced);
        Assert.Same(bounced, library.SelectedBucket);
        Assert.False(bounced.IsSelected);
        Assert.True(bounced.IsRule);
        Assert.False(library.AllGames.IsSelected);
        Assert.Single(library.Lists.All, l => l.IsSelected);

        // Exactly one Volt edge across the whole rail, and it is the list's.
        Assert.Equal(
            1,
            library.Buckets.Count(b => b.IsSelected)
                + (library.AllGames.IsSelected ? 1 : 0)
                + library.Lists.All.Count(l => l.IsSelected));
    }

    /// <summary>
    /// The manual case is unchanged and must stay so: a hand-built list is one
    /// more AND term, so the rail, the panel and the search box all still work
    /// inside it — and because none of those rules came from the list, leaving
    /// it takes none of them.
    /// </summary>
    [Fact]
    public async Task Leaving_a_manual_list_keeps_the_rules_the_user_set_inside_it()
    {
        using var fixture = new ListFixture();
        var disco = await fixture.SeedAsync("Disco Elysium", genres: ["RPG"]);
        var hades = await fixture.SeedAsync("Hades", genres: ["Action"]);
        await fixture.SeedAsync("Pillars of Eternity", genres: ["RPG"]);

        var library = await fixture.LoadAsync();
        var list = await library.Lists.CreateListAsync("Friday night", [disco, hades]);
        library.OpenListCommand.Execute(list);

        fixture.Check(library, FilterPanelViewModel.GenreKey, "RPG");
        Assert.Equal(["Disco Elysium"], fixture.Titles(library));

        // The list chip is the context; the genre is the user's own.
        Assert.Equal(FilterChipOrigin.Context, library.CutChips[0].Origin);
        Assert.Equal("LIST", library.CutChips[0].Dimension);
        Assert.Equal(FilterChipOrigin.User, library.CutChips.Single(c => c.Label == "RPG").Origin);

        library.SelectBucketCommand.Execute(library.AllGames);

        // Out of the list, still filtered by the rule the user set inside it.
        Assert.Null(library.Lists.Open);
        Assert.Equal([FilterPanelViewModel.GenreKey], ActiveGroups(library));
        Assert.Equal(["Disco Elysium", "Pillars of Eternity"], fixture.Titles(library).Order());
    }

    private static IEnumerable<string> ActiveGroups(LibraryViewModel library)
        => library.Filters.Groups.Where(g => g.Checked.Any()).Select(g => g.Key);

    // ── Rename and delete ───────────────────────────────────────────────────

    [Fact]
    public async Task Renaming_survives_a_reload_and_keeps_the_rail_alphabetical()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");

        var library = await fixture.LoadAsync();
        await library.Lists.CreateListAsync("Zebra", [hades]);
        var second = await library.Lists.CreateListAsync("Middle", [hades]);
        library.OpenListCommand.Execute(second);

        library.BeginRenameListCommand.Execute(null);
        Assert.Equal("Middle", library.Prompt!.Text);

        library.Prompt.Text = "Aardvark";
        await library.Prompt.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(["Aardvark", "Zebra"], library.Lists.Lists.Select(l => l.Name));

        var reloaded = await fixture.LoadAsync();
        Assert.Equal(["Aardvark", "Zebra"], reloaded.Lists.Lists.Select(l => l.Name));
    }

    [Fact]
    public async Task Deleting_a_list_asks_first_and_keeps_the_titles()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");

        var library = await fixture.LoadAsync();
        var list = await library.Lists.CreateListAsync("Friday night", [hades]);
        library.OpenListCommand.Execute(list);

        library.BeginDeleteListCommand.Execute(null);
        Assert.NotNull(library.Prompt);
        Assert.Equal("Delete “Friday night”?", library.Prompt!.Question);
        Assert.Equal("The titles stay in your library.", library.Prompt.Note);
        Assert.True(library.Prompt.IsDestructive);

        await library.Prompt.ConfirmCommand.ExecuteAsync(null);

        Assert.True(library.Lists.HasNoLists);
        Assert.Null(library.Lists.Open);

        var reloaded = await fixture.LoadAsync();
        Assert.True(reloaded.Lists.HasNoLists);
        Assert.Single(reloaded.VisibleTiles);
    }

    [Fact]
    public async Task Add_to_list_offers_the_lists_that_exist_and_a_new_one()
    {
        using var fixture = new ListFixture();
        var hades = await fixture.SeedAsync("Hades");
        var celeste = await fixture.SeedAsync("Celeste");

        var library = await fixture.LoadAsync();
        await library.Lists.CreateListAsync("Friday night", [hades]);

        library.SelectedTiles = [.. library.VisibleTiles.Where(t => t.ReleaseId == celeste)];
        Assert.Equal("Add to list", library.AddToListLabel);
        library.BeginAddToListCommand.Execute(null);

        Assert.NotNull(library.Prompt);
        Assert.Equal("Add Celeste to", library.Prompt!.Question);
        Assert.Equal(["Friday night"], library.Prompt.Choices.Select(c => c.Name));
        Assert.Equal("New list", library.Prompt.ConfirmLabel);

        await library.Prompt.ChooseCommand.ExecuteAsync(library.Prompt.Choices[0]);

        Assert.Null(library.Prompt);
        Assert.Equal([hades, celeste], library.Lists.Lists.Single().ReleaseIds);
    }

    [Fact]
    public async Task The_button_names_the_number_once_there_is_more_than_one()
    {
        using var fixture = new ListFixture();
        await fixture.SeedAsync("Hades");
        await fixture.SeedAsync("Celeste");

        var library = await fixture.LoadAsync();
        library.SelectedTiles = [.. library.VisibleTiles];

        Assert.Equal("Add 2 to list", library.AddToListLabel);
    }

    private sealed class ListFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private readonly Dictionary<string, long> _releaseByTitle = [];
        private int _appId = 910000;

        public ListFixture()
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

        public async Task<LibraryViewModel> LoadAsync()
        {
            var library = new LibraryViewModel(
                Queries, Ownerships, Releases, Works, Updates,
                covers: null, ramp: null, snapshots: null,
                facets: Facets, lists: GameLists);
            await library.LoadCommand.ExecuteAsync(null);
            return library;
        }

        /// <summary>The shell owns the rail's list command, which is the one that toggles.</summary>
        public MergeQueueViewModel CreateMergeQueue()
            => new(new MergeCandidateRepository(_db.Factory), Releases, Works);

        public IEnumerable<string> Titles(LibraryViewModel library)
            => library.VisibleTiles.Select(t => t.Title);

        public void Check(LibraryViewModel library, string group, string label)
            => library.Filters.Groups
                .Single(g => g.Key == group)
                .AllOptions.Single(o => o.Label == label)
                .IsChecked = true;

        public async Task<long> SeedAsync(
            string title,
            long minutes = 0,
            string[]? genres = null,
            string? appType = null)
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = title,
                SteamAppType = appType,
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
            });

            await Plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = minutes,
                LastPlayedAt = null,
                Source = "steam_localconfig",
                ObservedAt = Now,
            });

            if (genres is { Length: > 0 })
            {
                await Facets.SetWorkFacetsAsync(
                    workId,
                    [.. genres.Select(g => new FacetAssignment(FacetKinds.Genre, g))]);
            }

            _releaseByTitle[title] = releaseId;
            return releaseId;
        }

        public void Dispose() => _db.Dispose();
    }
}
