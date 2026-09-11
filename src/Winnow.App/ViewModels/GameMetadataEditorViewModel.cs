using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// Per-field metadata editor for one work. Each field carries its own value
/// and its own source; a manual edit sets one field and makes the user its
/// source, leaving every other field tracking its own. A metadata fetch
/// (the IGDB assignment control in the same modal) rewrites every field in
/// one pass.
///
/// <para>Opened from "Edit details" in More (§10.3), beside "Wrong game?",
/// in focused content beneath the persistent header. Omitting any one optional
/// constructor argument costs exactly that one capability; with no
/// IWorkMetadataEditService registered or no resolved work id the row is
/// not drawn at all.</para>
/// </summary>
public partial class GameMetadataEditorViewModel : ObservableObject, IDisposable
{
    private readonly IWorkMetadataEditService _service;
    private readonly ICoverLeases? _covers;
    private readonly IImageFilePicker? _picker;

    /// <summary>
    /// Refreshes library artwork after an art field is saved. The library
    /// keeps this editor and its drafts alive during the refresh. With no
    /// delegate supplied the confirmation lands on the row instead.
    /// </summary>
    private readonly Func<string, Task>? _afterArtChange;

    /// <summary>
    /// Invalidates visible metadata and filter facts after any saved text
    /// field. The library preserves this editor and its other drafts.
    /// </summary>
    private readonly Func<string, string?, Task>? _afterTextChange;

    private readonly long _workId;

    private double _scaling = 1;

    public GameMetadataEditorViewModel(
        IWorkMetadataEditService service,
        long workId,
        WorkMetadataSnapshot? snapshot = null,
        ICoverLeases? covers = null,
        IImageFilePicker? picker = null,
        Func<string, Task>? afterArtChange = null,
        Func<string, string?, Task>? afterTextChange = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _workId = workId;
        _covers = covers;
        _picker = picker;
        _afterArtChange = afterArtChange;
        _afterTextChange = afterTextChange;
        Note = note;

        if (snapshot is not null)
        {
            Adopt(snapshot);
        }
    }

    /// <summary>Whether the editor surface is disclosed.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>One row per <see cref="WorkFields.All"/> entry, in display order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial IReadOnlyList<MetadataFieldRowViewModel> Rows { get; set; } = [];

    /// <summary>Status field, in words. Null when nothing is in flight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; set; }

    /// <summary>A refusal sentence, drawn in Amber.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    /// <summary>
    /// Confirmation after an art save refreshes visible artwork. It remains
    /// available outside the editor when the user returns to details.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string? Note { get; set; }

    public bool HasNote => Note is not null;

    /// <summary>One busy flag for the whole editor: a second write cannot start while one is in flight.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Whether the work has a live IGDB pin.</summary>
    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    public bool HasRows => Rows.Count > 0;

    public bool HasStatus => Status is not null;

    public bool HasProblem => Problem is not null;

    /// <summary>
    /// The menu row's label, constant regardless of whether the section is
    /// open. The row names an action ("Edit details"), not a state; a toggle
    /// label that flipped to "Close" said nothing about what it closed.
    /// </summary>
    public string OpenLabel => GameMetadataEditorCopy.OpenLabel;

    public string OpenTooltip => GameMetadataEditorCopy.OpenTooltip;

    public string SectionHeading => GameMetadataEditorCopy.SectionHeading;

    public string CloseTooltip => GameMetadataEditorCopy.CloseTooltip;

    public string CloseAutomationName => GameMetadataEditorCopy.CloseAutomationName;

    public string Intro => GameMetadataEditorCopy.Intro;

    /// <summary>
    /// Sets the display resolution for art previews from the view's own render
    /// scaling. Same arrangement the IGDB assignment control uses.
    /// </summary>
    public void SetCoverScaling(double scaling)
    {
        if (scaling <= 0)
        {
            return;
        }

        _scaling = scaling;
        RequestPreviews();
    }

