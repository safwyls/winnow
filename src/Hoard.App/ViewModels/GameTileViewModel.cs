using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.App.Services;
using Hoard.Core.Domain;
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
        long releaseId,
        string title,
        string store,
        string bucket,
        long playtimeMinutes,
        DateTime? lastPlayedUtc,
        DateTime nowUtc,
        bool hasUnread = false,
        CoverKey? coverKey = null,
        ICoverCache? covers = null,
        Work? work = null,
        Ownership? ownership = null)
    {
        CoverKey = coverKey;
        _covers = covers;
        OwnershipId = ownershipId;
        ReleaseId = releaseId;
        Title = title;
        Store = store;
        StoreBadge = store.ToUpperInvariant();
        Bucket = bucket;
        PlaytimeMinutes = playtimeMinutes;
        LastPlayedUtc = lastPlayedUtc;
        HasUnread = hasUnread;

        // Enrichment fills these in behind a library the user is already
        // browsing (§7), so every one of them is legitimately null on a fresh
        // database. Nothing here invents a stand-in — the detail view simply
        // does not render a row it has no fact for.
        ReleaseYear = work?.FirstReleaseYear;
        Summary = string.IsNullOrWhiteSpace(work?.Summary) ? null : work!.Summary;
        Publisher = string.IsNullOrWhiteSpace(work?.Publisher) ? null : work!.Publisher;
        Installed = ownership?.Installed ?? false;
        InstallPath = string.IsNullOrWhiteSpace(ownership?.InstallPath) ? null : ownership!.InstallPath;

        DormancyAlpha = Dormancy.VividAlphaFor(lastPlayedUtc, nowUtc);
        PlaytimeText = BuildPlaytimeText(playtimeMinutes);
        IdleText = BuildIdleText(lastPlayedUtc, nowUtc);
        // Three states, not two. A game with minutes on the clock and no
        // last-played stamp is common in Steam's local files, and calling that
        // "Never played" would contradict the playtime sitting next to it.
        HasLastPlayedDate = lastPlayedUtc is not null;
        LastPlayedText = lastPlayedUtc is { } played
            ? UpdateEventViewModel.LocalDateText(played)
            : playtimeMinutes <= 0 ? "Never played" : "Not recorded";
        StatText = BuildStatText(playtimeMinutes, lastPlayedUtc, nowUtc);

        var (start, end) = PlaceholderArt.VividColors(title);
        VividBrush = PlaceholderArt.Gradient(start, end);
        FloorBrush = PlaceholderArt.Gradient(PlaceholderArt.ToFloor(start), PlaceholderArt.ToFloor(end));
        FloorTitleBrush = new ImmutableSolidColorBrush(PlaceholderArt.ToFloor(Colors.White));
    }

    public long OwnershipId { get; }

    /// <summary>The release this ownership is a license for — the key update events hang off.</summary>
    public long ReleaseId { get; }

    public string Title { get; }

    /// <summary>Store as stored ("steam"); the badge is the uppercased display cut.</summary>
    public string Store { get; }

    public string StoreBadge { get; }

    /// <summary>Derived-bucket key (LibraryBuckets.*), used for rail filtering.</summary>
    public string Bucket { get; }

    public long PlaytimeMinutes { get; }

    public DateTime? LastPlayedUtc { get; }

    /// <summary>Unread-update badge (§5.2) — set from stale-but-patched bucket membership.</summary>
    public bool HasUnread { get; }

    /// <summary>Scrim line: "312h · idle 8mo", or "never opened".</summary>
    public string StatText { get; }

    /// <summary>List-view playtime column: "312h", or an em dash at zero.</summary>
    public string PlaytimeText { get; }

    /// <summary>List-view idle column: "8mo", or an em dash when never played.</summary>
    public string IdleText { get; }

    /// <summary>Detail view: the date itself, local time — "12 Mar 2023", or why there isn't one.</summary>
    public string LastPlayedText { get; }

    /// <summary>True when <see cref="LastPlayedText"/> is a date; false when it is a sentence.</summary>
    public bool HasLastPlayedDate { get; }

    /// <summary>works.first_release_year, or null until enrichment lands it.</summary>
    public int? ReleaseYear { get; }

    /// <summary>works.summary, or null. Never a placeholder sentence.</summary>
    public string? Summary { get; }

    /// <summary>works.publisher (migration 0005), or null until enrichment lands it.</summary>
    public string? Publisher { get; }

    /// <summary>Whether the store's local files say this is on disk right now.</summary>
    public bool Installed { get; }

    /// <summary>Install directory when installed and known; null otherwise.</summary>
    public string? InstallPath { get; }

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

        var playtime = BuildPlaytimeText(playtimeMinutes);

        return lastPlayedUtc is null
            ? playtime
            : $"{playtime} · idle {IdleSpanText(nowUtc - lastPlayedUtc.Value)}";
    }

    /// <summary>
    /// An em dash rather than "0h" at zero playtime: the list's job is to be
    /// scannable, and a column of zeroes reads as data when it is an absence.
    /// </summary>
    private static string BuildPlaytimeText(long playtimeMinutes)
        => playtimeMinutes <= 0
            ? "—"
            : playtimeMinutes < 60
                ? $"{playtimeMinutes}m"
                : $"{playtimeMinutes / 60}h";

    private static string BuildIdleText(DateTime? lastPlayedUtc, DateTime nowUtc)
        => lastPlayedUtc is { } played ? IdleSpanText(nowUtc - played) : "—";

    private static string IdleSpanText(TimeSpan idle)
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
