using CommunityToolkit.Mvvm.ComponentModel;

namespace Winnow.App.ViewModels;

/// <summary>
/// One rail bucket row. Zero-count buckets render at reduced opacity rather than hiding.
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

    public string RailLabel => Name.ToUpperInvariant();

    public bool ShowsFlarePip { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowOpacity), nameof(CountText))]
    public partial int Count { get; set; }

    /// <summary>Whether this bucket is the active selection in the rail (Volt edge).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The bucket is filtering the grid but the open list owns the selection.</summary>
    [ObservableProperty]
    public partial bool IsRule { get; set; }

    /// <summary>Formatted count for display.</summary>
    public string CountText => Count.ToString("N0");

    public double RowOpacity => Count == 0 ? 0.4 : 1.0;
}
