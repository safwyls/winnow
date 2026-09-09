using System.Globalization;
using System.ComponentModel;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.App.Themes;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Lists;
using Winnow.Core.Repositories;

namespace Winnow.App.Views.Fullscreen;

/// <summary>TV presentation state is independent; persistence and domain operations are shared.</summary>
public sealed class FullscreenContext : IDisposable
{
    public LibraryViewModel Library { get; }
    public FeedViewModel Feed { get; }
    public MainWindowViewModel Shared { get; }
    public IServiceProvider? Services { get; }
    public event Action<FullscreenPage>? PageRequested;
    public event Action? BackRequested;
    public event Action<TextBox>? TextRequested;
    public event Action<string>? Notice;
    public event Action<GameDetailsViewModel?>? DetailsChanged;
    public event EventHandler? PreferencesChanged;
    public Func<string, IReadOnlyList<string>?, Task<string?>>? FilePicker { get; set; }
    public Func<string, string, Task<string?>>? SaveFilePicker { get; set; }
    private readonly SemaphoreSlim _writes = new(1);
    private double _textScale = 1, _safeMargin = 5;
    private bool _reducedMotion, _fitUltrawide, _dimCovers = true;
    private bool _openingGame;
    private bool _disposed, _refreshPending, _active;
    private Task? _refreshTask;
    public double TextScale { get => _textScale; set { _textScale = Math.Clamp(value, .7, 1.4); Preference("text-scale", _textScale.ToString(CultureInfo.InvariantCulture)); } }
    public double SafeMarginPercent { get => _safeMargin; set { _safeMargin = Math.Clamp(value, 0, 10); Preference("safe-margin", _safeMargin.ToString(CultureInfo.InvariantCulture)); } }
    public bool ReducedMotion { get => _reducedMotion; set { _reducedMotion = value; Library.Ramp.ReducedMotion = value; Preference("reduced-motion", value.ToString()); } }
    public bool DimCovers { get => _dimCovers; set { _dimCovers = value; Library.Ramp.DimsDormantCovers = value; Preference("dim-covers", value.ToString()); } }
    public bool FitUltrawide => _fitUltrawide;
    public void SetFitUltrawide(bool value) { _fitUltrawide = value; Preference("fit-ultrawide", value.ToString()); }
    public string ThemeId
    {
        get => Shared.Appearance.Service.Theme.Id;
        set { if (Themes.FirstOrDefault(t => t.Id == value) is { } theme) Shared.Appearance.Service.SelectTheme(theme); }
    }
    public IReadOnlyList<WinnowTheme> Themes => Shared.Appearance.Service.Catalogue;