    /// <summary>
    /// Loads the field rows on first opening only. Reopening preserves drafts and
    /// the host restores focus. The editor stays in the modal tree, outside the
    /// More popup, so its fields use the same focus treatment as the rest of details.
    /// </summary>
    [RelayCommand]
    private async Task OpenAsync(CancellationToken ct)
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        Problem = null;

        if (!HasRows)
        {
            await LoadAsync(ct);
        }
    }

    /// <summary>
    /// Returns to details without discarding field drafts. The editor close button,
    /// the shared Back control and Escape all use this command.
    /// </summary>
    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>Reads the six field states. The status field is words; there is no spinner.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Problem = null;
            Status = GameMetadataEditorCopy.LoadingStatus;

            var snapshot = await _service.GetAsync(_workId, ct);
            Status = null;

            if (snapshot is null)
            {
                Problem = GameMetadataEditorCopy.LoadFailedText;
                return;
            }

            Adopt(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            Status = null;
            IsBusy = false;
        }
    }

    /// <summary>Writes one field's value, making the user its source.</summary>
    internal async Task<WorkFieldEditOutcome> SetFieldAsync(
        string field, string? value, CancellationToken ct)
        => await _service.SetFieldAsync(_workId, field, value, ct);

    /// <summary>Hands a field back to automatic, clearing its source.</summary>
    internal async Task<WorkFieldEditOutcome> ResetFieldAsync(string field, CancellationToken ct)
        => await _service.ResetFieldAsync(_workId, field, ct);

    /// <summary>Imports art from a local file into the cover cache.</summary>
    internal async Task<WorkArtEditOutcome> SetArtFromFileAsync(
        string field, string path, CancellationToken ct)
        => await _service.SetArtFromFileAsync(_workId, field, path, ct);

    /// <summary>Imports art from a URL into the cover cache.</summary>
    internal async Task<WorkArtEditOutcome> SetArtFromUrlAsync(
        string field, string url, CancellationToken ct)
        => await _service.SetArtFromUrlAsync(_workId, field, url, ct);

    /// <summary>Opens the file dialog if a picker is registered; null otherwise.</summary>
    internal Task<string?> PickImageAsync(string title, CancellationToken ct)
        => _picker is null ? Task.FromResult<string?>(null) : _picker.PickAsync(title, ct);

    /// <summary>True when a file picker is registered and the choose-a-file route should be drawn.</summary>
    internal bool CanPickFiles => _picker is not null;

    /// <summary>Resolves a stored art URL to a <see cref="CoverKey"/> for the preview.</summary>
    internal CoverKey? ArtKeyFor(string? value) => _service.ArtKeyFor(value);

    internal ICoverLeases? Covers => _covers;

    internal double Scaling => _scaling;

    /// <summary>Takes the editor-wide busy flag. A second write cannot start while one is in flight.</summary>
    internal void Enter() => IsBusy = true;

    /// <summary>Releases the editor-wide busy flag and re-arms every row's commands.</summary>
    internal void Leave() => IsBusy = false;

    /// <summary>
    /// Re-reads the whole work after a write and refreshes every row's value
    /// and source, so a row never shows a source the store no longer agrees
    /// with. Only the row named by <paramref name="committed"/> has its draft
    /// replaced; every other row keeps whatever the user was typing.
    /// </summary>
    internal async Task RefreshAsync(string committed, CancellationToken ct)
    {
        var snapshot = await _service.GetAsync(_workId, ct);
        if (snapshot is null)
        {
            return;
        }

        IsPinned = snapshot.IsPinned;

        var byField = snapshot.Fields.ToDictionary(f => f.Field, StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            if (byField.TryGetValue(row.Field, out var state))
            {
                row.Adopt(state, resetDraft: string.Equals(row.Field, committed, StringComparison.Ordinal));
            }
        }

        RequestPreviews();
    }

    /// <summary>Fires the art-change reload if a delegate was supplied.</summary>
    internal async Task AfterArtChangeAsync(string note)
    {
        if (_afterArtChange is null)
        {
            return;
        }

        await _afterArtChange(note);
    }

    /// <summary>Whether an art save triggers a library reload.</summary>
    internal bool ReloadsOnArtChange => _afterArtChange is not null;

    /// <summary>
    /// Fires the text-change delegate if one was supplied. Called after
    /// the rows have been refreshed, so the value it carries is the value
    /// as stored, not the raw draft.
    /// </summary>
    internal async Task AfterTextChangeAsync(string field, string? value)
    {
        if (_afterTextChange is null)
        {
            return;
        }

        await _afterTextChange(field, value);
    }

    /// <summary>
    /// Builds one row per <see cref="WorkFields.All"/> entry: an art row for
    /// the two art fields and a text row for the rest. A field the snapshot
    /// did not mention is built on automatic rather than dropped.
    /// </summary>
    private void Adopt(WorkMetadataSnapshot snapshot)
    {
        IsPinned = snapshot.IsPinned;

        var byField = snapshot.Fields.ToDictionary(f => f.Field, StringComparer.Ordinal);
        var rows = new List<MetadataFieldRowViewModel>(WorkFields.All.Count);

        foreach (var field in WorkFields.All)
        {
            var state = byField.TryGetValue(field, out var found)
                ? found
                : new WorkMetadataField(field, null, null);

            rows.Add(WorkFields.IsArt(field)
                ? new MetadataArtRowViewModel(this, state)
                : new MetadataTextRowViewModel(this, state));
        }

        var outgoing = Rows;
        Rows = rows;
        foreach (var row in outgoing)
        {
            row.Dispose();
        }

        RequestPreviews();
    }

    private void RequestPreviews()
    {
        foreach (var row in Rows)
        {
            row.RequestPreview();
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var row in Rows)
        {
            row.NotifyCommands();
        }
    }

    /// <summary>
    /// Releases every art row's preview lease. The detail modal that owns the
    /// editor disposes it when it closes.
    /// </summary>
    public void Dispose()
    {
        foreach (var row in Rows)
        {
            row.Dispose();
        }
    }
}

