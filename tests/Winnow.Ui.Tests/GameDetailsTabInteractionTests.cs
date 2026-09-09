using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class GameDetailsTabInteractionTests
{
    [AvaloniaTheory]
    [InlineData(1200, 640)]
    [InlineData(1280, 820)]
    public void Populated_tabs_keep_the_header_pinned_and_all_content_reachable(int width, int height)
    {
        using var model = RichModel();
        using var fixture = new DetailsFixture(model, width, height);
        var card = fixture.Find<Border>("Card");
        var more = fixture.Find<Button>("MoreActionsButton");
        var initialHeader = BoundsIn(fixture.Find<Grid>("DetailsHeader"), fixture.Window);
        AssertContained(BoundsIn(card, fixture.Window), new Rect(fixture.Window.ClientSize));

        string[] tabs = ["OverviewTab", "ActivityTab", "LibraryTab"];
        string[] scrolls = ["OverviewScroll", "ActivityScroll", "LibraryScroll"];
        for (var index = 0; index < tabs.Length; index++)
        {
            fixture.Click(fixture.Find<TabItem>(tabs[index]));
            Assert.Equal(index, model.SelectedTabIndex);
            var scroll = fixture.Find<ScrollViewer>(scrolls[index]);
            Assert.True(scroll.IsEffectivelyVisible);
            Assert.InRange(scroll.Viewport.Height, 80, card.Bounds.Height);
            AssertContained(BoundsIn(scroll, card), new Rect(card.Bounds.Size));
            Assert.Equal(initialHeader, BoundsIn(fixture.Find<Grid>("DetailsHeader"), fixture.Window));
            Capture(fixture.Window, $"details-{tabs[index]}-{width}x{height}");

            if (index == 0) fixture.Click(fixture.Find<Expander>("ExpansionsDisclosure"));
            if (index == 2) fixture.Click(fixture.Find<Expander>("TechnicalFactsDisclosure"));

            scroll.ScrollToEnd();
            Flush();
            Assert.Equal(initialHeader, BoundsIn(fixture.Find<Grid>("DetailsHeader"), fixture.Window));
            Assert.InRange(scroll.Offset.Y, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height) + 1);
            var last = scroll.GetVisualDescendants().OfType<Control>()
                .Where(control => control.IsEffectivelyVisible && control.Bounds.Height > 0
                    && control is Button or TextBlock or CheckBox)
                .OrderBy(control => BoundsIn(control, scroll).Bottom)
                .LastOrDefault();
            Assert.NotNull(last);
            AssertContained(BoundsIn(last!, scroll), new Rect(scroll.Bounds.Size));
            Capture(fixture.Window, $"details-{tabs[index]}-bottom-{width}x{height}");
            var offset = scroll.Offset;
            fixture.Click(fixture.Find<TabItem>(tabs[(index + 1) % tabs.Length]));
            fixture.Click(fixture.Find<TabItem>(tabs[index]));
            Assert.Equal(offset, scroll.Offset);
        }
        fixture.Click(more);
        Capture(fixture.Window, $"details-menu-{width}x{height}");
    }

    [AvaloniaFact]
    public void Keyboard_arrows_switch_tabs_and_leave_focus_on_the_selected_tab()
    {
        using var model = SparseModel();
        using var fixture = new DetailsFixture(model);
        var overview = fixture.Find<TabItem>("OverviewTab");
        Assert.Equal(0, model.SelectedTabIndex);
        Assert.True(overview.Focus(NavigationMethod.Tab));
        fixture.Press(PhysicalKey.ArrowRight);
        Flush();
        Assert.Equal(1, model.SelectedTabIndex);
        Assert.True(fixture.Find<TabItem>("ActivityTab").IsKeyboardFocusWithin);
        var focusRing = fixture.Find<TabItem>("ActivityTab").GetVisualDescendants()
            .OfType<Border>().Single(border => border.Name == "TabFocusBorder");
        Assert.Equal(new Thickness(2), focusRing.BorderThickness);
        Assert.True(fixture.View.TryFindResource("Volt", out var volt));
        Assert.Same(volt, focusRing.BorderBrush);
        fixture.Press(PhysicalKey.ArrowRight);
        Flush();
        Assert.Equal(2, model.SelectedTabIndex);
        Assert.True(fixture.Find<TabItem>("LibraryTab").IsKeyboardFocusWithin);
        fixture.Press(PhysicalKey.ArrowLeft);
        Flush();
        Assert.Equal(1, model.SelectedTabIndex);
        fixture.Press(PhysicalKey.End);
        Assert.Equal(2, model.SelectedTabIndex);
        fixture.Press(PhysicalKey.Home);
        Assert.Equal(0, model.SelectedTabIndex);
    }

    [AvaloniaFact]
    public void Sparse_games_keep_all_tabs_and_do_not_claim_unread_updates()
    {
        using var model = SparseModel();
        using var fixture = new DetailsFixture(model);
        var tabs = fixture.Find<TabControl>("DetailsTabs");
        Assert.Equal(3, tabs.Items.Count);
        var shortcut = fixture.Find<Button>("UpdatesShortcutButton");
        Assert.False(shortcut.IsEffectivelyVisible);
        foreach (var name in new[] { "OverviewTab", "ActivityTab", "LibraryTab" })
        {
            var tab = fixture.Find<TabItem>(name);
            Assert.True(tab.IsEnabled);
            fixture.Click(tab);
            Assert.True(tab.IsSelected);
        }
        Assert.False(fixture.Find<Expander>("TechnicalFactsDisclosure").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Update_shortcut_opens_activity_without_replacing_the_details_model()
    {
        var model = PreviewData.GameDetails;
        model.SelectedTabIndex = 0;
        using var fixture = new DetailsFixture(model);
        var shortcut = fixture.Find<Button>("UpdatesShortcutButton");
        Assert.True(shortcut.IsEffectivelyVisible);
        fixture.Click(shortcut);
        Assert.Equal(1, model.SelectedTabIndex);
        Assert.Same(model, fixture.View.DataContext);
        Assert.True(fixture.Find<ScrollViewer>("ActivityScroll").IsEffectivelyVisible);
        model.SelectedTabIndex = 0;
    }

    [AvaloniaFact]
    public void Long_overview_keeps_its_scroll_position_when_activity_is_opened()
    {
        var now = DateTime.UtcNow;
        var work = new Work
        {
            Id = 1,
            Name = "A game with a long description",
            Summary = string.Join("\n\n", Enumerable.Repeat("This paragraph explains the places to explore and the choices to make in the game.", 40)),
        };
        using var model = new GameDetailsViewModel(TileFixture.Tile(now, work: work, title: work.Name),
            "Never played", [], now);
        using var fixture = new DetailsFixture(model);
        var overview = fixture.Find<ScrollViewer>("OverviewScroll");
        fixture.Click(fixture.Find<Button>("SummaryDisclosureButton"));
        overview.ScrollToEnd();
        Flush();
        Assert.True(overview.Offset.Y > 0);
        var offset = overview.Offset;
        fixture.Click(fixture.Find<TabItem>("ActivityTab"));
        fixture.Click(fixture.Find<TabItem>("OverviewTab"));
        Assert.Equal(offset, overview.Offset);
    }

    [AvaloniaFact]
    public async Task Focused_editor_returns_to_the_selected_tab_and_preserves_unsaved_fields()
    {
        var service = new MetadataService();
        using var editor = new GameMetadataEditorViewModel(service, 1, service.Snapshot);
        using var model = SparseModel(editor);
        model.SelectedTabIndex = 2;
        using var fixture = new DetailsFixture(model);
        await editor.OpenCommand.ExecuteAsync(null);
        Flush();
        Assert.False(fixture.Find<TabControl>("DetailsTabs").IsEffectivelyVisible);
        Capture(fixture.Window, "details-focused-editor-1200x640");
        var field = fixture.View.GetVisualDescendants().OfType<TextBox>()
            .Single(box => box.DataContext is MetadataTextRowViewModel row && row.Field == WorkFields.Name);
        field.Text = "A name still being considered";
        Flush();
        fixture.Click(fixture.Find<Button>("BackToDetailsButton"));
        Assert.Equal(2, model.SelectedTabIndex);
        Assert.True(fixture.Find<TabControl>("DetailsTabs").IsEffectivelyVisible);
        Assert.True(fixture.Find<Button>("MoreActionsButton").IsKeyboardFocusWithin);

        await editor.OpenCommand.ExecuteAsync(null);
        Flush();
        var row = Assert.Single(editor.Rows, candidate => candidate.Field == WorkFields.Name);
        Assert.Equal("A name still being considered", row.Draft);
        Assert.Equal(0, service.Reads);
        Assert.Equal(0, service.Writes);
        fixture.Press(PhysicalKey.Escape);
        Flush();
        Assert.True(fixture.Find<TabControl>("DetailsTabs").IsEffectivelyVisible);
        Assert.Equal(2, model.SelectedTabIndex);
        Assert.Equal("A name still being considered", row.Draft);
    }

    [AvaloniaFact]
    public async Task Wrong_game_menu_focuses_search_and_returns_without_losing_query_or_results()
    {
        var service = new AssignmentService();
        using var match = new GameIgdbMatchViewModel(service, 1, "An unplayed game");
        var now = DateTime.UtcNow;
        using var model = new GameDetailsViewModel(TileFixture.Tile(now, title: "An unplayed game"),
            "Never played", [], now, igdbMatch: match) { SelectedTabIndex = 2 };
        using var fixture = new DetailsFixture(model);
        Assert.False(fixture.Find<Expander>("TechnicalFactsDisclosure").IsEffectivelyVisible);

        fixture.Click(fixture.Find<Button>("MoreActionsButton"));
        var menuItem = fixture.Find<MenuItem>("WrongGameItem");
        Assert.True(menuItem.IsKeyboardFocusWithin);
        fixture.Press(PhysicalKey.Enter);
        var query = fixture.Find<TextBox>("MatchQueryField");
        Assert.True(query.IsKeyboardFocusWithin);
        query.Text = "Astral cartographers";
        fixture.Press(PhysicalKey.Enter);
        await match.SearchCommand.ExecutionTask!;
        Flush();
        var candidates = match.Candidates;
        Assert.Equal(5, candidates.Count);
        Capture(fixture.Window, "details-focused-match-1200x640");

        fixture.Click(fixture.Find<Button>("BackToDetailsButton"));
        Assert.Equal(2, model.SelectedTabIndex);
        Assert.True(fixture.Find<Button>("MoreActionsButton").IsKeyboardFocusWithin);
        fixture.Click(fixture.Find<Button>("MoreActionsButton"));
        fixture.Press(PhysicalKey.Enter);
        Assert.True(query.IsKeyboardFocusWithin);
        Assert.Equal("Astral cartographers", query.Text);
        Assert.Same(candidates, match.Candidates);
        Assert.Equal(1, service.Searches);
        fixture.Press(PhysicalKey.Escape);
        Assert.True(fixture.Find<TabControl>("DetailsTabs").IsEffectivelyVisible);
        Assert.Equal(2, model.SelectedTabIndex);
        Assert.Same(candidates, match.Candidates);
    }

    private static GameDetailsViewModel SparseModel(GameMetadataEditorViewModel? editor = null)
    {
        var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        return new GameDetailsViewModel(TileFixture.Tile(now, title: "An unplayed game"),
            "Never played", [], now, metadataEditor: editor);
    }

    [AvaloniaFact]
    public void Long_title_publisher_and_three_store_header_keep_actions_inside_the_card()
    {
        using var model = RichModel(longTitle: true);
        using var fixture = new DetailsFixture(model);
        var header = fixture.Find<Grid>("DetailsHeader");
        foreach (var name in new[] { "LaunchButton", "AddToListButton", "MoreActionsButton", "CloseButton" })
        {
            var button = fixture.Find<Button>(name);
            Assert.True(button.IsEffectivelyVisible);
            AssertContained(BoundsIn(button, header), new Rect(header.Bounds.Size));
        }
        Assert.True(fixture.Find<Control>("HeaderPlayGlyph").IsEffectivelyVisible);
        Assert.True(fixture.Find<Control>("HeaderAddToListGlyph").IsEffectivelyVisible);
        Assert.InRange(fixture.Find<ScrollViewer>("OverviewScroll").Viewport.Height, 80, 640);
        Capture(fixture.Window, "details-long-header-1200x640");
    }

    private static GameDetailsViewModel RichModel(bool longTitle = false)
    {
        var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var played = now.AddYears(-3);
        var work = new Work
        {
            Id = 1,
            Name = longTitle ? "The Astral Cartographers: Echoes Beyond the Forgotten Constellations — Complete Collection" : "The Astral Cartographers",
            FirstReleaseYear = 2019,
            Publisher = longTitle ? "The Independent Cartographic Society and the Interstellar Exploration Cooperative" : "Northstar Studio",
            Summary = "Set out from a quiet observatory to chart a sky that changes every night. Follow old signals, rebuild your ship, and find the people who left their stories among the stars.\n\n"
                + "Each expedition offers a new route home. You can trade discoveries with other travellers, restore abandoned stations, or spend an evening exploring a single unfamiliar world. The choices you make change which stories you find.",
        };
        var ownerships = new[] { "steam", "gog", "epic" }.Select((store, index) => new Ownership
        {
            Id = index + 1, ReleaseId = index + 1, Store = store, Installed = true,
            InstallPath = @"C:\Games\The Astral Cartographers\Library\Installed games\Complete edition",
            AcquiredAt = now.AddYears(-5), LicenseType = "gift",
        }).ToArray();
        var entries = ownerships.Select(ownership => TileEntry.For(ownership.Id, ownership.ReleaseId, 1, ownership.Store,
            playtimeMinutes: 740, lastPlayedAt: played, ownership: ownership,
            steamAppId: ownership.Store == "steam" ? "12345" : null)).ToArray();
        var events = Enumerable.Range(1, 12).Select(index => new UpdateEvent
        {
            Id = index, ReleaseId = 1, Kind = UpdateEventKinds.Announcement,
            OccurredAt = now.AddDays(-index * 10), Title = $"Expedition {index}: new places to discover and improvements",
            Url = $"https://store.steampowered.com/news/app/12345/view/{index}",
        }).ToArray();
        var tile = TileFixture.Tile(now, entries, 1, LibraryBuckets.StaleButPatched, events[0].OccurredAt,
            title: work.Name, work: work, unreadUpdateCount: events.Length);
        var coverage = IdentityCoverage.For(1, SameGameResolution.Empty, entries.Select(entry => new CoverageEntry
        {
            OwnershipId = entry.OwnershipId, ReleaseId = entry.ReleaseId, WorkId = 1, Title = work.Name,
            Store = entry.Store, PlaytimeMinutes = entry.PlaytimeMinutes, LastPlayedAt = entry.LastPlayedAt,
        }));
        var lists = new GameListsViewModel(Enumerable.Range(1, 6).Select(index => new GameListEntryViewModel(
            new GameListViewModel(new GameList { Id = index, Name = index == 1 ? "Return to these worlds" : $"Weekend expeditions {index}" }),
            index == 1 ? [1L] : [], (_, _) => Task.CompletedTask)).ToArray());
        var journal = new GameJournalViewModel(Enumerable.Range(1, 3).Select(index => new SessionJournalEntry
        {
            SessionId = index, OwnershipId = 1, SessionAt = played.AddDays(-index), Rating = 4,
            Note = "Reached the old observatory. Next time, follow the signal beyond the southern ridge and bring supplies for the return journey.",
        }), true, new JournalDetailsInteractionTests.JournalRepository());
        var expansions = new GameExpansionsViewModel(Enumerable.Range(1, 3).Select(index => new ExpansionRowViewModel(
            index + 10, index + 10, $"The Astral Cartographers: Distant Shores {index}", ["STEAM"], "Steam", 0, null)).ToArray(),
            ungroup: _ => Task.CompletedTask);
        return new GameDetailsViewModel(tile, "Patched", events.Select(update => UpdateEventViewModel.Create(update, played)).ToArray(), now,
            updateEvents: events, coverage: new GameCoverageViewModel(coverage, new Dictionary<long, string> { [1] = work.Name },
                new Dictionary<long, ReleaseAchievementSummary>()), expansions: expansions, lists: lists, ownerships: ownerships,
            journal: journal, addToList: new RelayCommand(() => { }), hideGame: new RelayCommand(() => { }),
            ratings:
            [
                new WorkRating { WorkId = 1, Source = RatingSources.IgdbUsers, Score = 84, RatingCount = 1240, ObservedAt = now },
                new WorkRating { WorkId = 1, Source = RatingSources.IgdbCritics, Score = 87, RatingCount = 43, ObservedAt = now },
                new WorkRating { WorkId = 1, Source = RatingSources.Steam, Score = 92, RatingCount = 18240, ObservedAt = now },
            ],
            images: [new WorkImages { WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Screenshot,
                ImageIds = "aa1,aa2,aa3,aa4,aa5,aa6,aa7,aa8", ObservedAt = now }]);
    }

    private static Rect BoundsIn(Control control, Control ancestor)
        => new(control.TranslatePoint(default, ancestor)!.Value, control.Bounds.Size);

    private static void AssertContained(Rect inner, Rect outer)
    {
        Assert.True(inner.Width > 0 && inner.Height > 0, $"Empty bounds: {inner}");
        Assert.True(inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1
            && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1,
            $"{inner} falls outside {outer}");
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, $"{name}.png"));
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class DetailsFixture : IDisposable
    {
        public GameDetailsView View { get; }
        public Window Window { get; }

        public DetailsFixture(GameDetailsViewModel model, int width = 1200, int height = 640)
        {
            View = new GameDetailsView { DataContext = model };
            Window = new Window { Width = width, Height = height, Content = View };
            Window.Show();
            Flush();
        }

        public T Find<T>(string name) where T : Control => View.FindControl<T>(name)!;

        public void Press(PhysicalKey key)
        {
            Window.KeyPressQwerty(key, RawInputModifiers.None);
            Window.KeyReleaseQwerty(key, RawInputModifiers.None);
            Flush();
        }

        public void Click(Control control)
        {
            control.BringIntoView();
            Flush();
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)!.Value;
            Window.MouseMove(point);
            Window.MouseDown(point, MouseButton.Left);
            Window.MouseUp(point, MouseButton.Left);
            Flush();
        }

        public void Dispose() => Window.Close();
    }

    private sealed class MetadataService : IWorkMetadataEditService
    {
        public WorkMetadataSnapshot Snapshot { get; } = new(1, "An unplayed game", false,
            WorkFields.All.Select(field => new WorkMetadataField(field,
                field == WorkFields.Name ? "An unplayed game" : null, null)).ToList());
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Task<WorkMetadataSnapshot?> GetAsync(long workId, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult<WorkMetadataSnapshot?>(Snapshot);
        }
        public Task<WorkFieldEditOutcome> SetFieldAsync(long workId, string field, string? value, CancellationToken ct = default)
        {
            Writes++;
            return Task.FromResult(WorkFieldEditOutcome.Applied);
        }
        public Task<WorkFieldEditOutcome> ResetFieldAsync(long workId, string field, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<WorkArtEditOutcome> SetArtFromFileAsync(long workId, string field, string filePath, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<WorkArtEditOutcome> SetArtFromUrlAsync(long workId, string field, string url, CancellationToken ct = default)
            => throw new NotSupportedException();
        public CoverKey? ArtKeyFor(string? value) => null;
    }

    private sealed class AssignmentService : IIgdbAssignmentService
    {
        public int Searches { get; private set; }
        public Task<IReadOnlyList<IgdbCandidate>> SearchAsync(string title, CancellationToken ct = default)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<IgdbCandidate>>(Enumerable.Range(1, 5).Select(index => new IgdbCandidate(
                index, $"The Astral Cartographers {index}", null, 2019 + index, ["PC (Microsoft Windows)", "PlayStation 5"])).ToArray());
        }
        public Task<IgdbCandidate?> GetCandidateByIdAsync(long igdbId, CancellationToken ct = default)
            => Task.FromResult<IgdbCandidate?>(null);
        public Task<IgdbAssignmentOutcome> AssignAsync(long workId, long igdbId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IgdbClaimingGame?> FindClaimingGameAsync(long igdbId, CancellationToken ct = default)
            => Task.FromResult<IgdbClaimingGame?>(null);
        public Task<bool> ClearAsync(long workId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<WorkIgdbPin?> GetPinAsync(long workId, CancellationToken ct = default)
            => Task.FromResult<WorkIgdbPin?>(null);
        public Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());
    }
}
