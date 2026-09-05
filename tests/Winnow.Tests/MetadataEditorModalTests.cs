using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The details modal's per-field metadata editor against a real migrated
/// database (TASK-119). These tests assert the wiring — disclosure from the
/// action band, save landing in the database, seam degradation — rather than
/// the editor's own behaviour, which GameMetadataEditorViewModelTests already
/// covers.
///
/// <para>Everything below the view model is the shipped code:
/// WorkMetadataEditService over the real WorkFieldSourceRepository and
/// WorkRepository, so a save lands in works and stamps
/// work_field_sources.</para>
/// </summary>
public sealed class MetadataEditorModalTests : IDisposable
{
    private static readonly DateTime Observed = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _plays;
    private readonly UpdateEventRepository _updates;
    private readonly LibraryQueryRepository _queries;
    private readonly WorkFieldSourceRepository _fields;

    public MetadataEditorModalTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
        _updates = new UpdateEventRepository(_db.Factory);
        _queries = new LibraryQueryRepository(_db.Factory);
        _fields = new WorkFieldSourceRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// The disclosure is drawn, opening it reads the six fields, and saving one
    /// writes the work the tile resolves to and stamps that one field as
    /// user-owned. The other five keep whatever source they had, which here is
    /// none.
    /// </summary>
    [Fact]
    public async Task The_editor_writes_the_work_the_tile_resolves_to()
    {
        var seeded = await SeedAsync();

        var library = await LoadAsync();
        var tile = Assert.Single(library.VisibleTiles);
        await library.OpenDetailsCommand.ExecuteAsync(tile);

        var editor = library.Details?.MetadataEditor;
        Assert.NotNull(editor);
        Assert.True(library.Details!.ShowMetadataEditor);

        await editor.ToggleCommand.ExecuteAsync(null);
        Assert.True(editor.IsOpen);
        Assert.Equal(WorkFields.All.Count, editor.Rows.Count);

        var name = editor.Rows.Single(r => r.Field == WorkFields.Name);
        Assert.Equal("Prey", name.Value);
        Assert.Null(name.Source);

        name.Draft = "Prey (2006)";
        await name.SaveCommand.ExecuteAsync(null);

        Assert.False(name.HasProblem);
        Assert.Equal(FieldSources.User, name.Source);

        var stored = await _works.GetAsync(seeded.WorkId);
        Assert.Equal("Prey (2006)", stored!.Name);

        var sources = await _fields.GetSourcesAsync(seeded.WorkId);
        Assert.Equal(FieldSources.User, sources[WorkFields.Name]);
        Assert.False(sources.ContainsKey(WorkFields.Summary));
    }

