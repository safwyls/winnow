using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Winnow.App.ViewModels;
using Winnow.App.Services;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class StartupReadThreadTests
{
    [AvaloniaFact]
    public async Task Library_and_startup_models_read_off_UI_thread_and_publish_on_UI_thread()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 20);
        var factory = new LibraryReadTrackingFactory(db.Factory)
        {
            BeforeLease = () => Assert.False(Dispatcher.UIThread.CheckAccess()),
        };
        var queries = new LibraryQueryRepository(factory);
        var works = new WorkRepository(factory);
        var releases = new ReleaseRepository(factory);
        var ownerships = new OwnershipRepository(factory);
        var links = new IdentityLinkRepository(factory);
        var lists = new GameListRepository(factory);
        var library = new LibraryViewModel(queries, ownerships, releases, works,
            new UpdateEventRepository(factory), lists: lists);
        library.PropertyChanged += (_, _) => Assert.True(Dispatcher.UIThread.CheckAccess());
        await library.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, factory.Leases);
        Assert.Equal(20, library.TotalCount);
        Assert.Equal([20L, 1L], Assert.Single(library.Lists.Lists).ReleaseIds);

        var refusals = new ExpansionRefusalRepository(factory);
        using var queue = new MergeQueueViewModel(new MergeCandidateRepository(factory), releases, works,
            links, ownerships, new LibraryExpansionScan(releases, links, refusals), refusals, queries,
            resolveState: new ResolveStateRepository(factory));
        queue.PropertyChanged += (_, _) => Assert.True(Dispatcher.UIThread.CheckAccess());
        await queue.LoadCommand.ExecuteAsync(null);

        var display = new DisplaySettingsViewModel(new DormancyRamp(), new SettingsRepository(factory), libraryQueries: queries);
        display.PropertyChanged += (_, _) => Assert.True(Dispatcher.UIThread.CheckAccess());
        await display.LoadAsync();
        var settings = new LibrarySettingsViewModel(new HiddenGameRepository(factory),
            new ManualEntryRepository(factory), queries, new SettingsRepository(factory));
        settings.PropertyChanged += (_, _) => Assert.True(Dispatcher.UIThread.CheckAccess());
        await settings.RefreshAsync();
    }
}
