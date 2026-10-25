using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Library;
using Winnow.App.Api;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Backend;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class BackendLiveUpdatesTests
{
    [AvaloniaFact]
    public async Task FullscreenActivityJournalAndSummaryUseApiOnlyFrontendServices()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-live-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await using var backend = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await backend.StartAsync(timeout.Token);
            using var client = WinnowApiClient.Attach(directory);
            var game = await client.CreateManualGameAsync(new("Journal game"), timeout.Token);
            var sessionId = await backend.Services.GetRequiredService<ISessionRepository>().InsertAsync(new()
            {
                OwnershipId = game.OwnershipId, StartedAt = DateTime.UtcNow.AddMinutes(-20),
                EndedAt = DateTime.UtcNow, DurationSeconds = 1200, DetectionMethod = "manual"
            }, timeout.Token);
            using var services = Frontend(directory, client);
            Assert.Null(services.GetService<ISessionRepository>());
            Assert.Null(services.GetService<IActivityRepository>());
            Assert.Null(services.GetService<IAccountStatsRepository>());
            var shell = services.GetRequiredService<MainWindowViewModel>();
            using var context = FullscreenContext.Create(services, shell);
            await context.Library.RefreshCommittedAsync(timeout.Token);
            using var activity = new FullscreenActivityPage(context);
            var window = new Window { Width = 1920, Height = 1080, Content = activity };
            try
            {
                window.Show(); Dispatcher.UIThread.RunJobs();
                await activity.PendingRefresh;
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(activity.GetVisualDescendants().OfType<Button>(),
                    button => AutomationProperties.GetAutomationId(button) == $"activity-session-{sessionId}");
                SessionNote? saved = null;
                using var editor = new FullscreenSessionNotePage(context, sessionId, "Journal game", null, note => saved = note);
                window.Content = editor; Dispatcher.UIThread.RunJobs();
                Assert.Single(editor.GetVisualDescendants().OfType<TextBox>()).Text = "Saved through the API";
                editor.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Save"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await UntilAsync(() => saved is not null, timeout.Token);
                Assert.Equal("Saved through the API", (await new DetailsClient(client).GetJournalAsync(sessionId, timeout.Token)).Note);
                using var summary = new FullscreenLibrarySummaryPage(context);
                summary.Stats.IsSpending = true;
                window.Content = summary; Dispatcher.UIThread.RunJobs();
                await summary.PendingRefresh;
                Assert.Null(summary.Stats.SpendingProblem);
            }
            finally { window.Close(); await backend.StopAsync(CancellationToken.None); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaFact]
    public async Task IndependentDesktopAndFullscreenClientsRefreshCommittedEditsAndRetainTheirSearch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-live-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await using var backend = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await backend.StartAsync(timeout.Token);
            using var firstClient = WinnowApiClient.Attach(directory);
            using var secondClient = WinnowApiClient.Attach(directory);
            using var firstServices = Frontend(directory, firstClient);
            using var secondServices = Frontend(directory, secondClient);
            var desktop = firstServices.GetRequiredService<MainWindowViewModel>();
            var secondWindow = secondServices.GetRequiredService<MainWindowViewModel>();
            using var fullscreen = FullscreenContext.Create(secondServices, secondWindow);
            fullscreen.SetActive(true);
            var firstEvents = firstServices.GetServices<IHostedService>().OfType<BackendLiveUpdates>().Single();
            var secondEvents = secondServices.GetServices<IHostedService>().OfType<BackendLiveUpdates>().Single();
            await firstEvents.StartAsync(timeout.Token);
            await secondEvents.StartAsync(timeout.Token);
            try
            {
                desktop.Library.SearchText = "Alpha";
                fullscreen.Library.SearchText = "Beta";
                var alpha = await firstClient.CreateManualGameAsync(new("Alpha"), timeout.Token);
                var beta = await secondClient.CreateManualGameAsync(new("Beta"), timeout.Token);
                await UntilAsync(() => desktop.Library.AllTiles.Count == 2 && fullscreen.Library.AllTiles.Count == 2, timeout.Token);
                Assert.Equal("Alpha", desktop.Library.SearchText);
                Assert.Equal("Beta", fullscreen.Library.SearchText);

                await secondClient.SetHiddenAsync(new([alpha.WorkId], true), timeout.Token);
                await UntilAsync(() => desktop.Library.AllTiles.Count == 1 && fullscreen.Library.AllTiles.Count == 1, timeout.Token);
                Assert.Equal("Beta", Assert.Single(desktop.Library.AllTiles).Title);
                Assert.Equal("Beta", Assert.Single(fullscreen.Library.AllTiles).Title);

                await firstClient.UpdateManualGameAsync(beta.OwnershipId,
                    new("Gamma", ExpectedIgdbMappingRevision: beta.IgdbMappingRevision, ExpectedRevision: beta.Revision), timeout.Token);
                await UntilAsync(() => desktop.Library.AllTiles.Single().Title == "Gamma"
                    && fullscreen.Library.AllTiles.Single().Title == "Gamma", timeout.Token);
                Assert.Equal("Alpha", desktop.Library.SearchText);
                Assert.Equal("Beta", fullscreen.Library.SearchText);
            }
            finally
            {
                await firstEvents.StopAsync(CancellationToken.None);
                await secondEvents.StopAsync(CancellationToken.None);
                await backend.StopAsync(CancellationToken.None);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaFact]
    public async Task FrontendResynchronizesAfterBackendRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-live-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            using var client = WinnowApiClient.Attach(directory);
            using var services = Frontend(directory, client);
            var shell = services.GetRequiredService<MainWindowViewModel>();
            var events = services.GetServices<IHostedService>().OfType<BackendLiveUpdates>().Single();
            await using (var first = BackendApplication.Build(["--data-dir", directory, "--no-sync"]))
            {
                await first.StartAsync(timeout.Token);
                await events.StartAsync(timeout.Token);
                await client.CreateManualGameAsync(new("Before restart"), timeout.Token);
                await UntilAsync(() => shell.Library.AllTiles.Count == 1, timeout.Token);
                await first.StopAsync(timeout.Token);
            }
            await using var restarted = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            await restarted.StartAsync(timeout.Token);
            try
            {
                await client.CreateManualGameAsync(new("After restart"), timeout.Token);
                await UntilAsync(() => shell.Library.AllTiles.Count == 2, timeout.Token);
                Assert.Contains(shell.Library.AllTiles, x => x.Title == "Before restart");
                Assert.Contains(shell.Library.AllTiles, x => x.Title == "After restart");
            }
            finally
            {
                await events.StopAsync(CancellationToken.None);
                await restarted.StopAsync(CancellationToken.None);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ServiceProvider Frontend(string directory, WinnowApiClient api)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        FrontendServiceRegistration.AddWinnowFrontend(services, WinnowDataLocation.ResolveOverride(directory), api);
        return services.BuildServiceProvider();
    }

    private static async Task UntilAsync(Func<bool> complete, CancellationToken ct)
    {
        while (!complete())
        {
            ct.ThrowIfCancellationRequested();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, ct);
        }
    }
}