    /// <summary>
    /// Handing a field back to automatic drops the stamp and empties the
    /// column, so the next enrichment pass can fill it again. Name is the
    /// special case: works.name is NOT NULL, so the reset marks it provisional
    /// and leaves the text standing.
    /// </summary>
    [Fact]
    public async Task A_field_can_be_handed_back_to_automatic_from_the_modal()
    {
        var seeded = await SeedAsync();

        var library = await LoadAsync();
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.VisibleTiles));

        var editor = library.Details!.MetadataEditor!;
        await editor.ToggleCommand.ExecuteAsync(null);

        var publisher = editor.Rows.Single(r => r.Field == WorkFields.Publisher);
        publisher.Draft = "Human Head Studios";
        await publisher.SaveCommand.ExecuteAsync(null);

        Assert.True(publisher.CanReset);

        await publisher.ResetCommand.ExecuteAsync(null);

        Assert.False(publisher.HasProblem);
        Assert.Null(publisher.Source);
        Assert.False(publisher.CanReset);

        var stored = await _works.GetAsync(seeded.WorkId);
        Assert.Null(stored!.Publisher);

        var sources = await _fields.GetSourcesAsync(seeded.WorkId);
        Assert.False(sources.ContainsKey(WorkFields.Publisher));
    }

    /// <summary>
    /// A name saved in the editor reaches the grid tile, the visible set, the
    /// modal headline and the tile's filterable row immediately, without
    /// reloading the library. The modal is the same instance and the editor is
    /// still open. The two rows the user had half-typed keep their drafts and
    /// their sources unchanged. A text save deliberately does not reload
    /// (design-system §10.10) because reloading would discard those drafts;
    /// this test pins the in-place rename that replaced the reload.
    /// </summary>
    [Fact]
    public async Task A_saved_name_reaches_the_tile_and_the_headline_without_touching_the_drafts()
    {
        await SeedAsync();

        var library = await LoadAsync();
        var tile = Assert.Single(library.VisibleTiles);
        await library.OpenDetailsCommand.ExecuteAsync(tile);

        var details = library.Details!;
        var editor = details.MetadataEditor!;
        await editor.ToggleCommand.ExecuteAsync(null);

        var summary = editor.Rows.Single(r => r.Field == WorkFields.Summary);
        var publisher = editor.Rows.Single(r => r.Field == WorkFields.Publisher);
        summary.Draft = "A half-written sentence.";
        publisher.Draft = "Human Head Studios";

        var name = editor.Rows.Single(r => r.Field == WorkFields.Name);
        name.Draft = "Prey (2006)";
        await name.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Prey (2006)", tile.Title);
        Assert.Equal("Prey (2006)", Assert.Single(library.VisibleTiles).Title);
        Assert.Equal("Prey (2006)", details.Title);
        Assert.Equal("Prey (2006)", tile.Row.Title);

        Assert.Same(details, library.Details);
        Assert.Same(editor, library.Details!.MetadataEditor);
        Assert.True(editor.IsOpen);

        Assert.Equal("A half-written sentence.", summary.Draft);
        Assert.Equal("Human Head Studios", publisher.Draft);
        Assert.Null(summary.Source);
        Assert.Null(publisher.Source);
    }

    /// <summary>
    /// With the title sort selected, renaming a game re-orders the visible set
    /// immediately rather than leaving the tile in its old position until the
    /// user touches the sort control. The in-place rename must feed the same
    /// sort path the library already uses.
    /// </summary>
    [Fact]
    public async Task A_saved_name_moves_the_game_in_the_sort_order()
    {
        await SeedAsync("Alpha Protocol");
        await SeedAsync("Zeno Clash");

        var library = await LoadAsync();
        library.Sort = LibrarySort.NameAscending;

        Assert.Equal(
            ["Alpha Protocol", "Zeno Clash"],
            library.VisibleTiles.Select(t => t.Title));

        var alpha = library.VisibleTiles[0];
        await library.OpenDetailsCommand.ExecuteAsync(alpha);

        var editor = library.Details!.MetadataEditor!;
        await editor.ToggleCommand.ExecuteAsync(null);

        var name = editor.Rows.Single(r => r.Field == WorkFields.Name);
        name.Draft = "Zzz Protocol";
        await name.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            ["Zeno Clash", "Zzz Protocol"],
            library.VisibleTiles.Select(t => t.Title));
    }

    /// <summary>
    /// With no edit service registered the modal is exactly the modal it was
    /// before TASK-119 — the degradation every optional seam on this view model
    /// takes.
    /// </summary>
    [Fact]
    public async Task No_service_means_no_editor()
    {
        await SeedAsync();

        var library = new LibraryViewModel(_queries, _ownerships, _releases, _works, _updates);
        await library.LoadCommand.ExecuteAsync(null);
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.VisibleTiles));

        Assert.NotNull(library.Details);
        Assert.Null(library.Details.MetadataEditor);
        Assert.False(library.Details.ShowMetadataEditor);
    }

    /// <summary>
    /// With no image picker registered the two art rows lose the choose-a-file
    /// route and keep the URL one — the seam degrading on its own rather than
    /// taking the whole editor with it.
    /// </summary>
    [Fact]
    public async Task No_picker_means_the_url_route_only()
    {
        await SeedAsync();

        var library = await LoadAsync();
        await library.OpenDetailsCommand.ExecuteAsync(Assert.Single(library.VisibleTiles));

        var editor = library.Details!.MetadataEditor!;
        await editor.ToggleCommand.ExecuteAsync(null);

        var art = editor.Rows.OfType<MetadataArtRowViewModel>().ToList();
        Assert.Equal(2, art.Count);
        Assert.All(art, row => Assert.False(row.CanPickFile));
    }

    private async Task<LibraryViewModel> LoadAsync()
    {
        var library = new LibraryViewModel(
            _queries,
            _ownerships,
            _releases,
            _works,
            _updates,
            metadataEdits: new WorkMetadataEditService(_works, _fields));

        await library.LoadCommand.ExecuteAsync(null);
        return library;
    }

    private async Task<SeededGame> SeedAsync(string name = "Prey")
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = name,
            FirstReleaseYear = 2006,
            Publisher = "2K Games",
            Summary = "A Cherokee garage mechanic is abducted.",
        });

        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = name,
            Platform = "windows",
        });

        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = "gog",
        });

        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 120,
            LastPlayedAt = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Source = "gog",
            ObservedAt = Observed,
        });

        return new SeededGame(workId, releaseId, ownershipId);
    }

    private sealed record SeededGame(long WorkId, long ReleaseId, long OwnershipId);
}
