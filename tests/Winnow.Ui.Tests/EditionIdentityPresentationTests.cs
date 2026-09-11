using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class EditionIdentityPresentationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acquired_native_evidence_updates_library_queue_and_details_and_can_be_separated(bool fullscreen)
    {
        using var fixture = await EditionEvidenceFixture.CreateAsync();
        await fixture.Candidates.InsertAsync(new MergeCandidate
        { LeftReleaseId = fixture.Epic.ReleaseId, RightReleaseId = fixture.Steam.ReleaseId, Score = .9 });
        var sync = fixture.SyncService();
        Assert.Equal(1, await sync.SyncAsync());
        Assert.Equal(1, sync.LastResult.Eligible);
        Assert.Equal(0, await fixture.Candidates.CountPendingAsync());
        using var library = new LibraryViewModel(fixture.Queries, fixture.Ownerships, fixture.Releases, fixture.Works,
            new UpdateEventRepository(fixture.Db.Factory), identityLinks: fixture.Links);
        await library.LoadCommand.ExecuteAsync(null);
        var tile = Assert.Single(library.VisibleTiles);
        Assert.Equal(2, tile.ReleaseIds.Count());
        await library.OpenDetailsCommand.ExecuteAsync(tile);
        var details = Assert.IsType<GameDetailsViewModel>(library.Details);
        Assert.True(details.ShowCoverage);
        Assert.Equal(2, details.Coverage!.Rows.Count);
        var services = new ServiceCollection();
        services.AddSingleton<IMergeCandidateRepository>(fixture.Candidates);
        services.AddSingleton<IReleaseRepository>(fixture.Releases);
        services.AddSingleton<IWorkRepository>(fixture.Works);
        services.AddSingleton<IIdentityLinkRepository>(fixture.Links);
        services.AddSingleton<IOwnershipRepository>(fixture.Ownerships);
        services.AddSingleton<ILibraryQueryRepository>(fixture.Queries);
        services.AddSingleton<IExpansionRefusalRepository>(new ExpansionRefusalRepository(fixture.Db.Factory));
        services.AddSingleton(new LibraryExpansionScan(fixture.Releases, fixture.Links, new ExpansionRefusalRepository(fixture.Db.Factory)));
        services.AddSingleton(library);
        using var provider = services.BuildServiceProvider();
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell, provider);
        var window = new Window { Width = fullscreen ? 1920 : 1100, Height = fullscreen ? 1080 : 900 };
        var pages = new List<FullscreenPage>();
        context.PageRequested += page => { pages.Add(page); window.Content = page; Dispatcher.UIThread.RunJobs(); };
        try
        {
            if (fullscreen)
            {
                using var detailPage = new FullscreenDetailsPage(context, details);
                window.Content = detailPage; window.Show(); Dispatcher.UIThread.RunJobs();
                Click(Find(window, "Library", exact: true));
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Epic edition", StringComparison.Ordinal) == true);
                using var identityPage = new FullscreenIdentityPage(context);
                window.Content = identityPage;
                await WaitUntil(() => window.GetVisualDescendants().OfType<Button>().Any(button => button.Content?.ToString()?.Contains("entries ·", StringComparison.Ordinal) == true));
                Click(Find(window, "entries ·"));
                Click(Find(window, "Separate again", exact: true));
                await identityPage.PendingAction;
                await WaitUntil(() => library.VisibleTiles.Count == 2);
            }
            else
            {
                var detailView = new GameDetailsView { DataContext = details };
                window.Content = detailView; window.Show(); Dispatcher.UIThread.RunJobs();
                var tab = detailView.FindControl<TabItem>("LibraryTab")!;
                tab.IsSelected = true; Dispatcher.UIThread.RunJobs();
                Assert.Contains(detailView.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "Epic edition");
                using var queue = new MergeQueueViewModel(fixture.Candidates, fixture.Releases, fixture.Works, fixture.Links,
                    fixture.Ownerships, provider.GetRequiredService<LibraryExpansionScan>(), provider.GetRequiredService<IExpansionRefusalRepository>(), fixture.Queries);
                await queue.LoadCommand.ExecuteAsync(null);
                Assert.True(Assert.Single(queue.Sections.SelectMany(section => section.Cards)).IsResolved);
                window.Content = new MergeQueueView { DataContext = queue }; Dispatcher.UIThread.RunJobs();
                Click(Find(window, "Separate again", exact: true));
                await (queue.SeparateCommand.ExecutionTask ?? Task.CompletedTask);
                await WaitUntil(() => !queue.Sections.SelectMany(section => section.Cards).Any(card => card.IsResolved));
                await library.LoadCommand.ExecuteAsync(null);
            }
            Assert.Equal(2, library.VisibleTiles.Count);
            Assert.True((await fixture.Links.GetResolutionAsync()).SameGame.IsEmpty);
            Assert.Equal(0, await sync.SyncAsync());
            Assert.Equal(1, sync.LastResult.ProtectedOrChanged);
            await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);
            Assert.False(library.Details!.ShowCoverage);
            Assert.Equal(2, (await fixture.Releases.GetAllExternalIdsAsync()).Count);
        }
        finally { window.Close(); foreach (var page in pages) page.Dispose(); }
    }

    private static Button Find(Window window, string text, bool exact = false)
        => window.GetVisualDescendants().OfType<Button>().First(button => exact ? button.Content?.ToString() == text
            : button.Content?.ToString()?.Contains(text, StringComparison.Ordinal) == true);
    private static void Click(Button button)
    {
        Assert.True(button.Focus());
        var window = Assert.IsType<Window>(TopLevel.GetTopLevel(button));
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
    }
    private static async Task WaitUntil(Func<bool> predicate)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < limit) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.True(predicate());
    }
}