/// <summary>
/// One row per field. Carries the field key, its display label, its current
/// value, an editable draft, its source, and per-row Save and Reset commands.
/// There is no form-wide Save: a single Save would be the whole-record gesture
/// this model exists to stop being the only one available. Note, Problem and
/// Status are per row, not per form, because the message lands under the field
/// it concerns (§16.3).
/// </summary>
public abstract partial class MetadataFieldRowViewModel : ObservableObject, IDisposable
{
    private readonly GameMetadataEditorViewModel _editor;

    protected MetadataFieldRowViewModel(GameMetadataEditorViewModel editor, WorkMetadataField state)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(state);

        _editor = editor;
        Field = state.Field;
        Value = state.Value;
        Source = state.Source;
        Draft = InitialDraft(state.Value);
    }

    /// <summary>Releases whatever the row holds. Only an art row holds anything.</summary>
    public virtual void Dispose()
    {
    }

    /// <summary>The <see cref="WorkFields"/> constant this row edits.</summary>
    public string Field { get; }

    /// <summary>The stored value. Null when no writer has claimed the field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValue))]
    public partial string? Value { get; set; }

    /// <summary>
    /// Who last wrote this field, or null for automatic. Reset is only
    /// available when the source is <see cref="FieldSources.User"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUserOwned))]
    [NotifyPropertyChangedFor(nameof(IsAutomatic))]
    [NotifyPropertyChangedFor(nameof(SourceLabel), nameof(MenuLabel), nameof(CanReset))]
    [NotifyPropertyChangedFor(nameof(SourceTooltip))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial string? Source { get; set; }

    /// <summary>The user's in-progress edit. Save is dead until this differs from the stored value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Draft { get; set; } = string.Empty;

    /// <summary>Confirmation sentence, drawn under the field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string? Note { get; set; }

    /// <summary>A refusal sentence, drawn in Amber under the field.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    /// <summary>Status field, in words. Null when nothing is in flight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; set; }

    public string Label => GameMetadataEditorCopy.LabelFor(Field);

    public string Watermark => GameMetadataEditorCopy.WatermarkFor(Field);

    public string FieldAutomationName => GameMetadataEditorCopy.FieldAutomationName(Label);

    public string SaveLabel => GameMetadataEditorCopy.SaveLabel;

    public string SaveAutomationName => GameMetadataEditorCopy.SaveAutomationName(Label);

    public string ResetLabel => GameMetadataEditorCopy.ResetLabel;

    public string ResetTooltip => GameMetadataEditorCopy.ResetTooltip;

    public string ResetAutomationName => GameMetadataEditorCopy.ResetAutomationName(Label);

    public bool IsUserOwned => FieldSources.IsUserOwned(Source);

    public bool IsAutomatic => Source is null;

    public string SourceLabel => GameMetadataEditorCopy.SourceLabelFor(Source);
    public string MenuLabel => $"{Label} · {SourceLabel}";

    public string SourceTooltip => GameMetadataEditorCopy.SourceTooltipFor(Source);

    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public bool HasNote => Note is not null;

    public bool HasProblem => Problem is not null;

    public bool HasStatus => Status is not null;

    /// <summary>True when the draft differs from the stored value.</summary>
    public bool IsDirty => !string.Equals(Draft, InitialDraft(Value), StringComparison.Ordinal);

    public virtual bool IsMultiline => false;

    public virtual bool IsNumeric => WorkFields.IsNumeric(Field);

    public virtual double FieldHeight => 30;

    public virtual TextWrapping Wrapping => TextWrapping.NoWrap;

    public virtual bool IsArt => false;

    protected GameMetadataEditorViewModel Editor => _editor;

    /// <summary>Save is available when the draft differs from the stored value and no write is in flight.</summary>
    public bool CanSave => !_editor.IsBusy && IsDirty;
    public bool CanEdit => !_editor.IsBusy;

    /// <summary>Reset is available only when the user is the source. A field nobody has claimed and a field a service owns have nothing to hand back.</summary>
    public bool CanReset => !_editor.IsBusy && IsUserOwned;

    public virtual void RequestPreview()
    {
    }

    /// <summary>Refreshes the stored value and source from a re-read snapshot.</summary>
    internal void Adopt(WorkMetadataField state, bool resetDraft)
    {
        Value = state.Value;
        Source = state.Source;

        if (resetDraft)
        {
            Draft = InitialDraft(state.Value);
        }

        OnPropertyChanged(nameof(IsDirty));
        NotifyCommands();
    }

    internal void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanReset));
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    protected static string InitialDraft(string? value) => value ?? string.Empty;

    protected static string? Normalise(string? draft)
        => string.IsNullOrWhiteSpace(draft) ? null : draft.Trim();

    protected void Clear()
    {
        Note = null;
        Problem = null;
    }

    /// <summary>Saves this field only, makes the user its source, then refreshes all rows.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (_editor.IsBusy)
        {
            return;
        }

        _editor.Enter();
        try
        {
            Clear();
            Status = SavingStatus;

            await ApplyAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            Status = null;
            _editor.Leave();
        }
    }

    /// <summary>Hands this field back to automatic, clearing its source.</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ResetAsync(CancellationToken ct)
    {
        if (_editor.IsBusy || !IsUserOwned)
        {
            return;
        }

        _editor.Enter();
        try
        {
            Clear();
            Status = GameMetadataEditorCopy.ResettingStatus;

            var outcome = await _editor.ResetFieldAsync(Field, ct);
            Status = null;

            if (outcome != WorkFieldEditOutcome.Applied)
            {
                Problem = GameMetadataEditorCopy.ProblemFor(outcome);
                return;
            }

            await _editor.RefreshAsync(Field, ct);
            Note = GameMetadataEditorCopy.ResetNote(Label);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            Status = null;
            _editor.Leave();
        }
    }

    protected abstract string SavingStatus { get; }

    protected abstract Task ApplyAsync(CancellationToken ct);
}