    public FullscreenContext(LibraryViewModel library, FeedViewModel feed, MainWindowViewModel shared, IServiceProvider? services = null)
    {
        Library = library; Feed = feed; Shared = shared; Services = services;
        library.PropertyChanged += LibraryChanged;
        feed.PropertyChanged += FeedChanged;
        shared.Appearance.Service.Applied += ThemeChanged;
        if (!ReferenceEquals(shared.Library, library)) shared.Library.TilesChanged += SharedTilesChanged;
    }
    public static FullscreenContext Create(IServiceProvider services, MainWindowViewModel shared)
    {
        var journal = new JournalPromptViewModel();
        var launch = ActivatorUtilities.CreateInstance<LaunchStatusViewModel>(services);
        var library = ActivatorUtilities.CreateInstance<LibraryViewModel>(services, new DormancyRamp(), journal, launch);
        var feed = ActivatorUtilities.CreateInstance<FeedViewModel>(services, library, library.Lists);
        return new(library, feed, shared, services);
    }
    public async Task LoadAsync()
    {
        if (_disposed) return;
        if (Services?.GetService<ISettingsRepository>() is { } settings)
        {
            if (double.TryParse(await settings.GetAsync("fullscreen.text-scale"), CultureInfo.InvariantCulture, out var scale)) _textScale = Math.Clamp(scale, .7, 1.4);
            if (double.TryParse(await settings.GetAsync("fullscreen.safe-margin"), CultureInfo.InvariantCulture, out var margin)) _safeMargin = Math.Clamp(margin, 0, 10);
            if (bool.TryParse(await settings.GetAsync("fullscreen.reduced-motion"), out var motion)) _reducedMotion = motion;
            if (bool.TryParse(await settings.GetAsync("fullscreen.dim-covers"), out var dim)) _dimCovers = dim;
            if (bool.TryParse(await settings.GetAsync("fullscreen.fit-ultrawide"), out var fit)) _fitUltrawide = fit;
        }
        if (_disposed) return;
        PreferencesChanged?.Invoke(this, EventArgs.Empty);
        Library.ShowExplicitContent = Shared.LibrarySettings.ShowExplicitContent;
        Library.ShowNonGameEntries = Shared.Library.ShowNonGameEntries;
        Library.GroupExpansions = Shared.Library.GroupExpansions;
        Library.MaturityCap = Shared.Library.MaturityCap;
        Library.Ramp.ReducedMotion = _reducedMotion;
        Library.Ramp.DimsDormantCovers = _dimCovers;
        await RefreshAsync();
    }
    private void LibraryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.Prompt) && Library.Prompt is { } prompt) OpenPrompt(prompt);
        if (e.PropertyName == nameof(LibraryViewModel.Details) && !_openingGame) DetailsChanged?.Invoke(Library.Details);
    }
    private void ThemeChanged(object? sender, EventArgs e) => PreferencesChanged?.Invoke(this, EventArgs.Empty);
    private void FeedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FeedViewModel.ListPrompt) && Feed.ListPrompt is { } prompt) OpenPrompt(prompt);
    }
    private async void SharedTilesChanged(object? sender, EventArgs e)
    {
        if (!_active) { _refreshPending = true; return; }
        try { await RefreshAsync(); }
        catch (Exception) { if (!_disposed) Notify("Could not refresh the library. Try again."); }
    }
    public void SetActive(bool active)
    {
        _active = active;
        if (active && _refreshPending) SharedTilesChanged(this, EventArgs.Empty);
    }
    public Task RefreshAsync()
    {
        if (_disposed) return Task.CompletedTask;
        _refreshPending = true;
        if (_refreshTask is not { IsCompleted: false }) _refreshTask = RefreshCoreAsync();
        return _refreshTask;
    }
    private async Task RefreshCoreAsync()
    {
        while (_refreshPending && !_disposed)
        {
            while (!_disposed && (Library.LoadCommand.IsRunning || Library.OpenDetailsCommand.IsRunning || _openingGame ||
                Library.Details?.MetadataEditor?.IsBusy == true || Library.Details?.Refetch?.RefetchCommand.IsRunning == true ||
                Library.Details?.IgdbMatch?.AssignCommand.IsRunning == true || Library.Details?.IgdbMatch?.ClearCommand.IsRunning == true ||
                Library.Details?.IgdbMatch?.LinkClaimCommand.IsRunning == true ||
                Library.Details?.Coverage?.SeparateCommand.IsRunning == true || Library.Details?.Expansions?.UngroupCommand.IsRunning == true))
                await Task.Delay(100);
            if (_disposed) return;
            _refreshPending = false;
            Library.ShowExplicitContent = Shared.LibrarySettings.ShowExplicitContent;
            Library.ShowNonGameEntries = Shared.Library.ShowNonGameEntries;
            Library.GroupExpansions = Shared.Library.GroupExpansions;
            Library.MaturityCap = Shared.Library.MaturityCap;
            await Library.LoadCommand.ExecuteAsync(null);
            if (_disposed) return;
            // The library's TilesChanged observer already schedules this score pass.
            if (Feed.LoadCommand.ExecutionTask is { } feedLoad) await feedLoad;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Library.PropertyChanged -= LibraryChanged;
        Feed.PropertyChanged -= FeedChanged;
        Shared.Appearance.Service.Applied -= ThemeChanged;
        Shared.Library.TilesChanged -= SharedTilesChanged;
        if (!ReferenceEquals(Library, Shared.Library))
        {
            Feed.Dispose();
            Library.CloseDetailsCommand.Execute(null);
            Library.Journal.Dispose();
            if (!ReferenceEquals(Library.LaunchStatus, Shared.Library.LaunchStatus)) Library.LaunchStatus.Dispose();
        }
        GC.SuppressFinalize(this);
    }
    private async void Preference(string key, string value)
    {
        PreferencesChanged?.Invoke(this, EventArgs.Empty);
        if (Services?.GetService<ISettingsRepository>() is not { } settings) return;
        await _writes.WaitAsync();
        try { await settings.SetAsync("fullscreen." + key, value); }
        catch (Exception) { Notify("Could not save the fullscreen preference."); }
        finally { _writes.Release(); }
    }
    public void Push(FullscreenPage page) { if (_disposed) page.Dispose(); else PageRequested?.Invoke(page); }
    public void Back() { if (!_disposed) BackRequested?.Invoke(); }
    public void EditText(TextBox text) { if (!_disposed) TextRequested?.Invoke(text); }
    public void Notify(string text) { if (!_disposed) Notice?.Invoke(text); }
    public Task<string?> PickFile(string title, IReadOnlyList<string>? extensions = null) => FilePicker?.Invoke(title, extensions) ?? Task.FromResult<string?>(null);
    public Task<string?> SaveFile(string title, string suggestedName) => SaveFilePicker?.Invoke(title, suggestedName) ?? Task.FromResult<string?>(null);
    public void BrowseFolder(string path) => Push(new FullscreenFilePage(this, "Installed files", null,
        new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously), path, browse: true));
    public void ShowActions(string title, IReadOnlyList<FullscreenAction> actions) => Push(new FullscreenActionsPage(this, title, actions));
    public void OpenPrompt(ActionPromptViewModel prompt) => Push(new FullscreenPromptPage(this, prompt));
    public async void OpenGame(GameTileViewModel tile)
    {
        if (_disposed || _openingGame) return;
        _openingGame = true;
        try
        {
            await Library.OpenDetailsCommand.ExecuteAsync(tile);
            if (_disposed) { Library.CloseDetailsCommand.Execute(null); return; }
            if (Library.Details is { } details) Push(new FullscreenDetailsPage(this, details));
        }
        catch (Exception) { Notify("Could not open this game. Try again."); }
        finally { _openingGame = false; }
    }
    public async void Play(GameTileViewModel tile)
    {
        if (_disposed) return;
        try
        {
            await Library.LaunchCommand.ExecuteAsync(tile);
        }
        catch (Exception) { Notify("Could not start this game. Try again."); }
    }
    public void ChooseVersion(GameTileViewModel tile)
    {
        ShowActions("Choose version", tile.Entries.Select(entry => new FullscreenAction(
            $"{entry.StoreName} · {(entry.Installed is true ? "Installed" : entry.Installed is false ? "Not installed" : "Install state unknown")} · {entry.PrimaryAction?.Label ?? "Unavailable"}",
            () => Play(new GameTileViewModel([entry], tile.Game, tile.Title, DateTime.UtcNow, ramp: Library.Ramp)),
            entry.PrimaryAction is not null)).Append(new FullscreenAction("Cancel", () => { })).ToArray());
    }
    public async void OpenLink(GameLink link)
    {
        if (_disposed) return;
        try
        {
            var uri = new Uri(link.Uri);
            if (!link.IsLauncherProtocol && Services?.GetService<Winnow.Core.Reading.IPatchNotesReader>() is { IsAvailable: true } reader &&
                reader.Open(uri, Library.Details?.Title ?? link.Label) == Winnow.Core.Reading.PatchNotesOutcome.Opened)
                return;
            if (link.IsLauncherProtocol && Services?.GetService<GameLaunchService>() is { } launcher && Library.Details is { } details)
                await launcher.LaunchAsync(details.Tile.PlayableEntry.OwnershipId, link);
            else if (Services?.GetService<IUriDispatcher>() is not { } dispatcher || !await dispatcher.OpenAsync(uri))
                Notify("Could not open this link.");
        }
        catch (Exception) { Notify("Could not open this link."); }
    }
}

