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

    /// <summary>Rail selection: SurfaceRaised fill plus a 2px Volt left edge (§6).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The count as the rail renders it — Plex Mono, tabular, grouped.</summary>
    public string CountText => Count.ToString("N0");

    public double RowOpacity => Count == 0 ? 0.4 : 1.0;
}