/// <summary>
/// Row for the four non-art fields. <c>summary</c> is the one multi-line
/// field; <c>first_release_year</c> is the one numeric field and draws in
/// Plex Mono with tabular figures. The year check restates the bounds
/// <see cref="WorkFieldEditOutcome.InvalidValue"/> enforces — a whole number
/// between 1900 and 2200 — so a typo becomes a sentence under the field
/// rather than a round trip; the repository stays the authority.
/// </summary>
public sealed partial class MetadataTextRowViewModel : MetadataFieldRowViewModel
{
    public MetadataTextRowViewModel(GameMetadataEditorViewModel editor, WorkMetadataField state)
        : base(editor, state)
    {
    }

    public override bool IsMultiline
        => string.Equals(Field, WorkFields.Summary, StringComparison.Ordinal);

    public override double FieldHeight => IsMultiline ? 108 : 30;

    public override TextWrapping Wrapping
        => IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap;

    protected override string SavingStatus => GameMetadataEditorCopy.SavingStatus;

    protected override async Task ApplyAsync(CancellationToken ct)
    {
        var value = Normalise(Draft);

        if (IsNumeric && value is not null && !IsAYear(value))
        {
            Problem = GameMetadataEditorCopy.ProblemFor(WorkFieldEditOutcome.InvalidValue);
            return;
        }

        var outcome = await Editor.SetFieldAsync(Field, value, ct);
        Status = null;

        if (outcome != WorkFieldEditOutcome.Applied)
        {
            Problem = GameMetadataEditorCopy.ProblemFor(outcome);
            return;
        }

        await Editor.RefreshAsync(Field, ct);
        // After the refresh so Value is the stored value, not the raw draft.
        await Editor.AfterTextChangeAsync(Field, Value);
        Note = GameMetadataEditorCopy.SavedNote(Label);
    }

