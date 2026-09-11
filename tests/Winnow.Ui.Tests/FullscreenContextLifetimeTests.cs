using Avalonia.Headless.XUnit;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenContextLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Disposal_releases_each_independent_presentation_model_and_preserves_borrowed_shared_models(
        bool shareLibrary, bool shareFeed)
    {
        using var sharedLibrary = Library();
        using var sharedFeed = new FeedViewModel(new PreviewFeedService(), sharedLibrary);
        var releases = new PreviewReleaseRepository();
        var links = new PreviewIdentityLinkRepository();
        var refusals = new PreviewExpansionRefusalRepository();
        using var merges = new MergeQueueViewModel(new PreviewMergeCandidateRepository(), releases, new PreviewWorkRepository(),
            links, new PreviewOwnershipRepository(), new LibraryExpansionScan(releases, links, refusals), refusals, new PreviewLibraryQueryRepository());
        var shell = new MainWindowViewModel(sharedLibrary, merges, new StoresViewModel(new PreviewStoreConnections()),
            new AppearanceViewModel(new ThemeService()), sharedFeed, new AccountStatsViewModel(new PreviewAccountStatsRepository()), new LibrarySettingsViewModel());
        await sharedLibrary.LoadCommand.ExecuteAsync(null);
        await sharedFeed.LoadCommand.ExecuteAsync(null);
        Assert.NotEmpty(sharedFeed.Shelves);
        using var library = shareLibrary ? sharedLibrary : Library();
        using var feed = shareFeed ? sharedFeed : new FeedViewModel(new PreviewFeedService(), library);
        await library.LoadCommand.ExecuteAsync(null);
        await feed.LoadCommand.ExecuteAsync(null);
        Assert.NotEmpty(feed.Shelves);
        using var context = new FullscreenContext(library, feed, shell);
        context.Dispose();

        Assert.NotEmpty(sharedFeed.Shelves);
        if (!shareFeed) Assert.Empty(feed.Shelves);
        var sharedReloads = 0;
        sharedLibrary.TilesChanged += (_, _) => sharedReloads++;
        await sharedLibrary.LoadCommand.ExecuteAsync(null);
        await (sharedFeed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        Assert.Equal(1, sharedReloads);
        Assert.NotEmpty(sharedFeed.Shelves);
        if (!shareFeed) Assert.Empty(feed.Shelves);
        if (!shareLibrary)
        {
            var independentReloads = 0;
            library.TilesChanged += (_, _) => independentReloads++;
            await library.LoadCommand.ExecuteAsync(null);
            Assert.Equal(0, independentReloads);
        }
    }

    private static LibraryViewModel Library() => new(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
        new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
}
