using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ActivityPagingInteractionTests
{
    [AvaloniaFact]
    public async Task Week_navigation_discards_an_older_response_even_when_its_reader_ignores_cancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<(DateTime From, DateTime Until)>();
        var repository = new Reader(async (ids, from, until, _, _, ct) =>
        {
            Assert.False(Dispatcher.UIThread.CheckAccess());
            calls.Add((from, until));
            if (calls.Count == 1) { entered.SetResult(); await release.Task; }
            return Page(ids.First(), from, calls.Count == 1 ? 1 : 2);
        });
        using var services = new ServiceCollection().AddSingleton<IActivityRepository>(repository).BuildServiceProvider();
        using var context = await CreateContextAsync(services);
        using var page = new FullscreenActivityPage(context);
        var window = new Window { Width=1920, Height=1080, Content=page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Handle(GamepadButtons.Left); page.Handle(GamepadButtons.Left);
            release.SetResult();
            await page.PendingRefresh;
            Assert.Equal(2, page.WeekOffset);
            Assert.Equal(2, page.SelectedSessionId);
            Assert.Equal(2, calls.Count);
            Assert.Equal(calls[0].From.ToLocalTime().AddDays(-14), calls[1].From.ToLocalTime());
            Assert.Equal(7, (calls[1].Until.ToLocalTime() - calls[1].From.ToLocalTime()).TotalDays);
        }
        finally { release.TrySetResult(); window.Close(); await page.PendingRefresh; }
    }

    [AvaloniaFact]
    public async Task Load_more_is_explicit_and_disposal_cancels_a_pending_page_without_late_publication()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        var repository = new Reader(async (ids, from, _, _, after, ct) =>
        {
            if (after is null) return Page(ids.First(), from, 1) with { Next = new(from, 1) };
            observed = ct; entered.SetResult(); await release.Task;
            return Page(ids.First(), from, 2);
        });
        using var services = new ServiceCollection().AddSingleton<IActivityRepository>(repository).BuildServiceProvider();
        using var context = await CreateContextAsync(services);
        using var page = new FullscreenActivityPage(context);
        var window = new Window { Width=1920, Height=1080, Content=page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); await page.PendingRefresh;
            Assert.Equal(1, page.SelectedSessionId);
            page.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content,"Load more"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            Assert.True(observed.IsCancellationRequested);
            release.SetResult(); await page.PendingRefresh;
            Assert.Equal(1, page.SelectedSessionId);
        }
        finally { release.TrySetResult(); window.Close(); await page.PendingRefresh; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Slow_account_reads_leave_the_dispatcher_available_and_cancellation_does_not_publish(bool fullscreen)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var repository = new SlowStats(entered, release);
        using var services = new ServiceCollection().AddSingleton<IAccountStatsRepository>(repository).BuildServiceProvider();
        using var context = await CreateContextAsync(services);
        using var page = new FullscreenLibrarySummaryPage(context);
        var model = new AccountStatsViewModel(repository);
        var window = new Window { Width=1920, Height=1080, Content=fullscreen ? page : null };
        Task pending = Task.CompletedTask;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            if (!fullscreen) pending = model.RefreshCommand.ExecuteAsync(null);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(repository.OnUiThread);
            var moved = false;
            await Dispatcher.UIThread.InvokeAsync(() => moved = true);
            Assert.True(moved);
            if (fullscreen) page.Dispose(); else model.RefreshCommand.Cancel();
            release.Set();
            if (!fullscreen) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await repository.Returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(model.HasFacts);
        }
        finally { release.Set(); window.Close(); }
    }

    private static ActivityPage Page(long owner, DateTime at, long id) => new([
        new(owner, "steam", at, new Session { Id=id, OwnershipId=owner, StartedAt=at, DetectionMethod="manual" }, null, null)], null);
    private static async Task<FullscreenContext> CreateContextAsync(IServiceProvider services)
    {
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        await library.LoadCommand.ExecuteAsync(null);
        return new(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell, services);
    }
    private sealed class Reader(Func<IReadOnlyCollection<long>,DateTime,DateTime,ActivitySection,ActivityCursor?,CancellationToken,Task<ActivityPage>> read) : IActivityRepository
    {
        public Task<ActivityPage> GetPageAsync(IReadOnlyCollection<long> ownershipIds, DateTime fromUtc, DateTime untilUtc,
            ActivitySection section, ActivityCursor? after=null, int pageSize=50, CancellationToken ct=default)
            => read(ownershipIds,fromUtc,untilUtc,section,after,ct);
    }
    private sealed class SlowStats(TaskCompletionSource entered, ManualResetEventSlim release) : IAccountStatsRepository
    {
        public bool OnUiThread { get; private set; }
        public TaskCompletionSource Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AccountStats> GetAsync(string source, CancellationToken ct=default)
        {
            OnUiThread=Dispatcher.UIThread.CheckAccess(); entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10));
            Returned.TrySetResult();
            return Task.FromResult(new AccountStats { Source=source, TransactionCount=1 });
        }
    }
}
