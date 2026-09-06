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
    [NotifyPropertyChangedFor(nameof(RowOpacity), nameof(CountText), nameof(AutomationName))]
    public partial int Count { get; set; }

    /// <summary>
    /// What a screen reader is told about this row: the bucket, how many games
    /// are in it, and whether they carry unread updates. The row is a Button
    /// whose content is a Grid, and a ContentControl peer with no name of its
    /// own falls back to <c>Content?.ToString()</c> — so before this existed
    /// the rail announced "Avalonia.Controls.Grid" and nothing else. The count
    /// is in the string because the name is what a reader hears when it lands
    /// on the row, and the count is otherwise only in the TextBlocks inside
    /// it. See <see cref="UnreadCopy.RailBucket"/>.
    /// </summary>
    public string AutomationName => UnreadCopy.RailBucket(Name, CountText, ShowsFlarePip);

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