    private static bool IsAYear(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            && year is >= 1900 and <= 2200;
}

/// <summary>
/// Row for <c>cover_url</c> and <c>background_url</c>. The draft is a URL
/// (Save is the use-a-URL route), and <see cref="ChooseFileCommand"/> is
/// the local-file route through <see cref="IImageFilePicker"/>. The stored
/// value is an opaque <c>winnow://user-art/</c> reference that nobody can
/// usefully type; <see cref="IWorkMetadataEditService.ArtKeyFor"/> turns it
/// into a <see cref="CoverKey"/> for the preview.
///
/// <para>The file dialog is opened before the busy flag is taken, so a user
/// browsing does not lock the editor and a dismissed dialog writes nothing.
/// Every refusal on the art seam is a returned enum value and never an
/// exception, exactly like <see cref="IIgdbAssignmentService"/>.</para>
/// </summary>
public sealed partial class MetadataArtRowViewModel : MetadataFieldRowViewModel
{
    /// <summary>
    /// The row's preview, leased. Vivid only, and replaced rather than reused
    /// when the field's value changes, because the new value is a different
    /// cover key.
    /// </summary>
    private LeasedCover? _preview;

    public MetadataArtRowViewModel(GameMetadataEditorViewModel editor, WorkMetadataField state)
        : base(editor, state)
    {
    }

    /// <summary>Art preview, drawn at full saturation — the dormancy ramp is a scanning aid and the user is not scanning.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial Bitmap? Preview { get; set; }

    public override bool IsArt => true;

    public override bool IsNumeric => false;

    public bool IsCover => string.Equals(Field, WorkFields.CoverUrl, StringComparison.Ordinal);

