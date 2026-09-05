using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The per-field metadata editor (TASK-119): each field carries its own value
/// and its own source, a manual edit sets one field and leaves every other
/// field tracking its own, and handing a field back to automatic is only
/// available when the user is the source.
///
/// <para>Pure view-model tests over a fake seam -- no database, no network,
/// no Avalonia application. The seam is what makes these possible.</para>
/// </summary>
public sealed class GameMetadataEditorViewModelTests
{
    private const long WorkId = 42;

    // ══ Field sources ══════════════════════════════════════════════════════

    /// <summary>
    /// Saving one field makes the user its source and leaves every other
    /// field's source exactly as the store returned it.
    /// </summary>
    [Fact]
    public async Task Saving_one_field_leaves_every_other_fields_source_alone()
    {
        var service = FakeService.WithIgdbEverywhere();
        var editor = Build(service);

        var publisher = Row(editor, WorkFields.Publisher);
        publisher.Draft = "Eleon Game Studios";
        await publisher.SaveCommand.ExecuteAsync(null);

        Assert.Equal([WorkFields.Publisher], service.Written);
        Assert.Equal(FieldSources.User, publisher.Source);
        Assert.True(publisher.IsUserOwned);

        foreach (var field in WorkFields.All.Where(f => f != WorkFields.Publisher))
        {
            var row = Row(editor, field);
            Assert.Equal(FieldSources.Igdb, row.Source);
            Assert.False(row.IsUserOwned);
        }
    }

    /// <summary>
    /// Saving one field replaces only that row's draft; every other row keeps
    /// whatever the user was typing.
    /// </summary>
    [Fact]
    public async Task Saving_one_field_leaves_every_other_rows_draft_alone()
    {
        var editor = Build(FakeService.WithIgdbEverywhere());

        var summary = Row(editor, WorkFields.Summary);
        summary.Draft = "half-typed";

        var name = Row(editor, WorkFields.Name);
        name.Draft = "Empyrion";
        await name.SaveCommand.ExecuteAsync(null);

        Assert.Equal("half-typed", summary.Draft);
        Assert.Equal("Empyrion", name.Draft);
    }

    /// <summary>A null source reads as on automatic, not as blank or unknown.</summary>
    [Fact]
    public void A_field_no_writer_has_claimed_is_on_automatic_rather_than_blank()
    {
        var editor = Build(new FakeService());

        var row = Row(editor, WorkFields.Summary);

        Assert.Null(row.Source);
        Assert.True(row.IsAutomatic);
        Assert.False(row.IsUserOwned);
        Assert.False(string.IsNullOrWhiteSpace(row.SourceLabel));
        Assert.False(string.IsNullOrWhiteSpace(row.SourceTooltip));
    }

    [Fact]
    public void A_fetch_that_rewrote_every_field_leaves_one_source_on_every_row()
    {
        var editor = Build(FakeService.WithIgdbEverywhere());

        Assert.All(editor.Rows, row => Assert.Equal(FieldSources.Igdb, row.Source));
        Assert.All(editor.Rows, row => Assert.False(row.IsUserOwned));
    }


    // ══ Reset ══════════════════════════════════════════════════════════════

    /// <summary>
    /// A field nobody has claimed and a field a service owns have nothing to
    /// hand back; only a field the user set offers Reset.
    /// </summary>
    [Fact]
    public void Only_a_field_the_user_owns_can_be_handed_back_to_automatic()
    {
        var service = new FakeService();
        service.Set(WorkFields.Name, "Empyrion", FieldSources.User);
        service.Set(WorkFields.Publisher, "Eleon", FieldSources.Igdb);

        var editor = Build(service);

        Assert.True(Row(editor, WorkFields.Name).ResetCommand.CanExecute(null));
        Assert.False(Row(editor, WorkFields.Publisher).ResetCommand.CanExecute(null));
        Assert.False(Row(editor, WorkFields.Summary).ResetCommand.CanExecute(null));
    }

    [Fact]
    public async Task Handing_a_field_back_to_automatic_drops_its_source_and_disarms_the_control()
    {
        var service = new FakeService();
        service.Set(WorkFields.Publisher, "Eleon", FieldSources.User);
        service.Set(WorkFields.Name, "Empyrion", FieldSources.User);

        var editor = Build(service);
        var publisher = Row(editor, WorkFields.Publisher);

        await publisher.ResetCommand.ExecuteAsync(null);

        Assert.Equal([WorkFields.Publisher], service.Reset);
        Assert.Null(publisher.Source);
        Assert.True(publisher.IsAutomatic);
        Assert.False(publisher.ResetCommand.CanExecute(null));
        Assert.True(publisher.HasNote);

        Assert.Equal(FieldSources.User, Row(editor, WorkFields.Name).Source);
    }