internal sealed class FullscreenActionsPage : FullscreenPage
{
    private readonly string _title;
    public override string Title => _title;
    public FullscreenActionsPage(FullscreenContext context, string title, IReadOnlyList<FullscreenAction> actions) : base(context)
    {
        _title = title;
        var buttons = actions.Select(action => { var button = FullscreenUi.Button(action.Label, () => { Context.Back(); action.Invoke(); }); button.IsEnabled = action.IsEnabled; return button; }).ToArray();
        Content = FullscreenUi.Scroll(FullscreenUi.Stack([FullscreenUi.Text(title, 64), .. buttons]));
        SetFocusRows(buttons.Select(b => new Control[] { b }).ToArray());
    }
}

internal sealed class FullscreenPromptPage : FullscreenPage
{
    private readonly ActionPromptViewModel _prompt;
    public FullscreenPromptPage(FullscreenContext context, ActionPromptViewModel prompt) : base(context)
    {
        _prompt = prompt;
        var panel = FullscreenUi.Stack(FullscreenUi.Text(prompt.Question, 48));
        var rows = new List<Control[]>();
        if (prompt.Note is { } note) panel.Children.Add(FullscreenUi.Text(note));
        if (prompt.HasInput)
        {
            var text = new TextBox { Text = prompt.Text, Watermark = prompt.InputWatermark, FontSize = 32 };
            text.TextChanged += (_, _) => prompt.Text = text.Text ?? string.Empty;
            panel.Children.Add(text); rows.Add([text]);
        }
        foreach (var choice in prompt.Choices)
        {
            var button = FullscreenUi.Button(choice.Name, async () => { await prompt.ChooseCommand.ExecuteAsync(choice); Context.Back(); });
            panel.Children.Add(button); rows.Add([button]);
        }
        if (!prompt.HasChoices)
        {
            var confirm = FullscreenUi.Button(prompt.ConfirmLabel, async () => { if (!prompt.CanConfirm) return; await prompt.ConfirmCommand.ExecuteAsync(null); Context.Back(); });
            panel.Children.Add(confirm); rows.Add([confirm]);
        }
        var cancel = FullscreenUi.Button("Cancel", () => { prompt.CancelCommand.Execute(null); Context.Back(); });
        panel.Children.Add(cancel); rows.Add([cancel]);
        Content = FullscreenUi.Scroll(panel); SetFocusRows(rows.ToArray());
    }
    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Back)) _prompt.CancelCommand.Execute(null);
        return base.Handle(buttons);
    }
}