    /// <summary>Cover is 64x96 (the 2:3 portrait the whole grid is made of); background is 96x54.</summary>
    public double PreviewWidth => IsCover ? 64 : 96;

    /// <inheritdoc cref="PreviewWidth"/>
    public double PreviewHeight => IsCover ? 96 : 54;

    /// <summary>A row with no art draws a placeholder, never a hole.</summary>
    public bool ShowPlaceholder => Preview is null;

    public bool CanPickFile => Editor.CanPickFiles;

    public string ChooseFileLabel => GameMetadataEditorCopy.ChooseFileLabel;

    public string ChooseFileTooltip => GameMetadataEditorCopy.ChooseFileTooltip;

    public string ChooseFileAutomationName
        => GameMetadataEditorCopy.ChooseFileAutomationName(Label);

    public string NoArtText => GameMetadataEditorCopy.NoArtText;

    protected override string SavingStatus => GameMetadataEditorCopy.UrlStatus;

    /// <summary>Rides the existing cover-cache image path and adds no new one.</summary>
    public override void RequestPreview()
    {
        // Dispose clears Preview, so the row shows its placeholder from here
        // until the new key resolves — the same as before this held a lease.
        _preview?.Dispose();
        _preview = null;

        if (Editor.ArtKeyFor(Value) is not { } key)
        {
            return;
        }

        var widthPixels = PreviewWidth * Editor.Scaling;
        if (widthPixels <= 0)
        {
            return;
        }

        _preview = new LeasedCover(
            Editor.Covers, key, CoverLayers.Vivid, art => Preview = art?.Vivid);

        _preview.Request(widthPixels);
    }

    /// <summary>Releases the preview's lease.</summary>
    public override void Dispose()
    {
        _preview?.Dispose();
        _preview = null;
        base.Dispose();
    }

    protected override async Task ApplyAsync(CancellationToken ct)
    {
        var url = Normalise(Draft);
        if (url is null)
        {
            Problem = GameMetadataEditorCopy.ProblemFor(WorkArtEditOutcome.BadUrl);
            return;
        }

        await ApplyArtAsync(() => Editor.SetArtFromUrlAsync(Field, url, ct), ct);
    }

    /// <summary>
    /// Opens the file dialog, then imports the chosen file. The dialog runs
    /// before the busy flag is taken, so the user can browse without locking
    /// the editor.
    /// </summary>
    [RelayCommand]
    private async Task ChooseFileAsync(CancellationToken ct)
    {
        if (Editor.IsBusy || !CanPickFile)
        {
            return;
        }

        // Dialog opens before Enter() so a user browsing does not lock the editor.
        var path = await Editor.PickImageAsync(
            GameMetadataEditorCopy.FilePickerTitle(Label), ct);

        if (path is null)
        {
            return;
        }

        await ImportFileAsync(path, ct);
    }

    /// <summary>Imports a path selected by either presentation's file picker.</summary>
    internal async Task ImportFileAsync(string path, CancellationToken ct = default)
    {
        if (Editor.IsBusy) return;
        Editor.Enter();
        try
        {
            Clear();
            Status = GameMetadataEditorCopy.FileStatus;

            await ApplyArtAsync(() => Editor.SetArtFromFileAsync(Field, path, ct), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            Status = null;
            Editor.Leave();
        }
    }

    private async Task ApplyArtAsync(Func<Task<WorkArtEditOutcome>> write, CancellationToken ct)
    {
        var outcome = await write();
        Status = null;

        if (outcome != WorkArtEditOutcome.Applied)
        {
            Problem = GameMetadataEditorCopy.ProblemFor(outcome);
            return;
        }

        await Editor.RefreshAsync(Field, ct);

        var note = GameMetadataEditorCopy.SavedNote(Label);
        if (!Editor.ReloadsOnArtChange)
        {
            Note = note;
            return;
        }

        await Editor.AfterArtChangeAsync(note);
    }

}
