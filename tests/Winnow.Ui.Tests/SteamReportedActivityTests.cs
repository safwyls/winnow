using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class SteamReportedActivityTests
{
    [AvaloniaFact]
    public async Task Scope_filters_other_accounts_hidden_games_and_covered_increases()
    {
        var repo = new Repository { Rows = [Row(1), Row(2, account: "other"), Row(3, ownership: 99), Row(4) with { UnexplainedMinutes = 0 }] };
        var settings = new Settings();
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "000123");
        using var model = new SteamReportedActivityViewModel(repo, new Dictionary<long, string> { [1] = "Dragonwilds" }, settings);
        await model.RefreshAsync();
        Assert.Equal("123", repo.Account);
        Assert.Equal([1L], repo.Scope);
        Assert.Single(model.Entries);
        Assert.Contains("31 min", model.Entries[0].Duration);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "456");
        repo.Fail = true;
        await model.RefreshAsync();
        Assert.Empty(model.Entries);
        Assert.Contains("Couldn't read", model.Status);
        repo.Fail = false;
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "");
        await model.RefreshAsync();
        Assert.Empty(model.Entries);
        Assert.Contains("Confirm your Steam account", model.Status);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_presentations_label_estimates_and_bounds_without_changing_sessions(bool fullscreen)
    {
        var repo = new Repository { Rows = [Row(1), Row(2) with { ComparisonUnavailable = true, UnexplainedMinutes = null }] };
        using var model = new SteamReportedActivityViewModel(repo, new Dictionary<long, string> { [1] = "Dragonwilds" });
        using var details = new GameDetailsViewModel(PreviewData.GameDetails.Tile, "Started", [], DateTime.UtcNow,
            sessions: [new Session { OwnershipId = 1, StartedAt = DateTime.UtcNow.AddHours(-2), DurationSeconds = 600, DetectionMethod = "process" }],
            steamActivity: model);
        var total = details.Tracker.TotalText;
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var shell = fullscreen ? new FullscreenView(context) : null;
        using var page = fullscreen ? new FullscreenSteamActivityPage(context, model) : null;
        Control content = fullscreen ? page! : new SteamReportedActivityView { DataContext = model };
        var window = new Window { Content = fullscreen ? shell : new Border { Padding = new Avalonia.Thickness(24), Child = content }, Width = fullscreen ? 1920 : 800, Height = fullscreen ? 1080 : 700 };
        window.Show();
        if (page is not null) context.Push(page);
        try
        {
            await model.RefreshAsync();
            if (page is not null) await page.PendingRefresh;
            Dispatcher.UIThread.RunJobs();
            var text = string.Join("\n", content.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("not exact sessions", text);
            Assert.Contains("not added to recorded-session totals", text);
            Assert.Contains("Observed between", text);
            Assert.Contains("About 31 min not matched", text);
            Assert.Contains("may overlap recorded sessions", text);
            Assert.Equal(total, details.Tracker.TotalText);
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } capture)
            {
                Directory.CreateDirectory(capture);
                window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(capture, fullscreen ? "steam-activity-fullscreen.png" : "steam-activity-desktop.png"));
            }
            if (fullscreen)
            {
                FullscreenPage? opened = null;
                context.PageRequested += value => opened = value;
                var row = content.GetVisualDescendants().OfType<Button>().First(b =>
                    b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("About 31") == true));
                Assert.True(row.Focus());
                page!.Handle(GamepadButtons.Accept);
                Assert.IsType<FullscreenDetailsReadingPage>(opened);
                opened!.Dispose();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Non_Steam_games_hide_the_section_on_both_surfaces_but_mixed_games_keep_it()
    {
        var now = DateTime.UtcNow;
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        foreach (var store in new[] { "epic", "gog", "mixed" })
        {
            var tile = store == "mixed" ? TileFixture.Tile(now,
                [TileEntry.For(1, 1, 1, "gog", 0, null), TileEntry.For(2, 2, 1, "steam", 0, null)], 1, LibraryBuckets.NeverPlayed)
                : TileFixture.Tile(now, store: store);
            using var details = new GameDetailsViewModel(tile, "Never played", [], now);
            var desktop = new GameDetailsView { DataContext = details };
            var window = new Window { Content = desktop, Width = 1200, Height = 850 };
            window.Show();
            try
            {
                details.SelectedTabIndex = 1; Dispatcher.UIThread.RunJobs();
                var section = Assert.Single(desktop.GetVisualDescendants().OfType<SteamReportedActivityView>());
                Assert.Equal(store == "mixed", section.IsEffectivelyVisible);
                using var history = new FullscreenDetailsHistoryPage(context, details.Tracker, details.SteamActivity, () => details.ShowSteamActivity);
                window.Content = history; Dispatcher.UIThread.RunJobs();
                Assert.Equal(store == "mixed", history.GetVisualDescendants().OfType<TextBlock>()
                    .Any(block => block.Text == SteamReportedActivityViewModel.Heading));
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public async Task Desktop_details_activity_hosts_the_separate_projection()
    {
        using var model = PreviewSteamReportedActivity.Create();
        using var details = new GameDetailsViewModel(TileFixture.Tile(DateTime.UtcNow), "Started", [], DateTime.UtcNow, steamActivity: model);
        var view = new GameDetailsView { DataContext = details };
        var window = new Window { Content = view, Width = 1200, Height = 850 };
        window.Show();
        try
        {
            details.SelectedTabIndex = 1;
            Dispatcher.UIThread.RunJobs();
            await model.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
            var steam = Assert.Single(view.GetVisualDescendants().OfType<SteamReportedActivityView>());
            Assert.Same(model, steam.DataContext);
            Assert.Contains(steam.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("About 31 min") == true);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Failure_retries_and_scope_changes_discard_pending_reads()
    {
        var repo = new Repository { Fail = true };
        using var model = new SteamReportedActivityViewModel(repo, new Dictionary<long, string> { [1] = "Dragonwilds" });
        await model.RefreshAsync();
        Assert.Contains("Couldn't read", model.Status);
        repo.Fail = false;
        repo.Rows = [Row(1)];
        await model.RefreshAsync();
        Assert.Single(model.Entries);
        repo.Pending = new TaskCompletionSource<IReadOnlyList<SteamReportedActivity>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = model.RefreshAsync();
        while (!repo.PendingStarted) await Task.Delay(1);
        model.UpdateScope(new Dictionary<long, string>());
        repo.Pending.SetResult([Row(1)]);
        await pending;
        Assert.Empty(model.Entries);
        Assert.False(model.IsLoading);
        Assert.Equal(SteamReportedActivityViewModel.EmptyMessage, model.Status);
    }

    [AvaloniaFact]
    public async Task Fullscreen_activity_and_per_game_history_offer_separate_entry_points()
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var activity = new FullscreenActivityPage(context);
        var window = new Window { Content = activity, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            await activity.PendingRefresh; Dispatcher.UIThread.RunJobs();
            FullscreenPage? opened = null;
            context.PageRequested += page => opened = page;
            var link = activity.GetVisualDescendants().OfType<Button>().Single(button =>
                button.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == SteamReportedActivityViewModel.Heading));
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsType<FullscreenSteamActivityPage>(opened); opened!.Dispose();
            using var model = new SteamReportedActivityViewModel();
            using var history = new FullscreenDetailsHistoryPage(context, PreviewData.GameDetails.Tracker, model);
            window.Content = history; Dispatcher.UIThread.RunJobs();
            Assert.Contains(history.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == SteamReportedActivityViewModel.Heading);
        }
        finally { window.Close(); }
    }

    private static SteamReportedActivity Row(long id, long ownership = 1, string account = "123") => new()
    {
        Id = id, OwnershipId = ownership, AccountRef = account,
        WindowStartedAt = new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc),
        WindowEndedAt = new DateTime(2026, 9, 14, 19, 0, 0, DateTimeKind.Utc),
        SteamDeltaMinutes = 31, UnexplainedMinutes = 31, MatchedRecordedMinutes = 0
    };
    private sealed class Repository : ISteamPlaytimeObservationRepository
    {
        public IReadOnlyList<SteamReportedActivity> Rows = [];
        public bool Fail;
        public string? Account;
        public long[] Scope = [];
        public TaskCompletionSource<IReadOnlyList<SteamReportedActivity>>? Pending;
        public volatile bool PendingStarted;
        public Task ObserveAsync(SteamPlaytimeObservation observation, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SteamPlaytimeObservation>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SteamPlaytimeObservation>>([]);
        public Task<IReadOnlyList<SteamReportedActivity>> GetActivityAsync(IReadOnlyCollection<long> ownershipIds, DateTime asOfUtc, string? accountRef = null, CancellationToken ct = default)
        {
            if (Fail) throw new IOException();
            Scope = ownershipIds.ToArray(); Account = accountRef;
            if (Pending is not null) { PendingStarted = true; return Pending.Task; }
            return Task.FromResult(Rows);
        }
    }
    private sealed class Settings : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default) { _values[key] = value; return Task.CompletedTask; }
    }
}
