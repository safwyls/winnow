using Winnow.App.ViewModels.Filters;

namespace Winnow.App.ViewModels.Fullscreen;

/// <summary>Edits stay local until Apply; abandoning a page cannot change a collection.</summary>
public sealed class FullscreenBrowseFilterDraft
{
    public FilterPanelViewModel Filters { get; }
    public LibrarySort Sort { get; set; }
    public BucketViewModel? Bucket { get; set; }

    public FullscreenBrowseFilterDraft(LibraryViewModel library)
    {
        Filters = new FilterPanelViewModel(() => { });
        foreach (var group in Filters.Groups)
        {
            var original = library.Filters.Groups.First(g => g.Key == group.Key);
            group.SetOptions(original.AllOptions.Select(option => (option.Key, option.Label)));
        }
        Filters.Apply(library.Filters.ToFilter());
        Sort = library.Sort;
        Bucket = library.SelectedBucket;
    }

    public void Apply(LibraryViewModel library)
    {
        library.Filters.Apply(Filters.ToFilter());
        library.Sort = Sort;
        library.SelectedBucket = Bucket;
    }
}
