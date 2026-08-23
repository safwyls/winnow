using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.App.Services;
using Hoard.Covers;

namespace Hoard.App.ViewModels;

/// <summary>
/// One cover tile. Dormancy renders as the two-layer cross-fade from
/// docs/spikes/avalonia-dormancy-rendering.md: a floor variant (saturation
/// 0.22, brightness 0.60) sits under the vivid art, whose opacity is
/// <see cref="DisplayAlpha"/> — the ramp value normally, 1.0 under the
/// pointer (the view animates the change over 140ms).
/// <para>Real cover art (M1) is exactly that swap and nothing more: when
/// <see cref="VividCover"/> arrives it paints over the procedural
/// <see cref="PlaceholderArt"/> layers, sharing the same
/// <see cref="DisplayAlpha"/> and the same 140ms transition. The placeholder
/// stays underneath as the fallback, so a missing or still-loading cover shows
/// the title on a Surface field (§7) rather than a hole or a spinner.</para>
/// </summary>
public partial class GameTileViewModel : ObservableObject
{
    private readonly ICoverCache? _covers;
    private bool _coverWanted;

    public GameTileViewModel(
        long ownershipId,
        string title,
        string store,
        string bucket,
        long playtimeMinutes,
        DateTime? lastPlayedUtc,
        DateTime nowUtc,
        bool hasUnread = false,
        CoverKey? coverKey = null,
        ICoverCache? covers = null)
    {
        CoverKey = coverKey;
        _covers = covers;
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

    /// <summary>Provider id this tile's art is fetched under; null when we know no id for it.</summary>
    public CoverKey? CoverKey { get; }

    /// <summary>Real vivid cover, decoded at display resolution. Null until it arrives.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(ShowPlaceholder))]
    public partial Bitmap? VividCover { get; set; }

    /// <summary>Real floor variant (sat 0.22 / bright 0.60), pre-computed by the cover cache.</summary>
    [ObservableProperty]
    public partial Bitmap? FloorCover { get; set; }

    public bool HasCover => VividCover is not null;

    /// <summary>Procedural art is the fallback: it paints whenever no cover is loaded.</summary>
    public bool ShowPlaceholder => VividCover is null;

    /// <summary>
    /// Called when the tile is realized. A memory hit applies synchronously so
    /// scrolling back never flashes the placeholder; anything else is handed to
    /// the cover cache and arrives later (§5.1 — art never blocks the UI).
    /// </summary>
    public void RequestCover(double displayWidthPixels)
    {
        _coverWanted = true;
        if (_covers is null || CoverKey is not { } key)
        {
            return;
        }

        if (_covers.TryGet(key, displayWidthPixels, out var cached))
        {
            Apply(cached);
            return;
        }

        _ = LoadCoverAsync(key, displayWidthPixels);
    }

    /// <summary>
    /// Called when the tile is recycled out of the visual tree. Dropping the
    /// references is what makes the cache's memory bound real: off-screen tiles
    /// keep nothing alive, so the LRU is the only owner of decoded pixels.
    /// </summary>
    public void ReleaseCover()
    {
        _coverWanted = false;
        VividCover = null;
        FloorCover = null;
    }

    private async Task LoadCoverAsync(CoverKey key, double displayWidthPixels)
    {
        var art = await _covers!.GetAsync(key, displayWidthPixels).ConfigureAwait(false);
        if (art is null)
        {
            return;
        }

        // Covers appear as they arrive; the tile is already on screen showing
        // its placeholder, so this is a repaint, not a load gate.
        Dispatcher.UIThread.Post(() =>
        {
            if (_coverWanted)
            {
                Apply(art);
            }
        });
    }

    private void Apply(CoverArt art)
    {
        FloorCover = art.Floor;
        VividCover = art.Vivid;
    }

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
