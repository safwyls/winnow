using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class DetailsRefreshParityTests
{
    [AvaloniaTheory]
    [InlineData(false, WorkFields.Summary, "A saved summary.")]
    [InlineData(true, WorkFields.Summary, "A saved summary.")]
    [InlineData(false, WorkFields.Publisher, "A saved publisher")]
    [InlineData(true, WorkFields.Publisher, "A saved publisher")]
    [InlineData(false, WorkFields.FirstReleaseYear, "2017")]
    [InlineData(true, WorkFields.FirstReleaseYear, "2017")]
    public async Task Saving_metadata_refreshes_visible_facts_and_year_rules_without_replacing_other_drafts(
        bool fullscreen, string field, string value)
    {
        using var fixture = new Fixture();
        await fixture.LoadAsync();
        var library = fixture.Library;
        var details = library.Details!;
        var editor = details.MetadataEditor!;
        await editor.OpenCommand.ExecuteAsync(null);
        var row = editor.Rows.Single(candidate => candidate.Field == field);
        var other = editor.Rows.Single(candidate => candidate.Field == WorkFields.Name);
        other.Draft = "An unfinished title";
        library.Filters.Apply(new LibraryFilter { YearFrom = 2006, YearTo = 2006 });
        var live = await library.Lists.CreateLiveListAsync("2006 only", new LibraryFilter { YearFrom = 2006, YearTo = 2006 });
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell);
        using var tv = new FullscreenDetailsFieldPage(context, row);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? tv : view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var fieldBox = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box =>
                AutomationProperties.GetName(box) == row.FieldAutomationName && box.IsEffectivelyVisible);
            fieldBox.Text = value;
            Dispatcher.UIThread.RunJobs();
            var save = Assert.Single(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible &&
                (fullscreen ? Equals(button.Content, row.SaveLabel) : ReferenceEquals(button.Command, row.SaveCommand)));
            Assert.True(save.Focus());
            Activate(window, fullscreen ? tv : null);
            await row.SaveCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(row.Problem);
            Assert.Same(details, library.Details);
            Assert.Same(editor, details.MetadataEditor);
            Assert.Equal("An unfinished title", other.Draft);
            Assert.True(editor.IsOpen);
            Assert.Same(library.AllTiles.Single(tile => tile.OwnershipId == 1), details.Tile);
            Assert.Equal(field == WorkFields.Summary ? value : "Original summary", details.Summary);
            Assert.Equal(field == WorkFields.Publisher ? value : "Original publisher", details.Publisher);
            Assert.Equal(field == WorkFields.FirstReleaseYear ? 2017 : 2006, details.Tile.ReleaseYear);
            Assert.Equal(details.Tile.ReleaseYear, details.Tile.Row.FirstReleaseYear);
            Assert.Equal(field == WorkFields.FirstReleaseYear ? 0 : 1, library.VisibleTiles.Count);
            Assert.Equal(field == WorkFields.FirstReleaseYear ? 0 : 1,
                library.Lists.All.Single(list => list.Id == live!.Id).Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Background_reads_refresh_updates_history_art_and_reception_while_journal_drafts_survive(bool fullscreen)
    {
        using var fixture = new Fixture();
        var oldSession = await fixture.AddSessionAsync(Fixture.Now.AddDays(-90), "Old saved note");
        await fixture.LoadAsync();
        var library = fixture.Library;
        var details = library.Details!;
        var tracker = details.Tracker;
        tracker.IsTrackedSessions = true;
        details.SelectedTabIndex = 3;
        var journal = details.Journal!;
        var entry = Assert.Single(journal.Entries);
        entry.EditCommand.Execute(null);
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell);
        using var tv = new FullscreenDetailsPage(context, details, selectedSection: 2);
        using var notePage = new FullscreenDetailsJournalPage(context, entry);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? notePage : view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var note = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box =>
                AutomationProperties.GetName(box) == "Journal note" && box.IsEffectivelyVisible);
            note.Text = "My unfinished note";
            Assert.True(note.Focus());
            var newLastPlay = Fixture.Now.AddDays(-5);
            await fixture.Plays.InsertAsync(new PlayRecord { OwnershipId = 1, PlaytimeMinutes = 180,
                LastPlayedAt = newLastPlay, ObservedAt = Fixture.Now, Source = "steam" });
            await fixture.Snapshots.InsertAsync(new PlaytimeSnapshot { OwnershipId = 1, PlaytimeMinutes = 180, ObservedAt = Fixture.Now });
            await fixture.AddSessionAsync(newLastPlay, "A new saved note");
            await fixture.AddPatchAsync(Fixture.Now.AddDays(-1), "New patch");
            await fixture.Ratings.UpsertAsync(new WorkRating { WorkId = 1, Source = RatingSources.IgdbUsers,
                Score = 90, RatingCount = 20, ObservedAt = Fixture.Now });
            await fixture.Images.UpsertAsync(new WorkImages { WorkId = 1, Source = ImageSources.Igdb,
                Kind = ImageKinds.Screenshot, ImageIds = "newshot", ObservedAt = Fixture.Now });
            await library.LoadCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(details, library.Details);
            Assert.Same(tracker, details.Tracker);
            Assert.True(tracker.IsTrackedSessions);
            Assert.Equal(newLastPlay, tracker.LastPlayedUtc);
            Assert.Equal(newLastPlay, details.LastPlayedUtc);
            Assert.Same(journal, details.Journal);
            Assert.Same(entry, journal.Entries.Single(row => row.SessionId == oldSession));
            Assert.Equal(2, journal.Entries.Count);
            Assert.Equal("My unfinished note", entry.DraftNote);
            Assert.True(note.IsFocused);
            Assert.Equal(1, details.UnreadUpdateCount);
            Assert.Equal("1 unread update", tracker.UpdateSummary);
            Assert.Equal(2, details.Updates.Count);
            Assert.Equal("90", Assert.Single(details.Reception!.Figures).Value);
            Assert.Single(details.Screenshots!.Shots);
            Assert.Equal("newshot", details.Screenshots.Shots[0].Key.Id);

            // Render Updates, focus an existing row, and add a newer row above it.
            if (fullscreen) { window.Content = tv; tv.Handle(GamepadButtons.PagePrevious); }
            else details.SelectedTabIndex = 2;
            Dispatcher.UIThread.RunJobs();
            var existing = details.Updates.First(row => row.IsAnnouncement);
            var currentButton = window.GetVisualDescendants().OfType<Button>().First(button =>
                button.IsEffectivelyVisible && (fullscreen ? AutomationProperties.GetName(button) == existing.AutomationName
                    : ReferenceEquals(button.DataContext, existing)));
            Assert.True(currentButton.Focus());
            await fixture.AddPatchAsync(Fixture.Now, "Newest patch");
            await library.LoadCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, details.UnreadUpdateCount);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text?.Contains("Newest patch", StringComparison.Ordinal) == true && text.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.IsFocused &&
                (fullscreen ? AutomationProperties.GetName(button) == existing.AutomationName
                    : button.DataContext is UpdateEventViewModel row && row.ReleaseId == existing.ReleaseId &&
                        row.OccurredAtUtc == existing.OccurredAtUtc && row.IsAnnouncement));
            Assert.Equal(fullscreen ? 1 : 2, fullscreen ? tv.SelectedSection : details.SelectedTabIndex);
        }
        finally { window.Close(); }
    }

    private static void Activate(Window window, FullscreenPage? page)
    {
        if (page is not null) page.Handle(GamepadButtons.Accept);
        else
        {
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        private readonly TempDatabase _db = new();
        public Fixture()
        {
            LibraryReadFixtures.Seed(_db, 2);
            using (var connection = _db.Factory.Open()) connection.Execute("""
                UPDATE works SET first_release_year=2006, summary='Original summary', publisher='Original publisher' WHERE id=1;
                UPDATE works SET first_release_year=1990 WHERE id=2;
                """);
            var works = new WorkRepository(_db.Factory);
            var updates = new UpdateEventRepository(_db.Factory);
            Sessions = new SessionRepository(_db.Factory);
            Plays = new PlayRecordRepository(_db.Factory);
            Snapshots = new PlaytimeSnapshotRepository(_db.Factory);
            Ratings = new WorkRatingRepository(_db.Factory);
            Images = new WorkImageRepository(_db.Factory);
            Library = new LibraryViewModel(new LibraryQueryRepository(_db.Factory), new OwnershipRepository(_db.Factory),
                new ReleaseRepository(_db.Factory), works, updates, snapshots: Snapshots,
                sessions: Sessions, lists: new GameListRepository(_db.Factory), workRatings: Ratings, workImages: Images,
                metadataEdits: new WorkMetadataEditService(works, new WorkFieldSourceRepository(_db.Factory)));
        }
        public LibraryViewModel Library { get; }
        public SessionRepository Sessions { get; }
        public PlayRecordRepository Plays { get; }
        public PlaytimeSnapshotRepository Snapshots { get; }
        public WorkRatingRepository Ratings { get; }
        public WorkImageRepository Images { get; }
        public async Task LoadAsync()
        {
            await Plays.InsertAsync(new PlayRecord { OwnershipId = 1, PlaytimeMinutes = 120,
                LastPlayedAt = Now.AddDays(-90), ObservedAt = Now.AddDays(-30), Source = "steam" });
            await Snapshots.InsertAsync(new PlaytimeSnapshot { OwnershipId = 1, PlaytimeMinutes = 120, ObservedAt = Now.AddDays(-30) });
            await Library.LoadCommand.ExecuteAsync(null);
            await Library.OpenDetailsCommand.ExecuteAsync(Library.AllTiles.Single(tile => tile.OwnershipId == 1));
        }
        public async Task<long> AddSessionAsync(DateTime at, string note)
        {
            var id = await Sessions.InsertAsync(new Session { OwnershipId = 1, StartedAt = at,
                EndedAt = at.AddHours(1), DurationSeconds = 3600, DetectionMethod = "manual" });
            await Sessions.SetNoteAsync(new SessionNote { SessionId = id, Note = note, Rating = 3 });
            return id;
        }
        public async Task AddPatchAsync(DateTime at, string title)
        {
            var updates = new UpdateEventRepository(_db.Factory);
            await updates.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.BuildPush, OccurredAt = at, BuildId = at.Ticks.ToString() });
            await updates.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = at, Title = title,
                Url = "https://store.steampowered.com/news/app/1/view/" + at.Ticks });
        }
        public void Dispose() { Library.CloseDetailsCommand.Execute(null); _db.Dispose(); }
    }
}
