using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

public partial class ArtworkBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IArtworkBrowserService _service;
    private readonly long _workId;
    private readonly ICoverLeases? _covers;
    private readonly IImageFilePicker? _picker;
    private readonly Func<string, Task>? _afterCommit;
    private readonly Dictionary<ArtworkSlot, ArtworkSlotState> _slots = [];
    private CancellationTokenSource _visit = new();
    private bool _disposed;
    private LeasedCover? _preview;

    public ArtworkBrowserViewModel(IArtworkBrowserService service, long workId, string title,
        ICoverLeases? covers = null, IImageFilePicker? picker = null, Func<string, Task>? afterCommit = null)
    {
        _service = service; _workId = workId; Title = title; _covers = covers; _picker = picker; _afterCommit = afterCommit;
        foreach (var slot in Enum.GetValues<ArtworkSlot>())
            _slots.Add(slot, new(slot, service.Sources.Select(source => new ArtworkSourceViewModel(this, source, slot)).ToArray()));
    }

    public string Title { get; }
    public ArtworkSlotState State => _slots[Slot];
    public IReadOnlyList<ArtworkSourceViewModel> Sources => State.Sources;
    public IEnumerable<ArtworkSourceViewModel> VisibleSources => Sources.Where(source => State.SourceId == "all" || source.Id == State.SourceId);
    public ArtworkCandidateViewModel? Current => State.Current;
    public string SourceId => State.SourceId;
    public string SourceLabel => State.SourceId == "all" ? "All sources" : Sources.FirstOrDefault(source => source.Id == State.SourceId)?.Name ?? State.SourceId;
    public GameLink? SourceLink => Uri.TryCreate(Selected?.Candidate.PageUrl, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
        ? GameLink.Create("Open artwork source", uri.AbsoluteUri) : null;
    public bool HasSourceLink => SourceLink is not null;
    public bool IsHero => Slot == ArtworkSlot.Hero;
    public bool IsCover => Slot == ArtworkSlot.Cover;
    public bool IsIcon => Slot == ArtworkSlot.Icon;
    public bool ShowDesktopHero => !PreviewFullscreen;
    public bool ShowFullscreenHero => PreviewFullscreen;
    public bool CanPickFile => _picker is not null;
    public bool CanApply => !IsBusy && Selected is not null && !Selected.IsCurrent;
    public bool CanEdit => !IsBusy;
    public string PreviewDescription => Selected?.Description ?? "Select artwork to preview it.";
    public bool HasPreview => Preview is not null;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool HasProblem => !string.IsNullOrWhiteSpace(Problem);
    public event EventHandler? Closed;

    [ObservableProperty] public partial bool IsOpen { get; set; }
    [ObservableProperty] public partial ArtworkSlot Slot { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] public partial string? Status { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasProblem))] public partial string? Problem { get; set; }
    [ObservableProperty] public partial string ImportUrl { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowDesktopHero)), NotifyPropertyChangedFor(nameof(ShowFullscreenHero))]
    public partial bool PreviewFullscreen { get; set; }
    [RelayCommand] private void DesktopCrop() => PreviewFullscreen = false;
    [RelayCommand] private void FullscreenCrop() => PreviewFullscreen = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply)), NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply)), NotifyPropertyChangedFor(nameof(PreviewDescription)), NotifyPropertyChangedFor(nameof(SourceLink)), NotifyPropertyChangedFor(nameof(HasSourceLink))]
    public partial ArtworkCandidateViewModel? Selected { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPreview))] public partial Bitmap? Preview { get; set; }

    partial void OnSelectedChanged(ArtworkCandidateViewModel? oldValue, ArtworkCandidateViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        _preview?.Dispose();
        _preview = new(_covers, newValue?.Candidate.PreviewKey, CoverLayers.Vivid, art => Preview = art?.Vivid);
        _preview.Request(1200);
    }

    partial void OnSlotChanged(ArtworkSlot oldValue, ArtworkSlot newValue)
    {
        _slots[oldValue].Selected = Selected;
        PublishSlot();
    }

    private void PublishSlot()
    {
        Selected = State.Selected ?? State.Current;
        foreach (var name in new[] { nameof(State), nameof(Sources), nameof(VisibleSources), nameof(Current), nameof(SourceId), nameof(SourceLabel), nameof(IsHero), nameof(IsCover), nameof(IsIcon) }) OnPropertyChanged(name);
    }

    [RelayCommand] private Task Open() => OpenAsync();
    public async Task OpenAsync(ArtworkSlot slot = ArtworkSlot.Hero)
    {
        if (_disposed) return;
        if (!IsOpen)
        {
            _visit.Dispose(); _visit = new();
            foreach (var state in _slots.Values)
            {
                state.CurrentLoaded = false;
                foreach (var source in state.Sources.Where(source => source.CanRetry || source.Items.Count == 0)) source.Loaded = false;
            }
        }
        IsOpen = true; Slot = slot; PublishSlot(); Problem = null;
        await LoadSlotAsync(slot, _visit.Token);
    }

    [RelayCommand] private async Task ChooseSlotAsync(ArtworkSlot slot)
    {
        if (IsBusy) return;
        Slot = slot; Problem = null;
        await LoadSlotAsync(slot, _visit.Token);
    }

    private async Task LoadSlotAsync(ArtworkSlot slot, CancellationToken ct)
    {
        var state = _slots[slot];
        if (!state.CurrentLoaded)
        {
            try
            {
                var current = await _service.GetCurrentAsync(_workId, slot, ct);
                if (ct.IsCancellationRequested || _disposed) return;
                state.Current?.Dispose();
                state.Current = current is null ? null : new(current with { IsCurrent = true }, _covers);
                state.CurrentLoaded = true;
                if (Slot == slot) PublishSlot();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { if (Slot == slot && !ct.IsCancellationRequested) Problem = "Could not load current artwork. Reopen the browser to try again."; }
        }
        await Task.WhenAll(state.Sources.Where(source => !source.Loaded).Select(source => LoadSourceAsync(source, false, ct)));
    }

    [RelayCommand] private void ChooseSource(string id)
    {
        State.SourceId = id;
        OnPropertyChanged(nameof(SourceId)); OnPropertyChanged(nameof(SourceLabel)); OnPropertyChanged(nameof(VisibleSources));
    }
    [RelayCommand] private void Select(ArtworkCandidateViewModel candidate) { if (!IsBusy) Selected = candidate; }

    internal async Task LoadSourceAsync(ArtworkSourceViewModel source, bool more, CancellationToken ct = default)
    {
        if (_disposed || source.IsLoading && !source.RequestToken.IsCancellationRequested) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _visit.Token);
        var token = linked.Token;
        source.RequestToken = token;
        var generation = ++source.Generation;
        source.IsLoading = true; source.CanRetry = false; source.Message = "Loading artwork…";
        try
        {
            if (!source.SupportsSlot)
            { source.Message = $"{source.Name} does not offer {source.Slot.ToString().ToLowerInvariant()} artwork."; source.Loaded = true; return; }
            var page = await _service.BrowseAsync(_workId, source.Slot, source.Id, more ? source.NextCursor : null, token);
            if (token.IsCancellationRequested || _disposed || generation != source.Generation) return;
            if (!more)
            {
                if (source.Items.Contains(_slots[source.Slot].Selected!)) _slots[source.Slot].Selected = null;
                if (source.Items.Contains(Selected!)) Selected = _slots[Slot].Current;
                foreach (var old in source.Items) old.Dispose(); source.Items.Clear();
            }
            foreach (var candidate in page.Items)
                if (!source.Items.Any(item => item.Candidate.AssetId == candidate.AssetId)) source.Items.Add(new(candidate, _covers));
            source.NextCursor = page.NextCursor; source.CanRetry = page.CanRetry; source.Loaded = true;
            source.Message = page.Message ?? (source.Items.Count == 0 ? "No artwork available for this game." : null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { if (generation == source.Generation && !token.IsCancellationRequested) { source.Message = "Could not load artwork. Try again."; source.CanRetry = true; } }
        finally { if (generation == source.Generation) source.IsLoading = false; }
    }

    [RelayCommand] private Task ApplyAsync() => Selected is { } selected && CanApply
        ? WriteAsync(ct => _service.SaveAsync(_workId, Slot, selected.Candidate, ct)) : Task.CompletedTask;
    [RelayCommand] private Task AutomaticAsync() => WriteAsync(ct => _service.ResetAsync(_workId, Slot, ct));
    [RelayCommand] private Task ImportFromUrlAsync() => WriteAsync(ct => _service.ImportUrlAsync(_workId, Slot, ImportUrl, ct));
    [RelayCommand] private async Task ChooseFileAsync()
    {
        if (IsBusy || _picker is null) return;
        try { if (await _picker.PickAsync("Choose artwork", _visit.Token) is { } path && IsOpen) await ImportFileAsync(path); }
        catch (OperationCanceledException) { }
        catch (Exception) { Problem = "Could not open the file picker. Try again."; }
    }
    public Task ImportFileAsync(string path) => WriteAsync(ct => _service.ImportFileAsync(_workId, Slot, path, ct));

    private async Task WriteAsync(Func<CancellationToken, Task<ArtworkSaveResult>> write)
    {
        if (IsBusy || _disposed) return;
        IsBusy = true; Problem = null; Status = "Saving artwork…";
        var slot = Slot;
        var committed = false;
        try
        {
            var result = await write(_visit.Token);
            committed = result.Success;
            if (_disposed) return;
            if (!result.Success) { Problem = result.Message; Status = null; return; }
            Status = result.Message;
            _slots[slot].Selected = null;
            Selected = null;
            _slots[slot].CurrentLoaded = false;
            if (_afterCommit is not null)
            {
                try { await _afterCommit(result.Message); }
                catch (Exception) { Problem = "Artwork saved, but the library could not refresh. Reopen this game to refresh it."; }
            }
            await LoadSlotAsync(slot, _visit.Token);
        }
        catch (OperationCanceledException) when (_visit.IsCancellationRequested) { Status = null; }
        catch (Exception)
        {
            Problem = committed ? "Artwork saved, but the library could not refresh. Reopen this game to refresh it." : "Could not save artwork. Try again.";
            if (!committed) Status = null;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] public void Close()
    {
        if (IsBusy) return;
        _visit.Cancel(); IsOpen = false; Selected = null; Problem = null;
        foreach (var state in _slots.Values) { if (state.Selected is { } selected) selected.IsSelected = false; state.Selected = null; }
        Closed?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose()
    {
        _disposed = true; _visit.Cancel(); _visit.Dispose(); _preview?.Dispose();
        foreach (var state in _slots.Values) { state.Current?.Dispose(); foreach (var source in state.Sources) foreach (var item in source.Items) item.Dispose(); }
        GC.SuppressFinalize(this);
    }
}

public sealed class ArtworkSlotState(ArtworkSlot slot, IReadOnlyList<ArtworkSourceViewModel> sources)
{
    public ArtworkSlot Slot { get; } = slot;
    public IReadOnlyList<ArtworkSourceViewModel> Sources { get; } = sources;
    public string SourceId { get; set; } = "all";
    public ArtworkCandidateViewModel? Current { get; set; }
    public ArtworkCandidateViewModel? Selected { get; set; }
    internal bool CurrentLoaded { get; set; }
}

public partial class ArtworkSourceViewModel : ObservableObject
{
    private readonly ArtworkBrowserViewModel _browser;
    public ArtworkSourceViewModel(ArtworkBrowserViewModel browser, ArtworkBrowserSource source, ArtworkSlot slot)
    { _browser = browser; Id = source.Id; Name = source.Name; Slot = slot; SupportsSlot = source.Slots.Contains(slot); }
    public string Id { get; }
    public string Name { get; }
    public ArtworkSlot Slot { get; }
    public bool SupportsSlot { get; }
    public bool Loaded { get; set; }
    internal int Generation { get; set; }
    internal CancellationToken RequestToken { get; set; }
    public ObservableCollection<ArtworkCandidateViewModel> Items { get; } = [];
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool CanRetry { get; set; }
    [ObservableProperty] public partial string? Message { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMore))] public partial string? NextCursor { get; set; }
    public bool HasMore => NextCursor is not null;
    [RelayCommand] private Task RetryAsync() => _browser.LoadSourceAsync(this, false);
    [RelayCommand] private Task MoreAsync() => _browser.LoadSourceAsync(this, true);
}

public partial class ArtworkCandidateViewModel : ObservableObject, IDisposable
{
    private readonly LeasedCover _thumbnail;
    public ArtworkCandidateViewModel(ArtworkCandidate candidate, ICoverLeases? covers)
    { Candidate = candidate; _thumbnail = new(covers, candidate.ThumbnailKey ?? candidate.PreviewKey, CoverLayers.Vivid, art => Thumbnail = art?.Vivid); _thumbnail.Request(300); }
    public ArtworkCandidate Candidate { get; }
    public string Id => $"{Candidate.SourceId}:{Candidate.AssetId}";
    public bool IsCurrent => Candidate.IsCurrent;
    public string Description => string.Join(" · ", new[] { Candidate.SourceName,
        Candidate.Width is { } width && Candidate.Height is { } height ? $"{width} × {height}" : null,
        Candidate.Creator is { Length: > 0 } creator ? $"By {creator}" : null }.Where(text => text is not null));
    public string StateLabel => IsSelected ? IsCurrent ? "Current · Selected" : "Selected" : IsCurrent ? "Current" : "Preview";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StateLabel))] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial Bitmap? Thumbnail { get; set; }
    public void Dispose() { _thumbnail.Dispose(); GC.SuppressFinalize(this); }
}