    [Fact]
    public async Task A_refused_reset_says_so_and_leaves_the_source_in_place()
    {
        var service = new FakeService { FieldOutcome = WorkFieldEditOutcome.WorkNotFound };
        service.Set(WorkFields.Publisher, "Eleon", FieldSources.User);

        var editor = Build(service);
        var publisher = Row(editor, WorkFields.Publisher);

        await publisher.ResetCommand.ExecuteAsync(null);

        Assert.True(publisher.HasProblem);
        Assert.Equal(
            GameMetadataEditorCopy.ProblemFor(WorkFieldEditOutcome.WorkNotFound),
            publisher.Problem);
        Assert.Equal(FieldSources.User, publisher.Source);
    }

    // ══ Year validation ════════════════════════════════════════════════════

    [Fact]
    public async Task A_release_year_that_is_not_a_year_is_refused_before_anything_is_written()
    {
        var service = new FakeService();
        var editor = Build(service);

        var year = Row(editor, WorkFields.FirstReleaseYear);
        year.Draft = "last tuesday";
        await year.SaveCommand.ExecuteAsync(null);

        Assert.Empty(service.Written);
        Assert.Equal(
            GameMetadataEditorCopy.ProblemFor(WorkFieldEditOutcome.InvalidValue),
            year.Problem);
    }

    [Fact]
    public async Task A_four_digit_release_year_is_written()
    {
        var service = new FakeService();
        var editor = Build(service);

        var year = Row(editor, WorkFields.FirstReleaseYear);
        year.Draft = "2020";
        await year.SaveCommand.ExecuteAsync(null);

        Assert.Equal([WorkFields.FirstReleaseYear], service.Written);
        Assert.Equal(FieldSources.User, year.Source);
        Assert.False(year.HasProblem);
    }

    // ══ Art rows ═══════════════════════════════════════════════════════════

    [Fact]
    public void The_art_rows_are_cover_and_background_and_nothing_else()
    {
        var editor = Build(new FakeService());

        var art = editor.Rows.OfType<MetadataArtRowViewModel>().Select(r => r.Field).ToList();

        Assert.Equal([WorkFields.CoverUrl, WorkFields.BackgroundUrl], art);
    }

    [Fact]
    public async Task An_art_row_takes_a_local_file_through_the_picker()
    {
        var service = new FakeService();
        var picker = new FakePicker { Path = @"C:\pictures\empyrion.png" };
        var editor = Build(service, picker);

        var cover = ArtRow(editor, WorkFields.CoverUrl);
        Assert.True(cover.CanPickFile);

        await cover.ChooseFileCommand.ExecuteAsync(null);

        Assert.Equal((WorkFields.CoverUrl, @"C:\pictures\empyrion.png"), service.FileImport);
        Assert.Equal(FieldSources.User, cover.Source);
        Assert.False(cover.HasProblem);
    }

    [Fact]
    public async Task A_dismissed_file_dialog_writes_nothing()
    {
        var service = new FakeService();
        var editor = Build(service, new FakePicker { Path = null });

        await ArtRow(editor, WorkFields.CoverUrl).ChooseFileCommand.ExecuteAsync(null);

        Assert.Null(service.FileImport);
        Assert.Empty(service.Written);
    }

    [Fact]
    public void With_no_picker_registered_the_file_route_is_not_drawn()
    {
        var editor = Build(new FakeService());

        Assert.False(ArtRow(editor, WorkFields.CoverUrl).CanPickFile);
        Assert.False(ArtRow(editor, WorkFields.BackgroundUrl).CanPickFile);
    }

