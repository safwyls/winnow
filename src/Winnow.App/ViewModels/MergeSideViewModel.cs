using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// One member's visual face: the cover at 200x300 (§6) and the facts the
/// user needs to tell it from its siblings. Used for both sides of a pair
/// and for every row in a roster.
///
/// <para>Cover art follows the grid exactly: <see cref="ICoverCache"/> if the
/// host registered one, and the procedural placeholder underneath as the
/// fallback, so a game with no capsule shows its title in Bricolage on a
/// Surface field rather than a hole or a spinner (§7). There is no dormancy
/// ramp here; the question on this screen is "are these the same game", and
/// fading one side by how long ago it was played would be a second visual
/// language answering a question nobody asked.</para>
/// </summary>
public partial class MergeSideViewModel : ObservableObject, IMergeMemberFacts
{
    private readonly ICoverCache? _covers;

    public MergeSideViewModel(
        long releaseId,
        string title,
        int? year = null,
        string? publisher = null,
        CoverKey? coverKey = null,
        ICoverCache? covers = null,
        IReadOnlyList<string>? stores = null)
    {
        ReleaseId = releaseId;
        Title = string.IsNullOrWhiteSpace(title) ? $"Release {releaseId}" : title;
        Year = year;
        Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher;
        CoverKey = coverKey;
        _covers = covers;

        var ordered = stores is null
            ? []
            : stores
                .Where(store => !string.IsNullOrWhiteSpace(store))
                .Select(store => store.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        StoreChips = [.. ordered.Select(StoreNaming.Badge)];
        StoreNames = string.Join(", ", ordered.Select(StoreNaming.Label));

        var (start, end) = PlaceholderArt.VividColors(Title);
        PlaceholderBrush = PlaceholderArt.Gradient(start, end);
    }

    /// <summary>
    /// Badge text for each store this member is owned on, one chip per store.
    /// Ordered as the ownership rows arrived; duplicates and blanks removed.
    /// </summary>
    public IReadOnlyList<string> StoreChips { get; }

    /// <summary>
    /// The same stores spelled out as display names, comma-joined. Used for
    /// the chip row's tooltip and for the automation name that tells two
    /// identically titled members apart.
    /// </summary>
    public string StoreNames { get; }

    /// <summary>
    /// False when no ownership row names a store for this member, so the chip
    /// row is not drawn and the automation name falls back to the store-less
    /// format.
    /// </summary>
    public bool HasStores => StoreChips.Count > 0;

    public long ReleaseId { get; }

    public string Title { get; }

    public int? Year { get; }

    public string? Publisher { get; }

    public CoverKey? CoverKey { get; }

    /// <summary>Year in Plex Mono, or an em dash when no source has supplied one yet.</summary>
    public string YearText => Year?.ToString(CultureInfo.InvariantCulture) ?? "—";

    public string PublisherText => Publisher ?? "publisher unknown";

    /// <summary>Procedural stand-in; painted whenever no real cover is loaded.</summary>
    public IBrush PlaceholderBrush { get; }

    /// <summary>The vivid cover, or null while the placeholder shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial Bitmap? Cover { get; set; }

    /// <summary>
    /// The desaturated floor under the vivid layer. Drawn at full opacity with
    /// the vivid layer's opacity carrying the dormancy ramp, exactly as the
    /// grid does, so a row's thumbnail fades on the same rule as its tile.
    /// </summary>
    [ObservableProperty]
    public partial Bitmap? CoverFloor { get; set; }

    /// <summary>True until art arrives.</summary>
    public bool ShowPlaceholder => Cover is null;

    /// <summary>Asks the cache for the art at the width it will be drawn at, off-thread.</summary>
    public void RequestCover(double displayWidthPixels)
    {
        if (_covers is null || CoverKey is not { } key || Cover is not null)
        {
            return;
        }

        if (_covers.TryGet(key, displayWidthPixels, out var cached))
        {
            CoverFloor = cached.Floor;
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

        Dispatcher.UIThread.Post(() =>
        {
            CoverFloor = art.Floor;
            Cover = art.Vivid;
        });
    }
}
