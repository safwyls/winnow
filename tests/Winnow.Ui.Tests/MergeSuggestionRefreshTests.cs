using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class MergeSuggestionRefreshTests
{
    [AvaloniaFact]
    public async Task Desktop_pointer_and_keyboard_refresh_report_busy_failure_and_recovery_within_minimum_pane()
    {
        using var fixture = new Fixture();
        using var model = ActivatorUtilities.CreateInstance<MergeQueueViewModel>(fixture.Services);
        await model.EnsureLoadedAsync();
        await fixture.AddPairAsync();
        var view = new MergeQueueView { DataContext = model };
        // The 1200px desktop window reserves 250px for its rail and outer margins.
        var window = new Window { Width = 950, Height = 740, Content = view };
        try
        {
            window.Show();
            var refresh = view.FindControl<Button>("RefreshSuggestionsButton")!;
            var status = view.FindControl<TextBlock>("SuggestionRefreshStatus")!;
            var mergeSelected = view.GetVisualDescendants().OfType<Button>().Single(button => button.Command == model.MergeSelectedCommand);
            Assert.Equal(MergeCopy.RefreshSuggestions, AutomationProperties.GetName(refresh));
            Assert.Equal(MergeCopy.RefreshSuggestions, ToolTip.GetTip(refresh));
            Assert.Empty(refresh.GetVisualDescendants().OfType<TextBlock>());
            Assert.Single(refresh.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
            Assert.False(status.IsEffectivelyVisible);
            AssertFits(refresh, view);
            AssertFits(mergeSelected, view);
            Assert.Equal(refresh.Bounds.Center.Y, mergeSelected.Bounds.Center.Y);
            Assert.InRange(mergeSelected.Bounds.Left - refresh.Bounds.Right, 0, 12);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Capture(window, "merge-refresh-desktop-idle");
            Click(window, refresh);
            Assert.False(refresh.IsEffectivelyEnabled);
            Assert.Equal(MergeCopy.RefreshSuggestionsBusy, status.Text);
            Assert.True(status.IsEffectivelyVisible);
            Assert.Equal(1, fixture.Refresh.Calls);
            Click(window, refresh);
            Assert.Equal(1, fixture.Refresh.Calls);
            Capture(window, "merge-refresh-desktop-busy");

            fixture.Refresh.Fail();
            await model.RefreshSuggestionsCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(refresh.IsEffectivelyEnabled);
            Assert.Equal(MergeCopy.RefreshSuggestionsFailed, status.Text);
            AssertFits(status, view);
            AssertFits(refresh, view);
            Capture(window, "merge-refresh-desktop-error");

            Assert.True(refresh.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(2, fixture.Refresh.Calls);
            fixture.Refresh.Complete();
            await model.RefreshSuggestionsCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(refresh.IsEffectivelyEnabled);
            Assert.Equal(MergeCopy.RefreshSuggestionsCompleted, status.Text);
            Assert.Equal(1, model.PendingCount);
            Assert.Empty(await fixture.Services.GetRequiredService<IIdentityLinkRepository>().GetHistoryAsync());
            AssertFits(status, view);
            AssertFits(refresh, view);
            AssertFits(mergeSelected, view);
            Capture(window, "merge-refresh-desktop-complete");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_controller_and_keyboard_refresh_report_busy_failure_and_recovery()
    {
        using var fixture = new Fixture();
        using var desktop = ActivatorUtilities.CreateInstance<MergeQueueViewModel>(fixture.Services);
        desktop.IsPaneVisible = true;
        await desktop.EnsureLoadedAsync();
        using var library = fixture.CreateLibrary();
        var feed = new FeedViewModel(new PreviewFeedService(), library);
        var shell = new MainWindowViewModel(library, desktop, new StoresViewModel(new PreviewStoreConnections()),
            new AppearanceViewModel(new ThemeService()), feed, new AccountStatsViewModel(new PreviewAccountStatsRepository()), new LibrarySettingsViewModel());
        using var context = new FullscreenContext(library, feed, shell, fixture.Services);
        using var page = new FullscreenIdentityPage(context);
        var window = new Window { Width = 1280, Height = 720, Content = page };
        Button Refresh() => page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RefreshSuggestionsButton");
        TextBlock Status() => page.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "SuggestionRefreshStatus");
        try
        {
            window.Show();
            await Until(() => page.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "No possible matches to review."));
            await fixture.AddPairAsync();
            page.FocusInitial();
            page.Handle(GamepadButtons.Down);
            page.Handle(GamepadButtons.Down);
            page.Handle(GamepadButtons.Down);
            Assert.True(Refresh().IsKeyboardFocusWithin);
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Refresh().IsEffectivelyEnabled);
            Assert.Equal(MergeCopy.RefreshSuggestionsBusy, Status().Text);
            Assert.Equal(1, fixture.Refresh.Calls);
            Capture(window, "merge-refresh-fullscreen-busy");
            fixture.Refresh.Fail();
            await page.PendingAction;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Refresh().IsEffectivelyEnabled);
            Assert.Equal(MergeCopy.RefreshSuggestionsFailed, Status().Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Status()));
            AssertFits(Status(), page);
            Capture(window, "merge-refresh-fullscreen-error");

            Assert.True(Refresh().Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(2, fixture.Refresh.Calls);
            fixture.Refresh.Complete();
            await page.PendingAction;
            Dispatcher.UIThread.RunJobs();
            Assert.True(Refresh().IsEffectivelyEnabled);
            Assert.Equal(MergeCopy.RefreshSuggestionsCompleted, Status().Text);
            Assert.Equal(1, desktop.PendingCount);
            Assert.Contains(page.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button)?.Contains("Bastion", StringComparison.Ordinal) == true);
            Assert.Empty(await fixture.Services.GetRequiredService<IIdentityLinkRepository>().GetHistoryAsync());
            AssertFits(Status(), page);
            Capture(window, "merge-refresh-fullscreen-complete");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Automatic_publication_updates_fullscreen_when_pending_count_is_unchanged()
    {
        using var fixture = new Fixture();
        await fixture.AddPairAsync();
        using var library = fixture.CreateLibrary();
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, fixture.Services);
        using var page = new FullscreenIdentityPage(context);
        var window = new Window { Width = 1280, Height = 720, Content = page };
        bool Has(string title) => page.GetVisualDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button)?.Contains(title, StringComparison.Ordinal) == true);
        try
        {
            window.Show();
            await Until(() => Has("Bastion"));
            var candidates = fixture.Services.GetRequiredService<IMergeCandidateRepository>();
            var old = Assert.Single(await candidates.GetPendingAsync());
            await candidates.WithdrawPendingAsync(old.Id);
            await fixture.AddPairAsync("Hades");
            fixture.Refresh.Complete();
            await fixture.Refresh.RefreshAsync();
            await library.LoadCommand.ExecuteAsync(null);
            await Until(() => Has("Hades"));
            Assert.False(Has("Bastion"));
            Assert.Equal(1, await candidates.CountPendingAsync());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Leaving_fullscreen_cancels_manual_refresh_without_late_page_updates()
    {
        using var fixture = new Fixture();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, fixture.Services);
        using var page = new FullscreenIdentityPage(context);
        var window = new Window { Width = 1280, Height = 720, Content = page };
        try
        {
            window.Show();
            await Until(() => page.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "No possible matches to review."));
            Click(window, page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RefreshSuggestionsButton"));
            var content = page.Content;
            page.Dispose();
            await page.PendingAction;
            Assert.True(fixture.Refresh.LastToken.IsCancellationRequested);
            Assert.Same(content, page.Content);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertFits(Control child, Control container)
    {
        var origin = child.TranslatePoint(default, container)!.Value;
        Assert.True(origin.X >= -1 && origin.X + child.Bounds.Width <= container.Bounds.Width + 1);
        Assert.True(child.Bounds.Height >= child.DesiredSize.Height - child.Margin.Top - child.Margin.Bottom - 1);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition());
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        public RefreshStub Refresh { get; } = new();
        public ServiceProvider Services { get; }
        public Fixture()
        {
            Services = new ServiceCollection()
                .AddSingleton<IMergeCandidateRepository>(new MergeCandidateRepository(_db.Factory))
                .AddSingleton<IReleaseRepository>(new ReleaseRepository(_db.Factory))
                .AddSingleton<IWorkRepository>(new WorkRepository(_db.Factory))
                .AddSingleton<IIdentityLinkRepository>(new IdentityLinkRepository(_db.Factory))
                .AddSingleton<IOwnershipRepository>(new OwnershipRepository(_db.Factory))
                .AddSingleton<IExpansionRefusalRepository>(new ExpansionRefusalRepository(_db.Factory))
                .AddSingleton<ILibraryQueryRepository>(new LibraryQueryRepository(_db.Factory))
                .AddSingleton<IResolveStateRepository>(new ResolveStateRepository(_db.Factory))
                .AddSingleton<IMergeSuggestionRefresh>(Refresh)
                .AddSingleton<LibraryExpansionScan>().BuildServiceProvider();
        }
        public LibraryViewModel CreateLibrary() => new(Services.GetRequiredService<ILibraryQueryRepository>(),
            Services.GetRequiredService<IOwnershipRepository>(), Services.GetRequiredService<IReleaseRepository>(),
            Services.GetRequiredService<IWorkRepository>(), new UpdateEventRepository(_db.Factory));
        public async Task AddPairAsync(string title = "Bastion")
        {
            async Task<long> Add(string store)
            {
                var work = await Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = title, FirstReleaseYear = 2011, Publisher = "Supergiant Games" });
                var release = await Services.GetRequiredService<IReleaseRepository>().InsertAsync(new Release { WorkId = work, Name = title, Platform = "windows" });
                await Services.GetRequiredService<IOwnershipRepository>().UpsertAsync(new OwnershipUpsert(release, store, null, null, null, null));
                return release;
            }
            var left = await Add("steam");
            var right = await Add("psn");
            MatchSubject Subject(long id) => new() { ReleaseId = id, Title = title, ReleaseYear = 2011, Publisher = "Supergiant Games" };
            var score = new SoftMatcher().Score(Subject(left), Subject(right));
            await Services.GetRequiredService<IMergeCandidateRepository>().InsertAsync(new MergeCandidate
            {
                LeftReleaseId = left, RightReleaseId = right, Score = score.Score,
                SignalsJson = SoftMatchSignalsJson.Serialize(score), Status = MergeCandidateStatuses.Pending,
            });
            await Services.GetRequiredService<IResolveStateRepository>().SetLastSoftMatchSweepAsync(DateTimeOffset.UtcNow);
        }
        public void Dispose() { Services.Dispose(); _db.Dispose(); }
    }

    private sealed class RefreshStub : IMergeSuggestionRefresh
    {
        private TaskCompletionSource<SoftMatchSweepReport> _pending = NewCompletion();
        public int Calls { get; private set; }
        public long Revision { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public async Task<SoftMatchSweepReport> RefreshAsync(CancellationToken ct = default)
        {
            Calls++;
            LastToken = ct;
            var report = await _pending.Task.WaitAsync(ct);
            Revision++;
            return report;
        }
        public void Fail()
        {
            var pending = _pending;
            _pending = NewCompletion();
            pending.SetException(new InvalidOperationException("Test failure"));
        }
        public void Complete() => _pending.SetResult(SoftMatchSweepReport.Empty);
        private static TaskCompletionSource<SoftMatchSweepReport> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
