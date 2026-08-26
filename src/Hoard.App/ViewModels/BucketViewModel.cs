using CommunityToolkit.Mvvm.ComponentModel;

namespace Hoard.App.ViewModels;

/// <summary>
/// One rail bucket row. Zero-count buckets render at 40% opacity rather than
/// hiding, so the rail never reflows (§6). Only "Patched since" carries the
/// Flare pip — Flare marks unread updates and nothing else.
/// </summary>
public partial class BucketViewModel : ObservableObject
{
    public BucketViewModel(string key, string name, bool showsFlarePip = false)
    {
        Key = key;
        Name = name;
        ShowsFlarePip = showsFlarePip;
    }

    /// <summary>The derived-bucket query key (LibraryBuckets.*), or a stub key with no members.</summary>
    public string Key { get; }

    public string Name { get; }

    /// <summary>The rail renders bucket names in Display S, which is uppercase (§3).</summary>
    public string RailLabel => Name.ToUpperInvariant();

    public bool ShowsFlarePip { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowOpacity), nameof(CountText))]
    public partial int Count { get; set; }

    /// <summary>
    /// Rail selection: SurfaceRaised fill plus a 2px Volt left edge (§6).
    ///
    /// <para><b>The Volt edge means "this is where you are", and exactly one row
    /// in the rail ever carries it.</b> With a list open, that row is the list —
    /// so a bucket in force while a list is open is <see cref="IsRule"/>, not
    /// this. Two Volt edges at once is precisely what let a live list's poured-in
    /// rules read as rules the user had set by hand.</para>
    /// </summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// The bucket is cutting the grid, but it is not where you are: the open
    /// list is. Same SurfaceRaised fill as a selection, and a TextDim edge
    /// instead of a Volt one — chrome rather than choice, the same distinction
    /// the cut bar draws between a rule the list contributed and one the user
    /// added (see <see cref="Filters.FilterChipOrigin"/>).
    /// </summary>
    [ObservableProperty]
    public partial bool IsRule { get; set; }

    /// <summary>The count as the rail renders it — Plex Mono, tabular, grouped.</summary>
    public string CountText => Count.ToString("N0");

    public double RowOpacity => Count == 0 ? 0.4 : 1.0;
}
