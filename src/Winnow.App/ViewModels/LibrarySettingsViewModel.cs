using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// SETTINGS › LIBRARY, the settings surface's third section beside Platforms and
/// Appearance and Application. Three cards, one question — what is in the library: the
/// explicit-content filter, the games the user hid, and the games they added by
/// hand. Every dependency is optional, so a host that skipped one gets a card
/// that is empty rather than a screen that will not open.
///
/// <para><see cref="ReloadLibrary"/> is assigned by the shell rather than
/// injected, exactly as <c>StoresViewModel</c>'s is: changing what the library
/// shows means the grid, the list view, the feed and every rail count have to be
/// asked again, and this screen must not know how to do that itself.</para>
/// </summary>
public partial class LibrarySettingsViewModel : ObservableObject
{
    private readonly IHiddenGameRepository? _hidden;
    private readonly IManualEntryRepository? _manual;
    private readonly ILibraryQueryRepository? _libraryQueries;
    private readonly ISettingsRepository? _settings;

    /// <summary>
    /// The edit form reads back the IGDB id and Steam appid the entry already
    /// carries, because UpdateAsync writes both fields as given — an edit that
    /// opened with them blank would clear the ids that got the game its cover.
    /// ManualEntry does not carry them, so they are fetched from the work and
    /// the release.
    /// </summary>
    private readonly IWorkRepository? _works;

    private readonly IReleaseRepository? _releases;

    // The executable route. All four optional for the same reason every other
    // dependency here is: a host that omits them gets the typed form and not a
    // screen that will not open.
    private readonly IExecutableFilePicker? _executables;

    private readonly IExecutableInspector? _inspector;

    private readonly IIgdbAssignmentService? _igdb;

    private readonly ICoverLeases? _covers;

    /// <summary>Guards against write-back while the stored preference is being read.</summary>
    private bool _loading;

    /// <summary>In-flight IGDB search. One at a time.</summary>
    private bool _searching;

    // The title the last inspection proposed. A second browse replaces this
    // guess without overwriting a title the user typed.
    private string? _proposedTitle;

    private double _candidateCoverWidthPixels = GameIgdbMatchViewModel.CandidateCoverWidth;

    public LibrarySettingsViewModel(
        IHiddenGameRepository? hidden = null,
        IManualEntryRepository? manual = null,
        ILibraryQueryRepository? libraryQueries = null,
        ISettingsRepository? settings = null,
        IWorkRepository? works = null,
        IReleaseRepository? releases = null,
        IExecutableFilePicker? executables = null,
        IExecutableInspector? inspector = null,
        IIgdbAssignmentService? igdb = null,
        ICoverLeases? covers = null,
        AcquisitionExport? acquisitionExport = null,
        IAcquisitionExportDestination? exportDestination = null)
    {
        _hidden = hidden;
        _manual = manual;
        _libraryQueries = libraryQueries;
        _settings = settings;
        _works = works;
        _releases = releases;
        _executables = executables;
        _inspector = inspector;
        _igdb = igdb;
        _covers = covers;
        _acquisitionExport = acquisitionExport;
        _exportDestination = exportDestination;
    }

    private readonly AcquisitionExport? _acquisitionExport;
    private readonly IAcquisitionExportDestination? _exportDestination;

    public bool CanExportAcquisitions => _acquisitionExport is not null && _exportDestination is not null;

    [ObservableProperty]
    public partial string AcquisitionExportStatus { get; set; } = "";

    [RelayCommand(CanExecute = nameof(CanExportAcquisitions))]
    private async Task ExportAcquisitionsAsync(CancellationToken ct)
    {
        AcquisitionExportStatus = "Preparing acquisition CSV…";
        try
        {
            var csv = await _acquisitionExport!.ReadAsync(ct);
            AcquisitionExportStatus = await _exportDestination!.SaveAsync(csv.Content, ct)
                ? $"Exported {csv.OwnershipCount.ToString(CultureInfo.CurrentCulture)} ownership {(csv.OwnershipCount == 1 ? "record" : "records")}."
                : "Export cancelled.";
        }
        catch (OperationCanceledException)
        {
            AcquisitionExportStatus = "Export cancelled.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AcquisitionExportStatus = "Could not save the export. Try another location.";
        }
    }

