using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Winnow.Ui.Tests;

/// <summary>Reproducible latency evidence; correctness budgets use read shape, not machine-speed assertions.</summary>
public sealed class LargeHistoryResponsivenessTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Measure_details_activity_and_account_summary(bool fullscreen)
    {
        using var db = new TempDatabase();
        Seed(db);
        var tracking = new LibraryReadTrackingFactory(db.Factory);
        var uiReads = 0;
        Stopwatch? activeMeasurement = null;
        var exceeded = false;
        tracking.BeforeLease = () =>
        {
            if (Dispatcher.UIThread.CheckAccess()) Interlocked.Increment(ref uiReads);
            if (activeMeasurement?.Elapsed > TimeSpan.FromSeconds(10))
            {
                exceeded = true;
                throw new TimeoutException("Synthetic measurement exceeded its ten-second read budget.");
            }
        };
        var owners = new OwnershipRepository(tracking);
        var sessions = new SessionRepository(tracking);
        var updates = new UpdateEventRepository(tracking);
        using var library = new LibraryViewModel(new LibraryQueryRepository(tracking), owners,
            new ReleaseRepository(tracking), new WorkRepository(tracking), updates,
            snapshots: new PlaytimeSnapshotRepository(tracking), sessions: sessions);
        await library.LoadCommand.ExecuteAsync(null);
        using var services = new ServiceCollection().AddSingleton<IOwnershipRepository>(owners)
            .AddSingleton<ISessionRepository>(sessions).AddSingleton<IUpdateEventRepository>(updates)
            .AddSingleton<IActivityRepository>(new ActivityRepository(tracking))
            .AddSingleton<IAccountStatsRepository>(new AccountStatsRepository(tracking)).BuildServiceProvider();
        using var feed = new FeedViewModel(new PreviewFeedService(), library);
        using var context = new FullscreenContext(library, feed, PreviewData.Shell, services);
        var window = new Window { Width = 1920, Height = 1080 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await MeasureAsync("details", () => library.OpenDetailsCommand.ExecuteAsync(library.AllTiles.Single(t => t.OwnershipId == 1)));
            Assert.NotNull(library.Details);
            window.Content = fullscreen ? new FullscreenDetailsPage(context, library.Details) : new GameDetailsView { DataContext = library.Details };
            Dispatcher.UIThread.RunJobs();
            if (fullscreen)
            {
                using var activity = new FullscreenActivityPage(context);
                await MeasureAsync("weekly activity", () => { window.Content = activity; Dispatcher.UIThread.RunJobs(); return activity.PendingRefresh; });
            }
            var stats = new AccountStatsViewModel(new AccountStatsRepository(tracking));
            await MeasureAsync("account summary", () => stats.RefreshCommand.ExecuteAsync(null));
            Assert.True(stats.HasFacts);
        }
        finally { window.Close(); }

        async Task MeasureAsync(string operation, Func<Task> invoke)
        {
            var before = tracking.Leases; uiReads = 0; exceeded = false;
            var watch = Stopwatch.StartNew();
            activeMeasurement = watch;
            var lastTick = 0d; var maxUiGap = 0d;
            var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(1) };
            timer.Tick += (_, _) => { var now = watch.Elapsed.TotalMilliseconds; maxUiGap = Math.Max(maxUiGap, now - lastTick); lastTick = now; };
            timer.Start();
            var task = invoke();
            var synchronous = watch.Elapsed.TotalMilliseconds;
            await task;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            timer.Stop();
            activeMeasurement = null;
            output.WriteLine($"{(fullscreen ? "fullscreen" : "desktop")} {operation}: total_ms={watch.Elapsed.TotalMilliseconds:F2}; invoke_ms={synchronous:F2}; max_ui_gap_ms={maxUiGap:F2}; repository_leases={tracking.Leases - before}; ui_leases={uiReads}; exceeded={exceeded}");
            Assert.Equal(0, uiReads);
            Assert.False(exceeded);
        }
    }

    private static void Seed(TempDatabase db)
    {
        LibraryReadFixtures.Seed(db, 2_000);
        using var connection = db.Factory.Open();
        connection.Execute("""
            WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<60)
            INSERT INTO sessions(ownership_id,started_at,ended_at,duration_s,detection_method)
            SELECT o.id,datetime('now','-'||(seq.n*10)||' days'),datetime('now','-'||(seq.n*10)||' days','+1 hour'),3600,'process_watch'
            FROM ownerships o CROSS JOIN seq;
            WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<5000)
            INSERT INTO sessions(ownership_id,started_at,ended_at,duration_s,detection_method)
            SELECT 1,datetime('now','-'||n||' days'),datetime('now','-'||n||' days','+1 hour'),3600,'process_watch' FROM seq;
            INSERT INTO session_notes(session_id,note,rating) SELECT id,'A preserved journal entry',4 FROM sessions;
            WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<14600)
            INSERT INTO playtime_snapshots(ownership_id,playtime_minutes,observed_at)
            SELECT 1,n*10,datetime('now','-'||((14600-n)*6)||' hours') FROM seq;
            INSERT INTO update_events(release_id,kind,occurred_at,title)
            SELECT id,'announcement',datetime('now','-30 days'),'Older update' FROM releases;
            WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n<40000)
            INSERT INTO account_transactions(source,transaction_type_raw,occurred_at,kind,refunded,total_cents,currency_symbol,item_names_json,item_count,captured_at)
            SELECT 'steam','Purchase',datetime('now','-'||n||' hours'),'purchase',0,1000,'$','["Game"]',1,datetime('now') FROM seq;
            """, commandTimeout: 60);
    }
}
