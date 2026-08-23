using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.App.Services;

namespace Hoard.App.ViewModels;

/// <summary>
/// One cover tile. Dormancy renders as the two-layer cross-fade from
/// docs/spikes/avalonia-dormancy-rendering.md: a floor variant (saturation
/// 0.22, brightness 0.60) sits under the vivid art, whose opacity is
/// <see cref="DisplayAlpha"/> — the ramp value normally, 1.0 under the
/// pointer (the view animates the change over 140ms). M0 art is procedural
/// (<see cref="PlaceholderArt"/>); when real covers land, the two brushes
/// become two decoded bitmaps and nothing else changes.
/// </summary>
public partial class GameTileViewModel : ObservableObject
{
    public GameTileViewModel(
        long ownershipId,
        string title,
        string store,
        string bucket,
        long playtimeMinutes,
        DateTime? lastPlayedUtc,
        DateTime nowUtc,
        bool hasUnread = false)
    {
        OwnershipId = ownershipId;
        Title = title;
        StoreBadge = store.ToUpperInvariant();
        Bucket = bucket;
        PlaytimeMinutes = playtimeMinutes;
        LastPlayedUtc = lastPlayedUtc;
        HasUnread = hasUnread;

        DormancyAlpha = Dormancy.VividAlphaFor(lastPlayedUtc, nowUtc);
        StatText = BuildStatText(playtimeMinutes, lastPlayedUtc, nowUtc);

        var (start, end) = PlaceholderArt.VividColors(title);
        VividBrush = PlaceholderArt.Gradient(start, end);
        FloorBrush = PlaceholderArt.Gradient(PlaceholderArt.ToFloor(start), PlaceholderArt.ToFloor(end));
        FloorTitleBrush = new ImmutableSolidColorBrush(PlaceholderArt.ToFloor(Colors.White));
    }

    public long OwnershipId { get; }

    public string Title { get; }

    public string StoreBadge { get; }

    /// <summary>Derived-bucket key (LibraryBuckets.*), used for rail filtering.</summary>
    public string Bucket { get; }

    public long PlaytimeMinutes { get; }

    public DateTime? LastPlayedUtc { get; }

    /// <summary>Unread-update badge (§5.2) — set from stale-but-patched bucket membership.</summary>
    public bool HasUnread { get; }

    /// <summary>Scrim line: "312h · idle 8mo", or "never opened".</summary>
    public string StatText { get; }

    /// <summary>Resting vivid-layer opacity from the §5.1 ramp: α = (S − 0.22) / 0.78.</summary>
    public double DormancyAlpha { get; }

    /// <summary>Vivid art layer. Placeholder gradient now; display-resolution bitmap later.</summary>
    public IBrush VividBrush { get; }

    /// <summary>Floor variant (sat 0.22 / bright 0.60). Pre-computed bitmap variant later.</summary>
    public IBrush FloorBrush { get; }

    /// <summary>Placeholder-title ink on the floor layer, so the title fades with its art.</summary>
    public IBrush FloorTitleBrush { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayAlpha))]
    public partial bool IsPointerOver { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Hover restores full saturation (140ms transition lives in the view).</summary>
    public double DisplayAlpha => IsPointerOver ? 1.0 : DormancyAlpha;

    private static string BuildStatText(long playtimeMinutes, DateTime? lastPlayedUtc, DateTime nowUtc)
    {
        if (playtimeMinutes <= 0)
        {
            return "never opened";
        }

        var playtime = playtimeMinutes < 60
            ? $"{playtimeMinutes}m"
            : $"{playtimeMinutes / 60}h";

        return lastPlayedUtc is null
            ? playtime
            : $"{playtime} · idle {IdleText(nowUtc - lastPlayedUtc.Value)}";
    }

    private static string IdleText(TimeSpan idle)
    {
        var days = Math.Max(0, idle.TotalDays);
        if (days < 30)
        {
            return $"{Math.Max(1, (int)days)}d";
        }

        var months = (int)(days / 30.4375);
        if (months < 12)
        {
            return $"{months}mo";
        }

        var years = months / 12;
        var rest = months % 12;
        return rest == 0 ? $"{years}y" : $"{years}y {rest}mo";
    }
}