    [Fact]
    public async Task An_art_row_takes_a_url()
    {
        var service = new FakeService();
        var editor = Build(service);

        var background = ArtRow(editor, WorkFields.BackgroundUrl);
        background.Draft = "https://example.test/wide.png";
        await background.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            (WorkFields.BackgroundUrl, "https://example.test/wide.png"),
            service.UrlImport);
        Assert.Equal(FieldSources.User, background.Source);
    }

    /// <summary>
    /// Every one of the nine art refusals reaches the row as its own distinct
    /// sentence rather than as a crash -- nothing on that seam throws at a
    /// view model.
    /// </summary>
    [Fact]
    public async Task Every_art_refusal_renders_its_own_message()
    {
        var refusals = Enum.GetValues<WorkArtEditOutcome>()
            .Where(o => o != WorkArtEditOutcome.Applied)
            .ToList();

        var seen = new List<string>();

        foreach (var outcome in refusals)
        {
            var service = new FakeService { ArtOutcome = outcome };
            var editor = Build(service, new FakePicker { Path = @"C:\pictures\a.png" });
            var cover = ArtRow(editor, WorkFields.CoverUrl);

            await cover.ChooseFileCommand.ExecuteAsync(null);

            Assert.True(cover.HasProblem);
            Assert.Equal(GameMetadataEditorCopy.ProblemFor(outcome), cover.Problem);
            Assert.False(string.IsNullOrWhiteSpace(cover.Problem));
            seen.Add(cover.Problem!);
        }

        Assert.Equal(refusals.Count, seen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task An_empty_url_box_is_refused_rather_than_written()
    {
        var service = new FakeService();
        var editor = Build(service);

        var cover = ArtRow(editor, WorkFields.CoverUrl);
        cover.Draft = "   ";
        await cover.SaveCommand.ExecuteAsync(null);

        Assert.Null(service.UrlImport);
        Assert.Equal(GameMetadataEditorCopy.ProblemFor(WorkArtEditOutcome.BadUrl), cover.Problem);
    }

    // ══ Disclosure and loading ═════════════════════════════════════════════

    [Fact]
    public async Task Opening_the_editor_reads_the_work_and_the_status_field_is_words()
    {
        var service = FakeService.WithIgdbEverywhere();
        var editor = new GameMetadataEditorViewModel(service, WorkId);

        Assert.False(editor.HasRows);

        await editor.ToggleCommand.ExecuteAsync(null);

        Assert.True(editor.IsOpen);
        Assert.Equal(WorkFields.All.Count, editor.Rows.Count);
        Assert.False(editor.HasStatus);
        Assert.False(editor.HasProblem);
    }

    [Fact]
    public async Task A_work_that_cannot_be_read_says_so_and_draws_no_rows()
    {
        var editor = new GameMetadataEditorViewModel(new FakeService { Missing = true }, WorkId);

        await editor.LoadAsync();

        Assert.False(editor.HasRows);
        Assert.Equal(GameMetadataEditorCopy.LoadFailedText, editor.Problem);
    }

    // ══ The text-save seam ═════════════════════════════════════════════════

    /// <summary>
    /// The text-save callback fires once, carrying the field key and the value
    /// as stored rather than the raw draft. The row still draws its own
    /// confirmation because nothing here reloads — the callback is what lets
    /// the library rename the live tile in place (design-system §10.10).
    /// </summary>
    [Fact]
    public async Task A_saved_text_field_hands_the_library_the_field_and_its_stored_value()
    {
        var handed = new List<(string Field, string? Value)>();
        var service = FakeService.WithIgdbEverywhere();
        var editor = Build(service, afterTextChange: (field, value) =>
        {
            handed.Add((field, value));
            return Task.CompletedTask;
        });

        var name = Row(editor, WorkFields.Name);
        name.Draft = "Empyrion";
        await name.SaveCommand.ExecuteAsync(null);

        Assert.Equal([(WorkFields.Name, "Empyrion")], handed);
        Assert.Equal(GameMetadataEditorCopy.SavedNote(name.Label), name.Note);
    }

    /// <summary>
    /// A refused write tells the library nothing and leaves the refusal under
    /// the field. The callback must not report a rename that did not happen,
    /// because the library would retitle the tile to a value the database
    /// refused.
    /// </summary>
    [Fact]
    public async Task A_refused_text_save_hands_the_library_nothing()
    {
        var handed = new List<(string Field, string? Value)>();
        var service = new FakeService { FieldOutcome = WorkFieldEditOutcome.WorkNotFound };
        var editor = Build(service, afterTextChange: (field, value) =>
        {
            handed.Add((field, value));
            return Task.CompletedTask;
        });

        var name = Row(editor, WorkFields.Name);
        name.Draft = "Empyrion";
        await name.SaveCommand.ExecuteAsync(null);

        Assert.Empty(handed);
        Assert.True(name.HasProblem);
    }

    // ══ Helpers ════════════════════════════════════════════════════════════

    private static GameMetadataEditorViewModel Build(
        FakeService service,
        IImageFilePicker? picker = null,
        Func<string, string?, Task>? afterTextChange = null)
        => new(service, WorkId, service.Snapshot(), picker: picker, afterTextChange: afterTextChange);

    private static MetadataFieldRowViewModel Row(GameMetadataEditorViewModel editor, string field)
        => editor.Rows.Single(r => r.Field == field);

    private static MetadataArtRowViewModel ArtRow(GameMetadataEditorViewModel editor, string field)
        => (MetadataArtRowViewModel)Row(editor, field);

    /// <summary>Returns a canned path, or null for a dismissed dialog.</summary>
    private sealed class FakePicker : IImageFilePicker
    {
        public string? Path { get; init; }

        public string? Title { get; private set; }

        public Task<string?> PickAsync(string title, CancellationToken ct = default)
        {
            Title = title;
            return Task.FromResult(Path);
        }
    }

    /// <summary>
    /// Answers from canned values and records what it was asked, exactly as
    /// the real seam does: never throws, and a failure is a status or an
    /// empty list.
    /// </summary>
    private sealed class FakeService : IWorkMetadataEditService
    {
        private readonly Dictionary<string, (string? Value, string? Source)> _state =
            new(StringComparer.Ordinal);

        public bool Missing { get; init; }

        public WorkFieldEditOutcome FieldOutcome { get; init; } = WorkFieldEditOutcome.Applied;

        public WorkArtEditOutcome ArtOutcome { get; init; } = WorkArtEditOutcome.Applied;

        public List<string> Written { get; } = [];

        public List<string> Reset { get; } = [];

        public (string Field, string Path)? FileImport { get; private set; }

        public (string Field, string Url)? UrlImport { get; private set; }

        public static FakeService WithIgdbEverywhere()
        {
            var service = new FakeService();
            foreach (var field in WorkFields.All)
            {
                service.Set(field, $"{field}-value", FieldSources.Igdb);
            }

            return service;
        }

        public void Set(string field, string? value, string? source)
            => _state[field] = (value, source);

        public WorkMetadataSnapshot? Snapshot()
            => Missing
                ? null
                : new WorkMetadataSnapshot(
                    WorkId,
                    "Empyrion: Galactic Survival",
                    false,
                    [.. WorkFields.All.Select(f =>
                    {
                        var (value, source) = _state.TryGetValue(f, out var held)
                            ? held
                            : (null, null);
                        return new WorkMetadataField(f, value, source);
                    })]);

        public Task<WorkMetadataSnapshot?> GetAsync(long workId, CancellationToken ct = default)
            => Task.FromResult(Snapshot());

        public Task<WorkFieldEditOutcome> SetFieldAsync(
            long workId, string field, string? value, CancellationToken ct = default)
        {
            if (FieldOutcome != WorkFieldEditOutcome.Applied)
            {
                return Task.FromResult(FieldOutcome);
            }

            Written.Add(field);
            Set(field, value, FieldSources.User);
            return Task.FromResult(WorkFieldEditOutcome.Applied);
        }

        public Task<WorkFieldEditOutcome> ResetFieldAsync(
            long workId, string field, CancellationToken ct = default)
        {
            if (FieldOutcome != WorkFieldEditOutcome.Applied)
            {
                return Task.FromResult(FieldOutcome);
            }

            Reset.Add(field);
            Set(field, null, null);
            return Task.FromResult(WorkFieldEditOutcome.Applied);
        }

        public Task<WorkArtEditOutcome> SetArtFromFileAsync(
            long workId, string field, string filePath, CancellationToken ct = default)
        {
            if (ArtOutcome != WorkArtEditOutcome.Applied)
            {
                return Task.FromResult(ArtOutcome);
            }

            FileImport = (field, filePath);
            Written.Add(field);
            Set(field, "winnow://user-art/token", FieldSources.User);
            return Task.FromResult(WorkArtEditOutcome.Applied);
        }

        public Task<WorkArtEditOutcome> SetArtFromUrlAsync(
            long workId, string field, string url, CancellationToken ct = default)
        {
            if (ArtOutcome != WorkArtEditOutcome.Applied)
            {
                return Task.FromResult(ArtOutcome);
            }

            UrlImport = (field, url);
            Written.Add(field);
            Set(field, "winnow://user-art/token", FieldSources.User);
            return Task.FromResult(WorkArtEditOutcome.Applied);
        }

        public CoverKey? ArtKeyFor(string? value) => null;
    }
}