    /// <summary>
    /// Assigned by the shell. Re-runs the library query so the grid, the list
    /// view, the feed and the rail counts agree with what this screen changed.
    /// </summary>
    public Func<Task>? ReloadLibrary { get; set; }

    /// <summary>In-flight save; exposed for tests. The UI never awaits it.</summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    // ══ The screen ════════════════════════════════════════════════════════

    public string SegmentLabel => LibrarySettingsCopy.SegmentLabel;

    public string SegmentTooltip => LibrarySettingsCopy.SegmentTooltip;

    public string Title => LibrarySettingsCopy.Title;

    public string IntroMessage => LibrarySettingsCopy.IntroMessage;

    // ══ Explicit content ═══════════════════════════════════════════════════

    public string ExplicitSectionLabel => LibrarySettingsCopy.ExplicitSectionLabel;

    public string ExplicitToggleLabel => LibrarySettingsCopy.ExplicitToggleLabel;

    public string ExplicitDefaultNote => LibrarySettingsCopy.ExplicitDefaultNote;

    public string ExplicitPendingNote => LibrarySettingsCopy.ExplicitPendingNote;

    /// <summary>
    /// False by default. A work with no stored rating evidence is never
    /// explicit and is unaffected either way.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowExplicitContent { get; set; }

    /// <summary>
    /// How many tiles the filter would remove, from
    /// CountHiddenByExplicitFilterAsync. Zero on any library where enrichment
    /// has not yet stored a rating.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ExplicitHiddenCountText),
        nameof(ExplicitHiddenUnitLabel),
        nameof(ShowExplicitHiddenCount),
        nameof(ShowExplicitPendingNote))]
    public partial int ExplicitHiddenCount { get; set; }

    public string ExplicitHiddenCountText
        => ExplicitHiddenCount.ToString("N0", CultureInfo.CurrentCulture);

    public string ExplicitHiddenUnitLabel => ExplicitHiddenCount == 1
        ? LibrarySettingsCopy.ExplicitHiddenUnitSingular
        : LibrarySettingsCopy.ExplicitHiddenUnitPlural;

    public bool ShowExplicitHiddenCount => ExplicitHiddenCount > 0;

    public bool ShowExplicitPendingNote => ExplicitHiddenCount == 0;

    // ══ Hidden games ═══════════════════════════════════════════════════════

    public string HiddenSectionLabel => LibrarySettingsCopy.HiddenSectionLabel;

    public string HiddenEmptyMessage => LibrarySettingsCopy.HiddenEmptyMessage;

    /// <summary>Every currently-hidden game, newest first.</summary>
    public ObservableCollection<HiddenGameRowViewModel> HiddenGames { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHiddenEmpty))]
    public partial bool HasHiddenGames { get; set; }

    public bool ShowHiddenEmpty => !HasHiddenGames;

    // ══ Hand-added games ═══════════════════════════════════════════════════

    public string ManualSectionLabel => LibrarySettingsCopy.ManualSectionLabel;

    public string ManualEmptyMessage => LibrarySettingsCopy.ManualEmptyMessage;

    public string AddButtonText => LibrarySettingsCopy.AddButton;

    public string SaveButtonText => LibrarySettingsCopy.SaveButton;

    public string CancelButtonText => LibrarySettingsCopy.CancelButton;

    public string DeleteConfirmButtonText => LibrarySettingsCopy.DeleteConfirmButton;

    public string TitleFieldLabel => LibrarySettingsCopy.TitleFieldLabel;

    public string TitleWatermark => LibrarySettingsCopy.TitleWatermark;

    public string YearFieldLabel => LibrarySettingsCopy.YearFieldLabel;

    public string YearWatermark => LibrarySettingsCopy.YearWatermark;

    public string PlatformFieldLabel => LibrarySettingsCopy.PlatformFieldLabel;

    public string PlatformWatermark => LibrarySettingsCopy.PlatformWatermark;

    public string ExecutableFieldLabel => LibrarySettingsCopy.ExecutableFieldLabel;

    public string ExecutableWatermark => LibrarySettingsCopy.ExecutableWatermark;

    public string ExecutableHint => LibrarySettingsCopy.ExecutableHint;

    public string IgdbFieldLabel => LibrarySettingsCopy.IgdbFieldLabel;

    public string IgdbWatermark => LibrarySettingsCopy.IgdbWatermark;

    public string SteamAppIdFieldLabel => LibrarySettingsCopy.SteamAppIdFieldLabel;

    public string SteamAppIdWatermark => LibrarySettingsCopy.SteamAppIdWatermark;

    public string CoverHint => LibrarySettingsCopy.CoverHint;

    // ══ From a file (TASK-104) ═════════════════════════════════════════════

    public string AddFromFileButtonText => LibrarySettingsCopy.AddFromFileButton;

    public string AddFromFileTooltip => LibrarySettingsCopy.AddFromFileTooltip;

    public string BrowseButtonText => LibrarySettingsCopy.BrowseButton;

    public string BrowseTooltip => LibrarySettingsCopy.BrowseTooltip;

    public string MatchSearchButtonText => LibrarySettingsCopy.MatchSearchButton;

    public string MatchSearchTooltip => LibrarySettingsCopy.MatchSearchTooltip;

    public string MatchDismissButtonText => LibrarySettingsCopy.MatchDismissButton;

    public string MatchNoMatchesText => LibrarySettingsCopy.MatchNoMatchesText;

    // The Browse control is drawn only when a picker was supplied. Without one
    // the executable field is still typed into.
    public bool CanBrowse => _executables is not null;

    // The IGDB block is drawn only when the assignment service was supplied.
    // Without it the form is the typed-only form.
    public bool ShowMatchBlock => _igdb is not null;

    /// <summary>What the chosen executable yielded, or that it yielded nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExecutableNote))]
    public partial string? ExecutableNote { get; set; }

    public bool HasExecutableNote => ExecutableNote is not null;

    /// <summary>Candidates the last IGDB search returned. Rows are TASK-89's own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCandidates), nameof(ShowNoMatches), nameof(ShowMatchDismiss))]
    public partial IReadOnlyList<IgdbCandidateViewModel> Candidates { get; set; } = [];

    /// <summary>True once a search has completed, so an empty list reads differently from no search.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoMatches), nameof(ShowMatchDismiss))]
    public partial bool Searched { get; set; }

    /// <summary>Status field, in words. Null when nothing is in flight (§8).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatchStatus), nameof(ShowNoMatches))]
    public partial string? MatchStatus { get; set; }

    /// <summary>Standing note beside the IGDB block: which entry the form is now carrying.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatchNote), nameof(ShowMatchDismiss))]
    public partial string? MatchNote { get; set; }

    /// <summary>A search or a proposal that did not land. Amber, non-blocking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatchProblem), nameof(ShowNoMatches))]
    public partial string? MatchProblem { get; set; }

    public bool HasCandidates => Candidates.Count > 0;

    public bool HasMatchStatus => MatchStatus is not null;

    public bool HasMatchNote => MatchNote is not null;

    public bool HasMatchProblem => MatchProblem is not null;

    // True when a search completed with zero results. Not a failure; the form
    // can still be filled by hand.
    public bool ShowNoMatches
        => Searched && Candidates.Count == 0 && MatchStatus is null && MatchProblem is null;

    public bool ShowMatchDismiss => HasCandidates || Searched || HasMatchNote;

    /// <summary>Every hand-added entry, by title.</summary>
    public ObservableCollection<ManualEntryRowViewModel> ManualEntries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowManualEmpty))]
    public partial bool HasManualEntries { get; set; }

    public bool ShowManualEmpty => !HasManualEntries && !IsFormOpen;

    /// <summary>
    /// One inline form for both add and edit, in the pane's own tree rather
    /// than a flyout. A popup is its own root with no adorner layer (§12.3),
    /// so every focus ring inside one would have to be hand-drawn.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowManualEmpty), nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(BeginAddCommand), nameof(BeginAddFromFileCommand))]
    public partial bool IsFormOpen { get; set; }

    /// <summary>The entry being edited, or null while adding.</summary>
    [ObservableProperty]
    public partial ManualEntryRowViewModel? Editing { get; set; }

    [ObservableProperty]
    public partial string FormTitle { get; set; } = LibrarySettingsCopy.FormAddTitle;

    public bool CanAdd => !IsFormOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTitleError))]
    [NotifyCanExecuteChangedFor(nameof(SearchIgdbCommand))]
    public partial string DraftTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DraftYear { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DraftPlatform { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DraftExecutable { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DraftIgdbId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DraftSteamAppId { get; set; } = string.Empty;

    /// <summary>
    /// The repository names the field that conflicted
    /// (ManualEntryConflictException.Field), so the message lands under the
    /// box to fix rather than at the foot of the form.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTitleError))]
    public partial string? TitleError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasYearError))]
    public partial string? YearError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIgdbIdError))]
    public partial string? IgdbIdError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSteamAppIdError))]
    public partial string? SteamAppIdError { get; set; }

    /// <summary>A write that did not land. Amber, non-blocking, and never a field error.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasTitleError => TitleError is not null;

    public bool HasYearError => YearError is not null;

    public bool HasIgdbIdError => IgdbIdError is not null;

    public bool HasSteamAppIdError => SteamAppIdError is not null;

    public bool HasProblem => Problem is not null;

    /// <summary>The row awaiting a delete confirmation, or null (§12.3 — it asks first).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteConfirmOpen), nameof(DeleteConfirmMessage))]
    public partial ManualEntryRowViewModel? PendingDelete { get; set; }

    public bool IsDeleteConfirmOpen => PendingDelete is not null;

    public string DeleteConfirmMessage => PendingDelete is null
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            LibrarySettingsCopy.DeleteConfirmFormat,
            PendingDelete.Title);

    // ══ Loading ════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the stored explicit-content preference and both lists. Called when
    /// the section opens, so the screen shows the truth rather than a cache,
    /// and again after every write.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_settings is not null)
        {
            var stored = await Task.Run(() => _settings.GetAsync(
                BucketThresholds.ShowExplicitContentSettingKey, ct), ct);

            _loading = true;
            try
            {
                ShowExplicitContent = BucketThresholds.ParseShowExplicitContent(stored);
            }
            finally
            {
                _loading = false;
            }
        }

        if (_libraryQueries is not null)
        {
            // The count states what turning the filter ON would remove, so it
            // is asked with the non-game preference in force and independently
            // of the explicit preference itself.
            ExplicitHiddenCount = await Task.Run(() => _libraryQueries.CountHiddenByExplicitFilterAsync(
                BucketThresholds.Default, ct), ct);
        }

        await RefreshHiddenAsync(ct);
        await RefreshManualAsync(ct);
    }

    private async Task RefreshHiddenAsync(CancellationToken ct)
    {
        HiddenGames.Clear();

        if (_hidden is not null)
        {
            foreach (var game in await Task.Run(() => _hidden.GetHiddenGamesAsync(ct), ct))
            {
                HiddenGames.Add(new HiddenGameRowViewModel(game));
            }
        }

        HasHiddenGames = HiddenGames.Count > 0;
    }

    private async Task RefreshManualAsync(CancellationToken ct)
    {
        ManualEntries.Clear();

        if (_manual is not null)
        {
            foreach (var entry in await Task.Run(() => _manual.GetAllAsync(ct), ct))
            {
                ManualEntries.Add(new ManualEntryRowViewModel(entry));
            }
        }

        HasManualEntries = ManualEntries.Count > 0;
    }

    // ══ Commands ═══════════════════════════════════════════════════════════

    partial void OnShowExplicitContentChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // Reloads for the same reason the non-game toggle does: this decides
        // which rows the bucket query returns, and every rail count is computed
        // from that set, so the counts and the grid would otherwise disagree.
        var reload = ReloadLibrary?.Invoke() ?? Task.CompletedTask;

        PendingSave = _settings is null
            ? reload
            : Task.WhenAll(
                reload,
                _settings.SetAsync(
                    BucketThresholds.ShowExplicitContentSettingKey,
                    BucketThresholds.FormatShowExplicitContent(value)));
    }

    /// <summary>Puts one hidden game back, then reloads both the list and the library.</summary>
    [RelayCommand]
    private async Task UnhideAsync(HiddenGameRowViewModel? row)
    {
        if (_hidden is null || row is null)
        {
            return;
        }

        await _hidden.UnhideAsync(row.WorkId);
        await RefreshHiddenAsync(CancellationToken.None);
        await (ReloadLibrary?.Invoke() ?? Task.CompletedTask);
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void BeginAdd()
    {
        ClearForm();
        Editing = null;
        FormTitle = LibrarySettingsCopy.FormAddTitle;
        IsFormOpen = true;
    }

    // Opens the empty form and the OS file dialog together, so adding a game
    // starts from the executable the user already has on disk.
    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task BeginAddFromFileAsync(CancellationToken ct)
    {
        BeginAdd();
        await BrowseForExecutableAsync(ct);
    }

    // The picked path fills the executable field, the version info and the
    // path propose a title, and that title searches IGDB. Nothing is written;
    // Save is still the only write on this screen.
    [RelayCommand]
    private async Task BrowseForExecutableAsync(CancellationToken ct)
    {
        if (_executables is null)
        {
            return;
        }

        var path = await _executables.PickAsync(LibrarySettingsCopy.BrowseDialogTitle, ct);

        // A dismissed dialog changes nothing at all.
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        DraftExecutable = path;

        var facts = await InspectAsync(path);

        // A title the user typed is never overwritten. A title this route
        // proposed is, so picking a second executable re-proposes.
        if (facts.HasTitle
            && (string.IsNullOrWhiteSpace(DraftTitle)
                || string.Equals(DraftTitle, _proposedTitle, StringComparison.Ordinal)))
        {
            DraftTitle = facts.Title!;
            _proposedTitle = facts.Title;
            TitleError = null;
        }

        ExecutableNote = LibrarySettingsCopy.ExecutableNote(facts);
        MatchNote = null;
        MatchProblem = null;
        Candidates = [];
        Searched = false;

        if (_igdb is not null && !string.IsNullOrWhiteSpace(DraftTitle))
        {
            await SearchIgdbAsync(ct);
        }
    }

    private async Task<ExecutableFacts> InspectAsync(string path)
    {
        if (_inspector is null)
        {
            return ExecutableFacts.Derive(path);
        }

        try
        {
            // Off the UI thread because the chosen path may be on a slow or
            // network volume.
            return await Task.Run(() => _inspector.Inspect(path));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // A file that cannot be read is not a failed add; the path alone
            // still derives a title from the folder name.
            return ExecutableFacts.Derive(path);
        }
    }

    private bool CanSearchIgdb => _igdb is not null && !string.IsNullOrWhiteSpace(DraftTitle);

    // Searches IGDB using the title field as the query. Correcting the title
    // and searching again is how the user overrides a wrong proposal.
    [RelayCommand(CanExecute = nameof(CanSearchIgdb))]
    private async Task SearchIgdbAsync(CancellationToken ct)
    {
        if (_igdb is null || _searching)
        {
            return;
        }

        _searching = true;
        try
        {
            MatchProblem = null;
            MatchNote = null;
            MatchStatus = LibrarySettingsCopy.MatchSearchingStatus;

            var results = await _igdb.SearchAsync(DraftTitle.Trim(), ct);

            Candidates = [.. results.Select(r => new IgdbCandidateViewModel(r, _covers))];
            Searched = true;
            RequestCandidateCovers();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            MatchStatus = null;
            _searching = false;
        }
    }

    // Confirming a proposal fills the form and writes nothing. The user may
    // still edit every field; Save is what creates the entry.
    [RelayCommand]
    private void UseCandidate(IgdbCandidateViewModel? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        DraftTitle = candidate.Name;
        _proposedTitle = candidate.Name;
        DraftIgdbId = candidate.IgdbId.ToString(CultureInfo.InvariantCulture);

        if (candidate.FirstReleaseYear is { } year and > 0)
        {
            DraftYear = year.ToString(CultureInfo.InvariantCulture);
        }

        TitleError = null;
        YearError = null;
        IgdbIdError = null;

        Candidates = [];
        Searched = false;
        MatchProblem = null;
        MatchNote = LibrarySettingsCopy.MatchChosenNote(candidate.Name);
    }

    // The proposal folds away and the form stays exactly as typed, so a game
    // IGDB knows nothing about is still added by hand.
    [RelayCommand]
    private void DismissMatch()
    {
        Candidates = [];
        Searched = false;
        MatchStatus = null;
        MatchNote = null;
        MatchProblem = null;
    }

    // Candidate thumbnails decode at the width they are drawn at. The render
    // scaling is a fact of the window, handed in by the view.
    public void SetCoverScaling(double scaling)
    {
        if (scaling <= 0)
        {
            return;
        }

        _candidateCoverWidthPixels = GameIgdbMatchViewModel.CandidateCoverWidth * scaling;
        RequestCandidateCovers();
    }

    private void RequestCandidateCovers()
    {
        foreach (var candidate in Candidates)
        {
            candidate.RequestCover(_candidateCoverWidthPixels);
        }
    }

    [RelayCommand]
    private async Task BeginEditAsync(ManualEntryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        ClearForm();
        Editing = row;
        FormTitle = string.Format(
            CultureInfo.CurrentCulture, LibrarySettingsCopy.FormEditTitleFormat, row.Title);

        DraftTitle = row.Title;
        DraftYear = row.Entry.FirstReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        DraftPlatform = row.Entry.PlatformLabel ?? string.Empty;
        DraftExecutable = row.Entry.ExecutablePath ?? string.Empty;

        // Both ids are written as given on save, so an edit that opened with
        // them empty would clear them.
        if (_works is not null && await _works.GetAsync(row.Entry.WorkId) is { IgdbId: { } igdbId })
        {
            DraftIgdbId = igdbId.ToString(CultureInfo.InvariantCulture);
        }

        if (_releases is not null)
        {
            var externalIds = await _releases.GetExternalIdsAsync(row.Entry.ReleaseId);
            DraftSteamAppId = externalIds
                .FirstOrDefault(x => x.Provider == ExternalIdProviders.Steam)?.ProviderId
                ?? string.Empty;
        }

        IsFormOpen = true;
    }

    [RelayCommand]
    private void CancelForm()
    {
        ClearForm();
        Editing = null;
        IsFormOpen = false;
    }

    /// <summary>
    /// Validates in the form, then lets the repository decide the two things
    /// only it can — a blank title and an id already in the library — and puts
    /// each answer under the field it is about. A conflict is raised before
    /// anything is written, so a refused save leaves the library as it was.
    /// </summary>
    [RelayCommand]
    private async Task SaveFormAsync()
    {
        if (_manual is null)
        {
            return;
        }

        TitleError = null;
        YearError = null;
        IgdbIdError = null;
        SteamAppIdError = null;
        Problem = null;

        if (string.IsNullOrWhiteSpace(DraftTitle))
        {
            TitleError = LibrarySettingsCopy.TitleRequiredError;
            return;
        }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(DraftYear))
        {
            if (!int.TryParse(
                    DraftYear.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed is < 1000 or > 9999)
            {
                YearError = LibrarySettingsCopy.YearInvalidError;
                return;
            }

            year = parsed;
        }

        long? igdbId = null;
        if (!string.IsNullOrWhiteSpace(DraftIgdbId))
        {
            if (!long.TryParse(
                    DraftIgdbId.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                IgdbIdError = LibrarySettingsCopy.NumberInvalidError;
                return;
            }

            igdbId = parsed;
        }

        var appId = DraftSteamAppId.Trim();
        if (appId.Length > 0 && !appId.All(char.IsAsciiDigit))
        {
            SteamAppIdError = LibrarySettingsCopy.NumberInvalidError;
            return;
        }

        var draft = new ManualGameDraft
        {
            Title = DraftTitle,
            FirstReleaseYear = year,
            PlatformLabel = Nullable(DraftPlatform),
            ExecutablePath = Nullable(DraftExecutable),
            IgdbId = igdbId,
            SteamAppId = appId.Length == 0 ? null : appId,
        };

        try
        {
            if (Editing is { } editing)
            {
                await _manual.UpdateAsync(editing.OwnershipId, draft);
            }
            else
            {
                await _manual.CreateAsync(draft);
            }
        }
        catch (ManualEntryConflictException conflict)
        {
            // The repository names the field, so the message goes under the box
            // to fix rather than at the foot of the form.
            switch (conflict.Field)
            {
                case nameof(ManualGameDraft.IgdbId):
                    IgdbIdError = LibrarySettingsCopy.IdConflictError;
                    break;

                case nameof(ManualGameDraft.SteamAppId):
                    SteamAppIdError = LibrarySettingsCopy.IdConflictError;
                    break;

                default:
                    Problem = LibrarySettingsCopy.SaveProblem;
                    break;
            }

            return;
        }
        catch (ArgumentException)
        {
            TitleError = LibrarySettingsCopy.TitleRequiredError;
            return;
        }
        catch (InvalidOperationException)
        {
            Problem = LibrarySettingsCopy.SaveProblem;
            return;
        }

        ClearForm();
        Editing = null;
        IsFormOpen = false;

        await RefreshManualAsync(CancellationToken.None);
        await (ReloadLibrary?.Invoke() ?? Task.CompletedTask);
    }

    /// <summary>Opens the confirmation. Deleting never happens on one click (§12.3).</summary>
    [RelayCommand]
    private void BeginDelete(ManualEntryRowViewModel? row) => PendingDelete = row;

    [RelayCommand]
    private void CancelDelete() => PendingDelete = null;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (_manual is null || PendingDelete is null)
        {
            return;
        }

        var ownershipId = PendingDelete.OwnershipId;
        PendingDelete = null;
        Problem = null;

        try
        {
            await _manual.DeleteAsync(ownershipId);
        }
        catch (InvalidOperationException)
        {
            Problem = LibrarySettingsCopy.DeleteProblem;
            return;
        }

        // The form may have been open on the row that just went.
        if (Editing?.OwnershipId == ownershipId)
        {
            CancelForm();
        }

        await RefreshManualAsync(CancellationToken.None);
        await (ReloadLibrary?.Invoke() ?? Task.CompletedTask);
    }

    private void ClearForm()
    {
        DraftTitle = string.Empty;
        DraftYear = string.Empty;
        DraftPlatform = string.Empty;
        DraftExecutable = string.Empty;
        DraftIgdbId = string.Empty;
        DraftSteamAppId = string.Empty;
        TitleError = null;
        YearError = null;
        IgdbIdError = null;
        SteamAppIdError = null;
        Problem = null;

        _proposedTitle = null;
        ExecutableNote = null;
        MatchNote = null;
        MatchStatus = null;
        MatchProblem = null;
        Candidates = [];
        Searched = false;
    }

    private static string? Nullable(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// One row of the HIDDEN GAMES list: the title, how many store entries come
/// back with it, and when it was hidden.
/// </summary>
public sealed class HiddenGameRowViewModel
{
    public HiddenGameRowViewModel(HiddenGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        WorkId = game.WorkId;
        Title = game.Title;
        HiddenAt = game.HiddenAt;
        StoreEntryCount = game.StoreEntryCount;
    }

    public long WorkId { get; }

    public string Title { get; }

    public DateTime HiddenAt { get; }

    public int StoreEntryCount { get; }

    public string StoreEntryCountText
        => StoreEntryCount.ToString("N0", CultureInfo.CurrentCulture);

    public string StoreEntryUnitLabel => StoreEntryCount == 1
        ? LibrarySettingsCopy.StoreEntryUnitSingular
        : LibrarySettingsCopy.StoreEntryUnitPlural;

    /// <summary>When it was hidden, in the local zone and the short date form.</summary>
    public string HiddenAtText
        => HiddenAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public string UnhideText => LibrarySettingsCopy.UnhideButton;

    public string UnhideTooltip => LibrarySettingsCopy.UnhideTooltip;

    public string UnhideAutomationName => string.Format(
        CultureInfo.CurrentCulture, LibrarySettingsCopy.UnhideAutomationFormat, Title);
}

/// <summary>
/// One row of the ADDED BY HAND list: the entry as stored, plus the Edit and
/// Delete controls that act on it.
/// </summary>
public sealed class ManualEntryRowViewModel
{
    public ManualEntryRowViewModel(ManualEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
    }

    public ManualEntry Entry { get; }

    public long OwnershipId => Entry.OwnershipId;

    public string Title => Entry.Title;

    /// <summary>Year and platform label on one line, whichever of them exist.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>(2);
            if (Entry.FirstReleaseYear is { } year)
            {
                parts.Add(year.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(Entry.PlatformLabel))
            {
                parts.Add(Entry.PlatformLabel);
            }

            return string.Join(" · ", parts);
        }
    }

    public bool HasDetail => Detail.Length > 0;

    public string ExecutablePath => Entry.ExecutablePath ?? string.Empty;

    public bool HasExecutable => !string.IsNullOrWhiteSpace(Entry.ExecutablePath);

    public string EditText => LibrarySettingsCopy.EditButton;

    public string EditAutomationName => string.Format(
        CultureInfo.CurrentCulture, LibrarySettingsCopy.EditAutomationFormat, Title);

    public string DeleteText => LibrarySettingsCopy.DeleteButton;

    public string DeleteAutomationName => string.Format(
        CultureInfo.CurrentCulture, LibrarySettingsCopy.DeleteAutomationFormat, Title);
}
