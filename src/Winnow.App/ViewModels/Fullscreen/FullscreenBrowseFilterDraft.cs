using Winnow.App.ViewModels.Filters;

namespace Winnow.App.ViewModels.Fullscreen;

/// <summary>Edits stay local until Apply; abandoning a page cannot change a collection.</summary>
public sealed class FullscreenBrowseFilterDraft
{
    private readonly LibraryViewModel _library;
    public FilterPanelViewModel Filters { get; }
    public LibrarySort Sort { get; set; }
    public BucketViewModel? Bucket { get; set; }

    public FullscreenBrowseFilterDraft(LibraryViewModel library)
    {
        _library = library;
        Filters = new FilterPanelViewModel(Recount);
        Sort = library.Sort;
        Bucket = library.SelectedBucket;
        foreach (var group in Filters.Groups)
        {
            var original = library.Filters.Groups.First(g => g.Key == group.Key);
            group.SetOptions(original.AllOptions.Select(option => (option.Key, option.Label)));
        }
        Filters.Apply(library.Filters.ToFilter());
    }

    public void Recount() => Filters.Recount(filter =>
    {
        var scoped = filter with { Search = _library.SearchText, Buckets = Bucket is { } bucket ? [bucket.Key] : [] };
        var list = _library.Lists.Open;
        return _library.AllTiles.Where(tile => scoped.Matches(tile.Row) &&
            (list is not { IsManual: true } || list.ReleaseIds.Any(tile.CoversRelease))).ToArray();
    });

    public bool Apply(LibraryViewModel library)
    {
        if (Filters.HasYearProblem) return false;
        library.Filters.Apply(Filters.ToFilter());
        library.Sort = Sort;
        library.SelectedBucket = Bucket;
        return true;
    }
}
