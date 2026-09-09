using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenSettingsPage : FullscreenPage
{
    private string _section = "Appearance";
    private readonly Dictionary<Control, Action<int>> _adjustments = [];
    private readonly List<Action> _valueRefreshers = [];
    private Control? _focused;
    private Control? _initial;
    private readonly SemaphoreSlim _libraryRefresh = new(1);
    private int _refreshVersion;
    public Task PendingLibraryRefresh { get; private set; } = Task.CompletedTask;
    public override string Title => "Settings";
    public override string Hints => _section == "Appearance" ? "← / →  Adjust     A  Select     Y  Reset page" : "A  Select     B  Back";

    public FullscreenSettingsPage(FullscreenContext context) : base(context)
    {
        Render();
        context.PreferencesChanged += RefreshValues;
        AttachedToVisualTree += RefreshValues;
    }

    private void RefreshValues(object? sender, EventArgs e)
    {
        foreach (var refresh in _valueRefreshers) refresh();
    }

    public override void Dispose() { Context.PreferencesChanged -= RefreshValues; base.Dispose(); }

    private void Render()
    {
        _adjustments.Clear();
        _valueRefreshers.Clear();
        _initial = null;
        var tabs = new[] { "Appearance", "Controller", "Library", "Platforms", "Application" }.Select(label =>
            FullscreenUi.Button(label == _section ? $"{label}  •" : label, () => { _section = label; Render(); FocusInitial(); })).ToArray();
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var tab in tabs) { nav.Children.Add(tab); tab.GotFocus += (_, _) => _focused = tab; }
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
        void Adjust(string label, string description, Func<string> value, Action<int> change)
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
            rows.Children.Add(button); focus.Add([button]);
        }
        string On(bool value) => value ? "On" : "Off";
        if (_section == "Appearance")
        {
            Adjust("Text size", "Adjust until this reads comfortably from your seat.", () => $"{Context.TextScale:P0}", d => Context.TextScale = Math.Clamp(Math.Round(Context.TextScale + d * .1, 1), 1, 1.4));
            string ThemeLabel() => $"Theme     {Context.Themes.FirstOrDefault(t => t.Id == Context.ThemeId)?.Name ?? Context.ThemeId}";
            var theme = Action(ThemeLabel(), () => Context.ShowActions("Fullscreen theme", Context.Themes.Select(theme => new FullscreenAction(theme.Name, () => { Context.ThemeId = theme.Id; Render(); FocusInitial(); })).ToArray()));
            _valueRefreshers.Add(() => { theme.Content = ThemeLabel(); AutomationProperties.SetName(theme, ThemeLabel()); });
            Adjust("Screen margins", "Keep important content within your TV’s safe area.", () => $"{Context.SafeMarginPercent:0}%", d => Context.SafeMarginPercent = Math.Clamp(Context.SafeMarginPercent + d, 0, 10));
            Adjust("Reduce motion", "Minimise animations and motion effects.", () => On(Context.ReducedMotion), _ => Context.ReducedMotion = !Context.ReducedMotion);
            Adjust("Dim dormant covers", "Slightly dim games you haven’t played recently.", () => On(Context.DimCovers), _ => Context.DimCovers = !Context.DimCovers);
        }
        else if (_section == "Controller")
        {
            rows.Children.Add(FullscreenUi.Text("D-pad or left stick moves between choices.\nA selects. B returns one level.\nBumpers change the main screen.\nTriggers page through games or activity.\nMenu opens the quick menu.", 32));
            rows.Children.Add(FullscreenUi.Text("Keyboard: arrows move, Enter selects, Escape returns. Your place is kept if a controller disconnects.", 28, "TextDim"));
        }
        else if (_section == "Library")
        {
            var display = Context.Shared.Display;
            Adjust("Journal after playing", "Ask for a note after a session.", () => On(display.PromptAfterPlay), _ => display.PromptAfterPlay = !display.PromptAfterPlay);
            Adjust("Non-game entries", "Include tools and other library entries.", () => On(display.ShowNonGameEntries), _ => display.ShowNonGameEntries = !display.ShowNonGameEntries);
            Adjust("Group expansions", "Show expansions with their base game.", () => On(display.GroupExpansions), _ => display.GroupExpansions = !display.GroupExpansions);
            Adjust("Explicit content", Context.Shared.LibrarySettings.ExplicitDefaultNote, () => On(Context.Shared.LibrarySettings.ShowExplicitContent), _ => Context.Shared.LibrarySettings.ShowExplicitContent = !Context.Shared.LibrarySettings.ShowExplicitContent);
            Adjust("Content age limit", "Shared with your desktop library.", () => display.MaturityCapLabel, d => display.MaturityCapIndex = Math.Clamp(display.MaturityCapIndex + d, 0, DisplaySettingsViewModel.MaximumCapIndex));
            Action("Library tools", () => Context.Push(new FullscreenLibraryToolsPage(Context)));
        }
        else if (_section == "Platforms")
        {
            var stores = Context.Shared.Stores;
            Action($"Steam     {stores.SteamStatusLabel}", () => Context.Push(new FullscreenPlatformPage(Context, "Steam")));
            Action($"Epic     {stores.EpicStatusLabel}", () => Context.Push(new FullscreenPlatformPage(Context, "Epic")));
            Action($"GOG     {stores.GogStatusLabel}", () => Context.Push(new FullscreenPlatformPage(Context, "GOG")));
        }
        else
        {
            var app = Context.Shared.ApplicationSettings;
            Adjust("Minimize to tray", "Keep Winnow running when minimized.", () => On(app.MinimizeToTray), _ => app.MinimizeToTray = !app.MinimizeToTray);
            Adjust("Close to tray", "Keep Winnow running when its window is closed.", () => On(app.CloseToTray), _ => app.CloseToTray = !app.CloseToTray);
            if (app.IsStartupSupported) Adjust("Start with Windows", "Start Winnow when you sign in.", () => On(app.StartWithWindows), _ => app.StartWithWindows = !app.StartWithWindows);
            rows.Children.Add(FullscreenUi.Text($"Winnow {app.ApplicationVersion}", 28, "TextDim"));
        }
        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), ColumnSpacing = 56 };
        main.Children.Add(FullscreenUi.Scroll(rows));
        var preview = FullscreenUi.Stack(FullscreenUi.Text(_section == "Appearance" ? "PREVIEW" : _section.ToUpperInvariant(), 24, "TextDim"),
            FullscreenUi.Text("Your next game is already here.", 48),
            FullscreenUi.Text(_section == "Appearance" ? "Changes here apply to fullscreen. Your desktop layout stays the same." : "Library and account settings apply to both desktop and fullscreen.", 28, "TextDim"));
        Grid.SetColumn(preview, 1); main.Children.Add(preview);
        if (_section == "Appearance" && Context.Library.VisibleTiles.FirstOrDefault() is { } sample)
        {
            preview.Children.Clear();
            preview.Children.Add(FullscreenUi.Text("PREVIEW", 24, "TextDim"));
            preview.Children.Add(new FullscreenCover(sample) { Height = 320, HorizontalAlignment = HorizontalAlignment.Stretch });
            preview.Children.Add(FullscreenUi.Text(sample.Title, 48));
            preview.Children.Add(FullscreenUi.Text(sample.UnreadText, 28));
            preview.Children.Add(FullscreenUi.Text("Changes here apply to fullscreen. Your desktop layout stays the same.", 28, "TextDim"));
        }
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 24 };
        layout.Children.Add(FullscreenUi.Text("Make yourself comfortable", 64)); Grid.SetRow(nav, 1); layout.Children.Add(nav); Grid.SetRow(main, 2); layout.Children.Add(main);
        Content = layout; SetFocusRows(focus.ToArray()); Changed();
    }

    public override bool Handle(GamepadButtons buttons)
    {
        if (_focused?.IsFocused == true && _adjustments.TryGetValue(_focused, out var adjust))
        {
            if ((buttons & GamepadButtons.Left) != 0) { adjust(-1); return true; }
            if ((buttons & GamepadButtons.Right) != 0) { adjust(1); return true; }
        }
        if (_section == "Appearance" && (buttons & GamepadButtons.Keyboard) != 0)
        {
            Context.ShowActions("Reset fullscreen text size, theme, margins, motion and cover dimming?", [new("Reset fullscreen appearance", () =>
            { Context.TextScale = 1; Context.SafeMarginPercent = 5; Context.ReducedMotion = false; Context.DimCovers = true; Context.ThemeId = "winnow"; Render(); FocusInitial(); }), new("Cancel", () => { })]);
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

public sealed class FullscreenPlatformPage : FullscreenPage
{
    private readonly string _platform;
    public override string Title => _platform;
    public FullscreenPlatformPage(FullscreenContext context, string platform) : base(context)
    {
        _platform = platform;
        var stores = context.Shared.Stores;
        var body = FullscreenUi.Stack(FullscreenUi.Text(platform, 64));
        var status = FullscreenUi.Text("", 28, "TextDim");
        var controls = new List<Control[]>();
        void Add(string text, Action action) { var b = FullscreenUi.Button(text, action); body.Children.Add(b); controls.Add([b]); }
        if (platform == "Steam")
        {
            body.Children.Add(FullscreenUi.Text(stores.SteamConnectionMessage));
            body.Children.Add(FullscreenUi.Text(stores.SteamLocalMessage, 28, "TextDim"));
            if (stores.SteamHasSession) Add("Sign out of Steam", () => context.ShowActions(stores.SteamSignOutMessage, [new("Sign out", async () => { await stores.SignOutOfSteamCommand.ExecuteAsync(null); context.Back(); }), new("Cancel", () => { })]));
            if (stores.SteamSignInAvailable) Add(stores.SteamSignInButtonText, () => context.Push(new FullscreenSteamConsentPage(context, () => status.Text = stores.SteamSignInProblemMessage ?? stores.SteamSignInNoticeMessage ?? stores.SteamConnectionMessage)));
            else body.Children.Add(FullscreenUi.Text(stores.SteamSignInUnavailableMessage, 28, "TextDim"));
            Add("Steam Web API key", () => context.Push(new FullscreenSteamApiKeyPage(context)));
            if (stores.ShowPurchaseImport) Add("Purchase history", () => context.Push(new FullscreenPurchaseHistoryPage(context)));
            body.Children.Add(FullscreenUi.Text(stores.AccountScopeMessage));
            body.Children.Add(FullscreenUi.Text(stores.AccountScopeCaveatMessage, 24, "TextDim"));
            if (stores.CanChooseAccountScope) Add(stores.AccountScopeToggleLabel, async () =>
            {
                stores.ShowOwnAccountOnly = !stores.ShowOwnAccountOnly;
                try { await stores.PendingAccountScopeSave; await context.RefreshAsync(); status.Text = $"{stores.AccountScopeToggleLabel}: {(stores.ShowOwnAccountOnly ? "On" : "Off")}"; }
                catch (Exception) { status.Text = "Couldn't change account visibility. Try again."; }
            });
            else body.Children.Add(FullscreenUi.Text(stores.AccountScopeBlockedMessage, 28, "TextDim"));
        }
        else if (platform == "Epic")
        {
            body.Children.Add(FullscreenUi.Text(stores.EpicIsSignedIn ? stores.EpicAccountLine : stores.EpicLocalMessage));
            if (stores.EpicIsSignedIn) Add("Sign out of Epic", () => context.ShowActions(stores.EpicSignOutMessage, [new("Sign out", async () => { await stores.SignOutOfEpicCommand.ExecuteAsync(null); context.Back(); }), new("Cancel", () => { })]));
            if (stores.EpicCanSignIn) Add(stores.EpicSignInButtonText, async () =>
            {
                try { await stores.SignInToEpicCommand.ExecuteAsync(null); status.Text = stores.EpicIsSignedIn ? stores.EpicAccountLine : stores.EpicProblemMessage ?? stores.EpicStatusLabel; }
                catch (Exception) { status.Text = "Couldn't sign in to Epic. Try again."; }
            });
        }
        else { body.Children.Add(FullscreenUi.Text(stores.GogLocalMessage)); body.Children.Add(FullscreenUi.Text(stores.GogNoSignInMessage)); }
        if (platform != "GOG") body.Children.Add(FullscreenUi.Text("The sign-in window supports controller field navigation and text entry. Provider challenges may still ask for a phone or pointer.", 28, "TextDim"));
        body.Children.Add(status); Add("Back", context.Back); Content = FullscreenUi.Scroll(body); SetFocusRows(controls.ToArray());
    }
}
