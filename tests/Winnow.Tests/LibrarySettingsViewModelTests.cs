using System.Globalization;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The UI half of TASK-87, TASK-88 and TASK-99: SETTINGS › LIBRARY, and the
/// Hide action the library itself carries.
///
/// <para>The claim under test on all three is the same one: a change made here
/// reaches the grid, the list view, the feed and every rail count together,
/// because all four read one bucket query and the settings screen's only job is
/// to write the fact and ask for a reload. The tests therefore assert against
/// the library view model's own tiles and bucket counts rather than against the
/// repositories, which the data-layer tests already cover.</para>
/// </summary>
public sealed class LibrarySettingsViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();
    private int _appId = 700000;

    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IOwnershipRepository _ownerships;
    private readonly IPlayRecordRepository _plays;
    private readonly IUpdateEventRepository _updates;
    private readonly ILibraryQueryRepository _queries;
    private readonly IHiddenGameRepository _hidden;
    private readonly IManualEntryRepository _manual;
    private readonly ISettingsRepository _settings;

    public LibrarySettingsViewModelTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _plays = new PlayRecordRepository(_db.Factory);
        _updates = new UpdateEventRepository(_db.Factory);
        _queries = new LibraryQueryRepository(_db.Factory);
        _hidden = new HiddenGameRepository(_db.Factory);
        _manual = new ManualEntryRepository(_db.Factory);
        _settings = new SettingsRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    // ══ TASK-87 ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AC1 and AC2, from the library's side: the command the context menu and
    /// the details modal both bind to takes the game out of the tile set and
    /// out of the bucket count that was carrying it.
    /// </summary>
    [Fact]
    public async Task Hiding_a_game_removes_its_tile_and_its_bucket_count()
    {
        await SeedAsync("Kept", minutes: 0);
        await SeedAsync("Hidden", minutes: 0);

        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        var never = library.Buckets.Single(b => b.Key == LibraryBuckets.NeverPlayed);
        Assert.Equal(2, never.Count);

        await library.HideGameCommand.ExecuteAsync(Tile(library, "Hidden"));

        Assert.Equal(["Kept"], library.VisibleTiles.Select(t => t.Title));
        Assert.Equal(1, never.Count);
        Assert.Equal(1, library.TotalCount);
    }

    /// <summary>
    /// AC1's other route: the context menu acts on the whole picked set, which
    /// is what makes one control serve the grid's single selection and the list
    /// view's many.
    /// </summary>
    [Fact]
    public async Task The_context_menu_hides_every_picked_tile()
    {
        await SeedAsync("One", minutes: 0);
        await SeedAsync("Two", minutes: 0);
        await SeedAsync("Three", minutes: 0);

        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        library.SelectedTiles = [Tile(library, "One"), Tile(library, "Three")];
        Assert.True(library.HideSelectionCommand.CanExecute(null));

        await library.HideSelectionCommand.ExecuteAsync(null);

        Assert.Equal(["Two"], library.VisibleTiles.Select(t => t.Title));
    }

    /// <summary>
    /// AC4: the hidden game is listed somewhere the user can find it, with
    /// enough beside it to say what unhiding gives back, and one row at a time
    /// goes back into the library.
    /// </summary>
    [Fact]
    public async Task A_hidden_game_is_listed_and_can_be_put_back_one_at_a_time()
    {
        await SeedAsync("Kept", minutes: 0);
        await SeedAsync("Hidden", minutes: 0);

        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        await library.HideGameCommand.ExecuteAsync(Tile(library, "Hidden"));

        var screen = CreateSettings(library);
        await screen.RefreshAsync();

        var row = Assert.Single(screen.HiddenGames);
        Assert.Equal("Hidden", row.Title);
        Assert.Equal(1, row.StoreEntryCount);
        Assert.True(screen.HasHiddenGames);
        Assert.False(screen.ShowHiddenEmpty);

        await screen.UnhideCommand.ExecuteAsync(row);

        Assert.Empty(screen.HiddenGames);
        Assert.True(screen.ShowHiddenEmpty);
        Assert.Contains("Hidden", library.VisibleTiles.Select(t => t.Title));
    }

    // ══ TASK-88 ═══════════════════════════════════════════════════════════

    /// <summary>
    /// The toggle writes the stored preference and carries it onto the library,
    /// which is what puts it into the bucket query. Nothing is hidden here
    /// because nothing in this library has maturity evidence — which is the
    /// default the screen states.
    /// </summary>
    [Fact]
    public async Task The_explicit_toggle_persists_and_reaches_the_library()
    {
        await SeedAsync("Anything", minutes: 0);

        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        var screen = CreateSettings(library);
        await screen.RefreshAsync();

        Assert.False(screen.ShowExplicitContent);
        Assert.Equal(0, screen.ExplicitHiddenCount);
        Assert.True(screen.ShowExplicitPendingNote);

        screen.ShowExplicitContent = true;
        await screen.PendingSave;

        Assert.Equal(
            BucketThresholds.FormatShowExplicitContent(true),
            await _settings.GetAsync(BucketThresholds.ShowExplicitContentSettingKey));
        Assert.True(library.ShowExplicitContent);

        // A library with no evidence is unaffected either way, which is the
        // default the screen states beside the control.
        Assert.Single(library.VisibleTiles);
    }

    /// <summary>The stored preference is read back on the next open.</summary>
    [Fact]
    public async Task The_explicit_preference_survives_a_reload()
    {
        await _settings.SetAsync(
            BucketThresholds.ShowExplicitContentSettingKey,
            BucketThresholds.FormatShowExplicitContent(true));

        var library = CreateLibrary();
        var screen = CreateSettings(library);
        await screen.RefreshAsync();

        Assert.True(screen.ShowExplicitContent);
    }

    // ══ TASK-99 ═══════════════════════════════════════════════════════════

    /// <summary>
    /// AC1, AC2 and AC4: the form creates an entry no ingest reader produced,
    /// it appears in the library like any other game, and the executable it
    /// names is stored with the install path derived from it — which is what
    /// puts it in the session watcher's index.
    /// </summary>
    [Fact]
    public async Task A_hand_added_game_appears_in_the_library()
    {
        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);
        Assert.Empty(library.VisibleTiles);

        var screen = CreateSettings(library);
        await screen.RefreshAsync();

        screen.BeginAddCommand.Execute(null);
        Assert.True(screen.IsFormOpen);

        screen.DraftTitle = "Hand Added";
        screen.DraftYear = "2019";
        screen.DraftPlatform = "itch.io";
        screen.DraftExecutable = Path.Combine("C:", "Games", "HandAdded", "game.exe");

        await screen.SaveFormCommand.ExecuteAsync(null);

        Assert.False(screen.IsFormOpen);
        Assert.Null(screen.TitleError);

        var row = Assert.Single(screen.ManualEntries);
        Assert.Equal("Hand Added", row.Title);
        Assert.Equal("2019 · itch.io", row.Detail);
        Assert.True(row.HasExecutable);

        var stored = Assert.Single(await _manual.GetAllAsync());
        Assert.Equal(
            Path.Combine("C:", "Games", "HandAdded", "game.exe"), stored.ExecutablePath);

        // The two fields GameExecutableIndexBuilder reads to decide what to
        // watch: it skips an ownership that is not installed or has no install
        // path, and walks the directory of the rest.
        var ownership = (await _ownerships.GetAsync(stored.OwnershipId))!;
        Assert.Equal(Path.Combine("C:", "Games", "HandAdded"), ownership.InstallPath);
        Assert.True(ownership.Installed);

        var tile = Assert.Single(library.VisibleTiles);
        Assert.Equal("Hand Added", tile.Title);
        Assert.Equal(1, library.TotalCount);
    }

    /// <summary>AC5: the entry can be edited, and the edit reaches the library.</summary>
    [Fact]
    public async Task A_hand_added_game_can_be_edited()
    {
        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        var screen = CreateSettings(library);
        screen.BeginAddCommand.Execute(null);
        screen.DraftTitle = "Typo Heer";
        await screen.SaveFormCommand.ExecuteAsync(null);

        await screen.BeginEditCommand.ExecuteAsync(Assert.Single(screen.ManualEntries));
        Assert.True(screen.IsFormOpen);
        Assert.Equal("Typo Heer", screen.DraftTitle);

        screen.DraftTitle = "Typo Here";
        screen.DraftPlatform = "Switch";
        await screen.SaveFormCommand.ExecuteAsync(null);

        Assert.Equal("Typo Here", Assert.Single(screen.ManualEntries).Title);
        Assert.Equal("Typo Here", Assert.Single(library.VisibleTiles).Title);
    }

    /// <summary>
    /// AC5's other half. Deleting asks first (§12.3) and the confirmation is a
    /// separate command, so no single click can remove an entry.
    /// </summary>
    [Fact]
    public async Task A_hand_added_game_is_deleted_only_after_a_confirmation()
    {
        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        var screen = CreateSettings(library);
        screen.BeginAddCommand.Execute(null);
        screen.DraftTitle = "Going";
        await screen.SaveFormCommand.ExecuteAsync(null);

        var row = Assert.Single(screen.ManualEntries);
        screen.BeginDeleteCommand.Execute(row);

        Assert.True(screen.IsDeleteConfirmOpen);
        Assert.Contains("Going", screen.DeleteConfirmMessage, StringComparison.Ordinal);
        Assert.Single(await _manual.GetAllAsync());

        screen.CancelDeleteCommand.Execute(null);
        Assert.False(screen.IsDeleteConfirmOpen);
        Assert.Single(await _manual.GetAllAsync());

        screen.BeginDeleteCommand.Execute(row);
        await screen.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Empty(screen.ManualEntries);
        Assert.Empty(await _manual.GetAllAsync());
        Assert.Empty(library.VisibleTiles);
    }

    /// <summary>
    /// The conflict lands under the field that caused it rather than at the
    /// foot of the form, and nothing is written — the repository raises it
    /// before it starts.
    /// </summary>
    [Fact]
    public async Task A_conflicting_steam_appid_is_reported_against_its_own_field()
    {
        var releaseId = await SeedAsync("Already Owned", minutes: 0);
        var appId = (await _releases.GetExternalIdsAsync(releaseId))
            .Single(x => x.Provider == ExternalIdProviders.Steam).ProviderId;

        var library = CreateLibrary();
        await library.LoadCommand.ExecuteAsync(null);

        var screen = CreateSettings(library);
        screen.BeginAddCommand.Execute(null);
        screen.DraftTitle = "Same Game By Hand";
        screen.DraftSteamAppId = appId;

        await screen.SaveFormCommand.ExecuteAsync(null);

        Assert.True(screen.HasSteamAppIdError);
        Assert.False(screen.HasTitleError);
        Assert.False(screen.HasIgdbIdError);
        Assert.Null(screen.Problem);

        // The form stays open on the values the user typed, and nothing landed.
        Assert.True(screen.IsFormOpen);
        Assert.Empty(await _manual.GetAllAsync());
        Assert.Single(library.VisibleTiles);
    }

    /// <summary>A blank title is a field error, not a thrown exception.</summary>
    [Fact]
    public async Task A_blank_title_is_reported_against_the_title_field()
    {
        var library = CreateLibrary();
        var screen = CreateSettings(library);

        screen.BeginAddCommand.Execute(null);
        screen.DraftTitle = "   ";

        await screen.SaveFormCommand.ExecuteAsync(null);

        Assert.True(screen.HasTitleError);
        Assert.True(screen.IsFormOpen);
        Assert.Empty(await _manual.GetAllAsync());
    }

    /// <summary>A non-numeric id never reaches the repository.</summary>
    [Fact]
    public async Task A_non_numeric_id_is_reported_against_its_own_field()
    {
        var library = CreateLibrary();
        var screen = CreateSettings(library);

        screen.BeginAddCommand.Execute(null);
        screen.DraftTitle = "Fine";
        screen.DraftIgdbId = "not-a-number";

        await screen.SaveFormCommand.ExecuteAsync(null);

        Assert.True(screen.HasIgdbIdError);
        Assert.Empty(await _manual.GetAllAsync());
    }

    // ══ Fixture ═══════════════════════════════════════════════════════════

    private LibraryViewModel CreateLibrary()
        => new(
            _queries, _ownerships, _releases, _works, _updates,
            hidden: _hidden);

    private LibrarySettingsViewModel CreateSettings(LibraryViewModel library)
    {
        var screen = new LibrarySettingsViewModel(
            _hidden, _manual, _queries, _settings, _works, _releases);

        screen.ReloadLibrary = async () =>
        {
            library.ShowExplicitContent = screen.ShowExplicitContent;
            await library.LoadCommand.ExecuteAsync(null);
        };

        return screen;
    }

    private static GameTileViewModel Tile(LibraryViewModel library, string title)
        => library.VisibleTiles.Single(t => t.Title == title);

    private async Task<long> SeedAsync(string title, long minutes)
    {
        var workId = await _works.InsertAsync(new Work { Name = title });

        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = title,
            Platform = "windows",
        });

        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = (++_appId).ToString(CultureInfo.InvariantCulture),
        });

        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = "steam",
        });

        await _plays.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            LastPlayedAt = null,
            Source = "steam_localconfig",
            ObservedAt = Now,
        });

        return releaseId;
    }
}
