using System.Collections;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class LibraryPreparationResponsivenessTests
{
    [AvaloniaTheory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("replace")]
    public async Task Input_runs_during_preparation_without_publishing_partial_or_stale_tiles(string action)
    {
        using var database = new TempDatabase();
        LibraryReadFixtures.Seed(database, 512);
        var queries = new ObservedQueries(new LibraryQueryRepository(database.Factory));
        using var library = new LibraryViewModel(queries, new OwnershipRepository(database.Factory),
            new ReleaseRepository(database.Factory), new WorkRepository(database.Factory), new UpdateEventRepository(database.Factory));
        var published = 0;
        var countAtInput = -1;
        Task? replacement = null;
        library.TilesChanged += (_, _) => published++;
        queries.Preparing = () =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            Dispatcher.UIThread.Post(() =>
            {
                countAtInput = library.AllTiles.Count;
                if (action == "cancel") library.LoadCommand.Cancel();
                else if (action == "dispose") library.Dispose();
                else if (action == "replace")
                {
                    queries.OnlyFirst = true;
                    replacement = library.RefreshCommittedAsync(CancellationToken.None);
                }
            }, DispatcherPriority.Input);
        };
        await library.LoadCommand.ExecuteAsync(null);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        if (replacement is not null) await replacement;
        Assert.Equal(0, countAtInput);
        var expected = action switch { "complete" => 512, "replace" => 1, _ => 0 };
        Assert.Equal(expected, library.AllTiles.Count);
        Assert.Equal(expected, library.VisibleTiles.Count);
        Assert.Equal(expected, library.TotalCount);
        Assert.Equal(expected > 0 ? 1 : 0, published);
    }

    private sealed class ObservedQueries(ILibraryQueryRepository inner) : ILibraryQueryRepository
    {
        public Action? Preparing { get; set; }
        public bool OnlyFirst { get; set; }
        public async Task<LibrarySnapshot> GetSnapshotAsync(BucketThresholds thresholds, CancellationToken ct = default)
        {
            var snapshot = await inner.GetSnapshotAsync(thresholds, ct);
            if (OnlyFirst) return snapshot with { Buckets = snapshot.Buckets.Take(1).ToArray() };
            var callback = Preparing;
            Preparing = null;
            return snapshot with { Works = new ObservedList<Work>(snapshot.Works, callback) };
        }
        public Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(BucketThresholds thresholds, CancellationToken ct = default) => inner.GetOwnershipBucketsAsync(thresholds, ct);
        public Task<int> CountHiddenByAccountScopeAsync(BucketThresholds thresholds, CancellationToken ct = default) => inner.CountHiddenByAccountScopeAsync(thresholds, ct);
        public Task<int> CountHiddenByExplicitFilterAsync(BucketThresholds thresholds, CancellationToken ct = default) => inner.CountHiddenByExplicitFilterAsync(thresholds, ct);
        public Task<int> CountHiddenByRatingCapAsync(BucketThresholds thresholds, CancellationToken ct = default) => inner.CountHiddenByRatingCapAsync(thresholds, ct);
        public Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default) => inner.GetFacetTargetsAsync(ct);
    }

    private sealed class ObservedList<T>(IReadOnlyList<T> items, Action? firstEnumeration) : IReadOnlyList<T>
    {
        private Action? _firstEnumeration = firstEnumeration;
        public int Count => items.Count;
        public T this[int index] => items[index];
        public IEnumerator<T> GetEnumerator()
        {
            Interlocked.Exchange(ref _firstEnumeration, null)?.Invoke();
            return items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
