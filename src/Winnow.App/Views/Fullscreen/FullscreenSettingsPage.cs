using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenSettingsPage : FullscreenPage
{
    private static readonly string[] Sections = ["Appearance", "Controller", "Library", "Platforms", "Application"];
    private string _section = "Appearance";
    private readonly Dictionary<Control, Action<int>> _adjustments = [];
    private readonly List<Action> _valueRefreshers = [];
    private Control? _focused;
    private Control? _initial;
    private readonly SemaphoreSlim _libraryRefresh = new(1);
    private int _refreshVersion;
    private bool _disposed;
    public Task PendingLibraryRefresh { get; private set; } = Task.CompletedTask;
    public Task PendingPlatformRefresh { get; private set; } = Task.CompletedTask;
    public override string Title => "Settings";
    public override string Hints => _section == "Appearance" ? "← / →  Adjust     A  Select     Y  Reset page" : "A  Select     B  Back";
    public override string RightHints => "LT / RT  Section";

    public FullscreenSettingsPage(FullscreenContext context, string initialSection = "Appearance") : base(context)
    {
        _section = Sections.Contains(initialSection) ? initialSection : "Appearance";
        Render();
        context.PreferencesChanged += RefreshValues;
        context.Shared.ApplicationSettings.PropertyChanged += ApplicationSettingsChanged;
        AttachedToVisualTree += RefreshValues;
        AttachedToVisualTree += (_, _) => { if (_section == "Platforms") PendingPlatformRefresh = RefreshPlatformsAsync(); };
    }

    private void RefreshValues(object? sender, EventArgs e)
    {
        foreach (var refresh in _valueRefreshers) refresh();
    }

    private void ApplicationSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_section == "Application") RefreshValues(sender, EventArgs.Empty);
    }

    public override void Dispose() { _disposed = true; Context.PreferencesChanged -= RefreshValues; Context.Shared.ApplicationSettings.PropertyChanged -= ApplicationSettingsChanged; base.Dispose(); }

    private async Task RefreshPlatformsAsync()
    {
        try
        {
            var refresh = Context.Shared.Stores.RefreshCommand;
            if (refresh.IsRunning && refresh.ExecutionTask is { } running) await running;
            else await refresh.ExecuteAsync(null);
        }
        catch (Exception) { if (!_disposed) Context.Notify("Couldn't read the platform connection status. Reopen Platforms to try again."); }
    }

    private void Render()
    {
        _adjustments.Clear();
        _valueRefreshers.Clear();
        _initial = null;
        var tabs = Sections.Select(label =>
            FullscreenUi.Button(label, () => { _section = label; Render(); FocusInitial(); })).ToArray();
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var tab in tabs) { tab.Classes.Set("current", Equals(tab.Content, _section)); nav.Children.Add(tab); tab.GotFocus += (_, _) => _focused = tab; }
        var rows = new StackPanel { Spacing = 16 };
        var focus = new List<Control[]> { tabs };
        Button Action(string label, Action action)
        {
            var button = FullscreenUi.Button(label, action); button.MinHeight = 88;
            button.GotFocus += (_, _) => _focused = button;
            _initial ??= button;
            rows.Children.Add(button); focus.Add([button]);
            return button;
        }
        void Adjust(string label, string description, Func<string> value, Action<int> change, bool mouseStepper = false)
        {
            var text = FullscreenHistoryTypography.Data($"‹   {value()}   ›", 28);
            void Change(int direction)
            {
                change(direction); text.Text = $"‹   {value()}   ›";
                if (_section == "Library") PendingLibraryRefresh = RefreshLibraryAsync(++_refreshVersion);
            }
            var button = FullscreenUi.Button(label, () => Change(1));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 };
            grid.Children.Add(FullscreenUi.Stack(FullscreenUi.Text(label, 32), FullscreenUi.Text(description, 24, "TextDim")));
            Grid.SetColumn(text, 1); text.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(text);
            button.Content = grid; button.MinHeight = 104;
            _valueRefreshers.Add(() => { text.Text = $"‹   {value()}   ›"; AutomationProperties.SetItemStatus(button, value()); });
            _initial ??= button;
            _adjustments[button] = direction => { Change(direction); AutomationProperties.SetItemStatus(button, value()); };
            button.GotFocus += (_, _) => _focused = button;
            if (mouseStepper)
            {
                var decrease = FullscreenUi.Button($"Decrease {label.ToLowerInvariant()}", () => Change(-1));
                var increase = FullscreenUi.Button($"Increase {label.ToLowerInvariant()}", () => Change(1));
                decrease.Content = "−"; increase.Content = "+";
                decrease.MinWidth = increase.MinWidth = 64;
                var adjustment = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
                adjustment.Children.Add(button);
                Grid.SetColumn(decrease, 1); adjustment.Children.Add(decrease);
                Grid.SetColumn(increase, 2); adjustment.Children.Add(increase);
                rows.Children.Add(adjustment);
                foreach (var step in new[] { decrease, increase })
                {
                    _adjustments[step] = Change;
                    step.GotFocus += (_, _) => _focused = step;
                }
                focus.Add([button, decrease, increase]);
            }
            else { rows.Children.Add(button); focus.Add([button]); }
        }
        void Toggle(string label, string description, Func<bool> value, Action<bool> change)
        {
            var state = FullscreenUi.Text("", 28);
            var thumb = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16) };
            thumb[!Border.BackgroundProperty] = new DynamicResourceExtension("Text");
            var track = new Border { Width = 80, Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(4), BorderThickness = new Thickness(2), Child = thumb };
            Button button = null!;
            button = FullscreenUi.Button(label, () => Set(!value()));
            button.Classes.Add("tv-toggle");
            void Refresh()
            {
                var enabled = value();
                state.Text = enabled ? "On" : "Off";
                thumb.HorizontalAlignment = enabled ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                track[!Border.BackgroundProperty] = new DynamicResourceExtension(enabled ? "Volt" : "SurfaceRaised");
                track[!Border.BorderBrushProperty] = new DynamicResourceExtension(enabled ? "Volt" : "TextDim");
                thumb[!Border.BackgroundProperty] = new DynamicResourceExtension(enabled ? "VoltInk" : "Text");
                AutomationProperties.SetItemStatus(button, state.Text);
            }
            void Set(bool enabled)
            {
                change(enabled); Refresh();
                if (_section == "Library") PendingLibraryRefresh = RefreshLibraryAsync(++_refreshVersion);
            }
            var valueRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, VerticalAlignment = VerticalAlignment.Center };
            valueRow.Children.Add(state); valueRow.Children.Add(track); state.VerticalAlignment = VerticalAlignment.Center;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 24 };
            grid.Children.Add(FullscreenUi.Stack(FullscreenUi.Text(label, 32), FullscreenUi.Text(description, 24, "TextDim")));
            Grid.SetColumn(valueRow, 1); grid.Children.Add(valueRow); button.Content = grid; button.MinHeight = 104;
            _valueRefreshers.Add(Refresh); _adjustments[button] = direction => Set(direction > 0);
            _initial ??= button; button.GotFocus += (_, _) => _focused = button;
            rows.Children.Add(button); focus.Add([button]); Refresh();
        }
        if (_section == "Appearance")
        {
            Adjust("Text size", "Adjust until this reads comfortably from your seat.", () => $"{Context.TextScale:P0}", d => Context.TextScale = Math.Clamp(Math.Round(Context.TextScale + d * .1, 1), .7, 1.4), mouseStepper: true);
            string ThemeLabel() => $"Theme     {Context.Themes.FirstOrDefault(t => t.Id == Context.ThemeId)?.Name ?? Context.ThemeId}";
            var theme = Action(ThemeLabel(), () => Context.ShowActions("Theme", Context.Themes.Select(theme => new FullscreenAction(theme.Name, () => { Context.ThemeId = theme.Id; Render(); FocusInitial(); })).ToArray()));
            _valueRefreshers.Add(() => { theme.Content = ThemeLabel(); AutomationProperties.SetName(theme, ThemeLabel()); });
            Adjust("Screen margins", "Keep important content within your TV’s safe area.", () => $"{Context.SafeMarginPercent:0}%", d => Context.SafeMarginPercent = Math.Clamp(Context.SafeMarginPercent + d, 0, 10));
            Toggle("Fit ultrawide displays", "Use the full width of your display.", () => Context.FitUltrawide, Context.SetFitUltrawide);
            Toggle("Reduce motion", "Minimise animations and motion effects.", () => Context.ReducedMotion, value => Context.ReducedMotion = value);
            Toggle("Dim dormant covers", "Slightly dim games you haven’t played recently.", () => Context.DimCovers, value => Context.DimCovers = value);
        }
        else if (_section == "Controller")
        {
            rows.Children.Add(ControllerDiagram());
            rows.Children.Add(FullscreenUi.Text("Keyboard: arrows move · Enter selects · Escape returns. Your place is kept if a controller disconnects.", 24, "TextDim"));
        }
        else if (_section == "Library")
        {
            var display = Context.Shared.Display;
            Toggle("Journal after playing", "Ask for a note after a session.", () => display.PromptAfterPlay, value => display.PromptAfterPlay = value);
            Toggle("Non-game entries", "Include tools and other library entries.", () => display.ShowNonGameEntries, value => display.ShowNonGameEntries = value);
            Toggle("Group expansions", "Show expansions with their base game.", () => display.GroupExpansions, value => display.GroupExpansions = value);
            Toggle("Explicit content", Context.Shared.LibrarySettings.ExplicitDefaultNote, () => Context.Shared.LibrarySettings.ShowExplicitContent, value => Context.Shared.LibrarySettings.ShowExplicitContent = value);
            Adjust("Content age limit", "Shared with your desktop library.", () => display.MaturityCapLabel, d => display.MaturityCapIndex = Math.Clamp(display.MaturityCapIndex + d, 0, DisplaySettingsViewModel.MaximumCapIndex));
            Action("Library tools", () => Context.Push(new FullscreenLibraryToolsPage(Context)));
        }
        else if (_section == "Platforms")
        {
            var stores = Context.Shared.Stores;
            foreach (var (platform, property) in new[] { ("Steam", nameof(stores.SteamStatusLabel)), ("Epic", nameof(stores.EpicStatusLabel)), ("GOG", nameof(stores.GogStatusLabel)) })
            {
                var button = Action(platform, () => Context.Push(new FullscreenPlatformPage(Context, platform)));
                button.Bind(ContentControl.ContentProperty, new Binding(property) { Source = stores, StringFormat = platform + "     {0}" });
                button.Bind(AutomationProperties.NameProperty, new Binding(property) { Source = stores, StringFormat = platform + "     {0}" });
            }
            PendingPlatformRefresh = RefreshPlatformsAsync();
        }
        else
        {
            var app = Context.Shared.ApplicationSettings;
            Toggle("Start in fullscreen", app.FullscreenStartupNote, () => app.StartInFullscreen, value => app.StartInFullscreen = value);
            Toggle("Minimize to tray", "Keep Winnow running when minimized.", () => app.MinimizeToTray, value => app.MinimizeToTray = value);
            Toggle("Close to tray", "Keep Winnow running when its window is closed.", () => app.CloseToTray, value => app.CloseToTray = value);
            if (app.IsStartupSupported) Toggle("Start with Windows", "Start Winnow when you sign in.", () => app.StartWithWindows, value => app.StartWithWindows = value);
            rows.Children.Add(FullscreenUi.Text($"Winnow {app.ApplicationVersion}", 28, "TextDim"));
            Action("IGDB metadata", () => Context.Push(new FullscreenIgdbSettingsPage(Context)));
            if (!Context.Shared.Setup.IsOpen)
                Action("Run setup again", () => app.OpenSetupCommand.Execute(null));
            if (app.HasUpdater)
            {
                Toggle("Automatic background updates", app.AutomaticUpdatesNote, () => app.AutomaticUpdates, value => app.AutomaticUpdates = value);
                Toggle("Include beta releases", app.BetaUpdatesNote, () => app.IncludeBetaReleases, value => app.IncludeBetaReleases = value);
                var status = FullscreenUi.Text(app.UpdateStatus, 28);
                status.Bind(TextBlock.TextProperty, new Binding(nameof(app.UpdateStatus)) { Source = app });
                AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
                rows.Children.Add(status);
                var progress = FullscreenHistoryTypography.Data("", 28);
                progress.Bind(TextBlock.TextProperty, new Binding(nameof(app.UpdateProgress)) { Source = app, StringFormat = "Downloaded: {0:0}%" });
                progress.Bind(IsVisibleProperty, new Binding(nameof(app.CanCancelUpdate)) { Source = app });
                rows.Children.Add(progress);
                var version = FullscreenHistoryTypography.Data("", 28);
                version.Bind(TextBlock.TextProperty, new Binding(nameof(app.AvailableVersion)) { Source = app, StringFormat = "Available version: {0}" });
                version.Bind(IsVisibleProperty, new Binding(nameof(app.HasReleaseNotes)) { Source = app });
                rows.Children.Add(version);
                void UpdateAction(string label, System.Windows.Input.ICommand command, string enabled)
                {
                    var button = Action(label, () => { if (command.CanExecute(null)) command.Execute(null); });
                    button.Bind(IsEnabledProperty, new Binding(enabled) { Source = app });
                }
                UpdateAction("Check for updates", app.CheckUpdateCommand, nameof(app.CanCheckUpdate));
                UpdateAction("Download update", app.DownloadUpdateCommand, nameof(app.CanDownloadUpdate));
                UpdateAction("Cancel download", app.CancelUpdateCommand, nameof(app.CanCancelUpdate));
                UpdateAction("Restart to update", app.RestartUpdateCommand, nameof(app.CanRestartUpdate));
                UpdateAction("Release notes", app.OpenReleaseNotesCommand, nameof(app.HasReleaseNotes));
                UpdateAction("Download in browser", app.OpenManualDownloadCommand, nameof(app.HasManualDownload));
            }
        }
        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 56 };
        if (_section == "Controller")
        {
            // The guide is a single screen; give the illustration the remaining height.
            var guide = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 24 };
            var contents = rows.Children.ToArray();
            rows.Children.Clear();
            guide.Children.Add(contents[0]);
            Grid.SetRow(contents[1], 1); guide.Children.Add(contents[1]);
            main.Children.Add(guide);
        }
        else main.Children.Add(FullscreenUi.Scroll(rows));
        var preview = FullscreenUi.Stack(FullscreenUi.Text(_section == "Appearance" ? "PREVIEW" : _section.ToUpperInvariant(), 24, "TextDim"),
            FullscreenUi.Text("Your next game is already here.", 48),
            FullscreenUi.Text(_section == "Appearance" ? "Theme applies to both views. Other appearance settings apply to fullscreen." : "Library and account settings apply to both desktop and fullscreen.", 28, "TextDim"));
        if (_section == "Controller") Grid.SetColumnSpan(main.Children[0], 2);
        else { Grid.SetColumn(preview, 1); main.Children.Add(preview); }
        if (_section == "Appearance" && Context.Library.VisibleTiles.FirstOrDefault() is { } sample)
        {
            preview.Children.Clear();
            preview.Children.Add(FullscreenUi.Text("PREVIEW", 24, "TextDim"));
            preview.Children.Add(new FullscreenCover(sample) { Height = 320, HorizontalAlignment = HorizontalAlignment.Stretch });
            preview.Children.Add(FullscreenUi.Text(sample.Title, 48));
            preview.Children.Add(FullscreenUi.Text(sample.UnreadText, 28));
            preview.Children.Add(FullscreenUi.Text("Theme applies to both views. Other appearance settings apply to fullscreen.", 28, "TextDim"));
        }
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 24 };
        layout.Children.Add(FullscreenUi.Text("Make yourself comfortable", 64)); Grid.SetRow(nav, 1); layout.Children.Add(nav); Grid.SetRow(main, 2); layout.Children.Add(main);
        _initial ??= tabs[Array.IndexOf(Sections, _section)];
        Content = FullscreenAmbientBackdrop.Behind(layout, "settings"); SetFocusRows(focus.ToArray()); Changed();
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if ((buttons & (GamepadButtons.PagePrevious | GamepadButtons.PageNext)) != 0)
        {
            var direction = buttons.HasFlag(GamepadButtons.PageNext) ? 1 : -1;
            _section = Sections[(Array.IndexOf(Sections, _section) + direction + Sections.Length) % Sections.Length];
            Render(); FocusInitial(); return true;
        }
        if (_focused?.IsFocused == true && _adjustments.TryGetValue(_focused, out var adjust))
        {
            if ((buttons & GamepadButtons.Left) != 0) { adjust(-1); return true; }
            if ((buttons & GamepadButtons.Right) != 0) { adjust(1); return true; }
        }
        if (_section == "Appearance" && (buttons & GamepadButtons.Keyboard) != 0)
        {
            Context.ShowActions("Reset fullscreen text size, margins, display fit, motion and cover dimming?", [new("Reset fullscreen appearance", () =>
            { Context.TextScale = 1; Context.SafeMarginPercent = 5; Context.SetFitUltrawide(false); Context.ReducedMotion = false; Context.DimCovers = true; Render(); FocusInitial(); }), new("Cancel", () => { })]);
            return true;
        }
        return base.Handle(buttons);
    }

    public override void FocusInitial()
    {
        if (_focused?.IsAttachedToVisualTree() == true) FocusControl(_focused);
        else if (_initial is not null) FocusControl(_initial);
        else base.FocusInitial();
    }

    private static Control ControllerDiagram()
    {
        var art = FullscreenVectorArt.Load("controller");
        art.Name = "FullscreenControllerDiagram";
        foreach (var (label, x, y) in new[] { ("Y", 41d, 19d), ("X", 37d, 23d), ("B", 45d, 23d), ("A", 41d, 27d) })
        {
            var icon = FullscreenGlyphs.Icon(label, 4);
            Canvas.SetLeft(icon, x); Canvas.SetTop(icon, y); art.Children.Add(icon);
        }
        var diagram = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1.6*,*"), ColumnSpacing = 24 };
        StackPanel Callouts(params (string Glyph, string Label)[] items)
        {
            var panel = new StackPanel { Spacing = 24, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (glyph, label) in items)
            {
                var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
                var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                foreach (var key in glyph.Split('/')) keys.Children.Add(FullscreenGlyphs.Icon(key, 36));
                heading.Children.Add(keys);
                var labelText = FullscreenUi.Text(label, 28);
                Grid.SetColumn(labelText, 1); heading.Children.Add(labelText);
                var line = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 0) };
                line[!Border.BackgroundProperty] = new DynamicResourceExtension("Line");
                var callout = FullscreenUi.Stack(heading, line);
                callout.Spacing = 0; panel.Children.Add(callout);
            }
            return panel;
        }
        diagram.Children.Add(Callouts(("LB/RB", "Main screens"),
            ("LT/RT", "Tabs & shelves"),
            ("Dpad/LS", "Move"),
            ("View", "Search"),
            ("Menu", "Quick menu")));
        var center = new Viewbox { Child = art, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(center, 1); diagram.Children.Add(center);
        var right = Callouts(("Y", "More / filters / reset"),
            ("B", "Back / cancel"),
            ("A", "Select"),
            ("X", "Play / edit note"),
            ("RS", "Scroll long content"));
        Grid.SetColumn(right, 2); diagram.Children.Add(right);
        return diagram;
    }

    private async Task RefreshLibraryAsync(int version)
    {
        await _libraryRefresh.WaitAsync();
        try
        {
            if (version != _refreshVersion) return;
            var display = Context.Shared.Display;
            await Task.WhenAll(display.PendingSave, Context.Shared.LibrarySettings.PendingSave);
            Context.Library.ShowExplicitContent = Context.Shared.LibrarySettings.ShowExplicitContent;
            Context.Library.ShowNonGameEntries = display.ShowNonGameEntries;
            Context.Library.GroupExpansions = display.GroupExpansions;
            Context.Library.MaturityCap = display.MaturityCap;
            await Context.RefreshAsync();
        }
        catch (Exception) { Context.Notify("Couldn't refresh your fullscreen library. Try again."); }
        finally { _libraryRefresh.Release(); }
    }
}
