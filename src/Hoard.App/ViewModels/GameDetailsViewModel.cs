using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.Covers;

namespace Hoard.App.ViewModels;

/// <summary>
/// Everything the tile deliberately does not say.
///
/// <para>design-system.md §5.3 caps the hover overlay at four facts — "the tile
/// is a decision surface, not a detail view" — which only works if the detail
/// view exists somewhere. This is that somewhere: a modal over the library,
/// opened by <c>Enter</c> or a double click and dismissed by <c>Escape</c> or a
/// click on the scrim (§8: keyboard reachable, Escape closes).</para>
///
/// <para><b>Nothing here is invented.</b> Year, publisher and summary arrive
/// from IGDB enrichment behind a library the user is already browsing (§7), so
/// each is legitimately null for a long while after first run. A null fact
/// renders as no row at all rather than as "Unknown" — a placeholder that turns
/// into real data later is a lie with a timer on it.</para>
/// </summary>
public partial class GameDetailsViewModel : ObservableObject
{
    /// <summary>Cover at a size worth looking at, on the same 2:3 capsule geometry.</summary>
    public const double CoverWidth = 200;

    public const double CoverHeight = CoverWidth * 1.5;

    private readonly ICoverCache? _covers;

    public GameDetailsViewModel(
        GameTileViewModel tile,
        string bucketLabel,
        IReadOnlyList<UpdateEventViewModel> updates,
        string? publisher = null,
        ICoverCache? covers = null)
    {
        Tile = tile;
        BucketLabel = bucketLabel;
        Updates = updates;
        Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher;
        _covers = covers;
    }

    /// <summary>The tile this describes — title, store, art and the stat strings all come from it.</summary>
    public GameTileViewModel Tile { get; }

    public string Title => Tile.Title;

    public string StoreBadge => Tile.StoreBadge;

    /// <summary>The §7 bucket name this game currently falls in ("Never played").</summary>
    public string BucketLabel { get; }

    public string PlaytimeText => Tile.PlaytimeText;

    public string LastPlayedText => Tile.LastPlayedText;

    /// <summary>
    /// Dates render in Plex Mono (§3); the sentences that stand in for a
    /// missing date are prose and render as prose. Both are the same field, so
    /// the view swaps the face rather than the row.
    /// </summary>
    public bool HasLastPlayedDate => Tile.HasLastPlayedDate;

    public bool LacksLastPlayedDate => !Tile.HasLastPlayedDate;

    public string IdleText => Tile.IdleText;

    /// <summary>Install state as a fact, not a verdict.</summary>
    public string InstallText => Tile.Installed ? "Installed" : "Not installed";

    public string? InstallPath => Tile.InstallPath;

    public bool HasInstallPath => InstallPath is not null;

    public bool HasReleaseYear => Tile.ReleaseYear is > 0;

    /// <summary>Plex Mono, tabular, no thousands separator — it is a year, not a count.</summary>
    public string ReleaseYearText => Tile.ReleaseYear?.ToString("D4") ?? string.Empty;

    /// <summary>works.publisher (migration 0005), or null until enrichment lands it.</summary>
    public string? Publisher { get; }

    public bool HasPublisher => Publisher is not null;

    public string? Summary => Tile.Summary;

    public bool HasSummary => Summary is not null;

    /// <summary>Newest first — the update the user missed most recently is the one they want.</summary>
    public IReadOnlyList<UpdateEventViewModel> Updates { get; }

    public bool HasUpdates => Updates.Count > 0;

    /// <summary>
    /// Whether the lower half has anything in it at all. On a fresh database
    /// it does not, and a scroll region reserving space for nothing reads as a
    /// panel that failed to load.
    /// </summary>
    public bool HasBody => HasSummary || HasUpdates;

    /// <summary>"3 updates since you played" (§7's badge tooltip copy, exactly).</summary>
    public string UpdatesHeading => Updates.Count == 1
        ? "1 update since you played"
        : $"{Updates.Count} updates since you played";

    /// <summary>Real cover at detail resolution; null until it arrives (or forever, with no art).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial Bitmap? Cover { get; set; }

    /// <summary>Procedural art is the fallback here too — never a hole, never a spinner (§7).</summary>
    public bool ShowPlaceholder => Cover is null;

    /// <summary>The tile's own placeholder gradient, so the modal looks like the tile it came from.</summary>
    public IBrush PlaceholderBrush => Tile.VividBrush;

    /// <summary>
    /// Full saturation, always. The dormancy ramp is a scanning aid for the
    /// grid (§5.1); once the user has chosen a game, fading its art tells them
    /// something they just acted on.
    /// </summary>
    public void RequestCover(double displayWidthPixels)
    {
        if (_covers is null || Tile.CoverKey is not { } key)
        {
            return;
        }

        if (_covers.TryGet(key, displayWidthPixels, out var cached))
        {
            Cover = cached.Vivid;
            return;
        }

        _ = LoadCoverAsync(key, displayWidthPixels);
    }

    private async Task LoadCoverAsync(CoverKey key, double displayWidthPixels)
    {
        var art = await _covers!.GetAsync(key, displayWidthPixels).ConfigureAwait(false);
        if (art is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => Cover = art.Vivid);
    }
}
