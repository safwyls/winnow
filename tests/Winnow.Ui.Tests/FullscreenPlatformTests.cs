using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Enrich.SteamWeb.Credentials;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenPlatformTests
{
    [AvaloniaFact]
    public async Task Settings_platform_summary_refreshes_on_open_and_tracks_shared_account_state()
    {
        using var context = Context(new Connections { Epic = new(true, "Test player") });
        using var page = new FullscreenSettingsPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Click(FindButton(page, "Platforms"));
            await page.PendingPlatformRefresh; Dispatcher.UIThread.RunJobs();
            var epic = FindButton(page, "Epic     SIGNED IN");
            epic.Focus();
            await context.Shared.Stores.SignOutOfEpicCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            var labels = epic.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("Epic     NOT SIGNED IN", labels);
            Assert.Contains("Open", labels);
            Assert.Contains("›", labels);
            Assert.Same(epic, window.FocusManager!.GetFocusedElement());
            context.Shared.Stores.SteamSessionState = SteamSessionHealth.Expired;
            Dispatcher.UIThread.RunJobs();
            Assert.True(FindButton(page, "Steam     " + context.Shared.Stores.SteamStatusLabel).IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Epic_expiry_and_failed_signin_remain_truthful()
    {
        var connections = new Connections { Epic = new(false, "Test player"), FailSignIn = true };
        using var context = Context(connections);
        using var page = new FullscreenPlatformPage(context, "Epic");
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            AssertText(page, "SESSION EXPIRED");
            AssertText(page, "Test player");
            Click(FindButton(page, "Sign in again")); Dispatcher.UIThread.RunJobs();
            AssertText(page, "SESSION EXPIRED");
            AssertText(page, "Sign-in cancelled. Nothing was changed.");
            Assert.True(FindButton(page, "Sign in again").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Platform_reads_existing_sessions_and_updates_after_epic_signout_and_signin()
    {
        var connections = new Connections { Epic = new(true, "Test player") };
        using var context = Context(connections);
        using var page = new FullscreenPlatformPage(context, "Epic");
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        FullscreenPage? confirmation = null;
        var backs = 0;
        context.PageRequested += value => { confirmation = value; window.Content = value; };
        context.BackRequested += () => { backs++; window.Content = page; };
        window.Show();
        try
        {
            await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            AssertText(page, "SIGNED IN"); AssertText(page, "Test player");
            Click(FindButton(page, "Sign out of Epic"));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(confirmation);
            Click(FindButton(confirmation, "Sign out")); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, backs); // Only dismiss the confirmation; keep the platform screen.
            AssertText(page, "NOT SIGNED IN");
            Assert.False(FindButton(page, "Sign out of Epic", visible: false).IsVisible);
            Click(FindButton(page, "Sign in to Epic")); Dispatcher.UIThread.RunJobs();
            AssertText(page, "SIGNED IN"); AssertText(page, "New test player");
            Assert.False(FindButton(page, "Sign in to Epic", visible: false).IsVisible);
            Assert.True(FindButton(page, "Sign out of Epic").IsVisible);
        }
        finally { confirmation?.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Steam_renders_health_identity_and_actions_as_shared_state_changes()
    {
        using var context = Context(new Connections());
        using var page = new FullscreenPlatformPage(context, "Steam");
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        window.Show();
        try
        {
            await page.PendingRefresh;
            var stores = context.Shared.Stores;
            stores.SteamSignInAvailable = true;
            stores.SteamCredentials = new(false, false, true, true, DateTimeOffset.UtcNow.AddHours(1), "76561198000000000");
            stores.SteamSessionState = SteamSessionHealth.Live;
            Dispatcher.UIThread.RunJobs();
            AssertText(page, stores.SteamStatusLabel); AssertText(page, stores.SteamSignedInAccountText);
            Assert.True(FindButton(page, "Sign out of Steam").IsVisible);
            Assert.False(FindButton(page, stores.SteamSignInButtonText, visible: false).IsVisible);
            stores.SteamSessionState = SteamSessionHealth.Expired;
            Dispatcher.UIThread.RunJobs();
            AssertText(page, stores.SteamStatusLabel); AssertText(page, stores.SteamSessionHealthMessage);
            Assert.True(FindButton(page, stores.SteamSignInButtonText).IsVisible);
            stores.SteamCredentials = SteamConnection.None;
            stores.SteamSessionState = SteamSessionHealth.NotSignedIn;
            Dispatcher.UIThread.RunJobs();
            Assert.False(FindButton(page, "Sign out of Steam", visible: false).IsVisible);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "76561198000000000");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Platform_tool_directional_focus_matches_the_vertical_layout()
    {
        using var context = Context(new Connections());
        using var consent = new FullscreenSteamConsentPage(context, () => { });
        using var key = new FullscreenSteamApiKeyPage(context);
        using var saved = new FullscreenSavedPagesPage(context, "Choose pages", new());
        var window = new Window { Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            foreach (var page in new FullscreenPage[] { consent, key, saved })
            {
                window.Content = page; Dispatcher.UIThread.RunJobs();
                var controls = page.GetVisualDescendants().OfType<Control>().Where(c => c is Button or TextBox).ToArray();
                for (var i = 0; i < controls.Length - 1; i++)
                {
                    controls[i].Focus();
                    page.Handle(GamepadButtons.Down);
                    Assert.Same(controls[i + 1], window.FocusManager!.GetFocusedElement());
                    Assert.True(controls[i].TranslatePoint(default, page)!.Value.Y < controls[i + 1].TranslatePoint(default, page)!.Value.Y);
                    page.Handle(GamepadButtons.Up);
                    Assert.Same(controls[i], window.FocusManager.GetFocusedElement());
                    page.Handle(GamepadButtons.Right);
                    Assert.Same(controls[i], window.FocusManager.GetFocusedElement());
                }
            }
        }
        finally { window.Close(); }
    }

    private static FullscreenContext Context(Connections connections)
    {
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(), new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        var feed = new FeedViewModel(new PreviewFeedService(), library);
        var preview = PreviewData.Shell;
        var shell = new MainWindowViewModel(library, preview.MergeQueue, new StoresViewModel(connections), preview.Appearance, feed, preview.AccountStats, preview.LibrarySettings);
        return new(library, feed, shell);
    }
    private static Button FindButton(Control page, string content, bool visible = true)
        => page.GetVisualDescendants().OfType<Button>().Single(b =>
            (Equals(b.Content, content) || Avalonia.Automation.AutomationProperties.GetName(b) == content)
            && (!visible || b.IsEffectivelyVisible));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static void AssertText(Control page, string content)
        => Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == content);

    private sealed class Connections : IStoreConnections
    {
        public StoreSession? Epic { get; set; }
        public bool FailSignIn { get; set; }
        public ValueTask<bool> IsSteamWebApiConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(false);
        public ValueTask<SteamConnection> GetSteamConnectionAsync(CancellationToken ct = default) => ValueTask.FromResult(SteamConnection.None);
        public Task<SteamApiKeySaveOutcome> SaveSteamApiKeyAsync(string? key, CancellationToken ct = default) => Task.FromResult(SteamApiKeySaveOutcome.Stored);
        public Task ClearSteamApiKeyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask<StoreSession?> GetEpicSessionAsync(CancellationToken ct = default) => ValueTask.FromResult(Epic);
        public Task<StoreSignInOutcome> SignInToEpicAsync(CancellationToken ct = default)
        {
            if (FailSignIn) return Task.FromResult(new StoreSignInOutcome(false, null, false, StoreSignInProblem.Cancelled, StoreSignInMessages.Cancelled));
            Epic = new(true, "New test player");
            return Task.FromResult(new StoreSignInOutcome(true, Epic.DisplayName, true, StoreSignInProblem.None, ""));
        }
        public Task SignOutOfEpicAsync(CancellationToken ct = default) { Epic = null; return Task.CompletedTask; }
    }
}
