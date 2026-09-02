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
public partial class MergeSideViewModel : ObservableObject
{
    private readonly ICoverCache? _covers;
    private readonly IReadOnlyList<long>? _alsoReleaseIds;

    public MergeSideViewModel(
        long releaseId,
        string title,
        string? normalizedTitle = null,
        int? year = null,
        string? publisher = null,
        CoverKey? coverKey = null,
        ICoverCache? covers = null,
        IReadOnlyList<long>? alsoReleaseIds = null,
        IReadOnlyList<string>? stores = null)
    {
        ReleaseId = releaseId;
        Title = string.IsNullOrWhiteSpace(title) ? $"Release {releaseId}" : title;
        NormalizedTitle = normalizedTitle ?? string.Empty;
        Year = year;
        Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher;
        CoverKey = coverKey;
        _covers = covers;
        _alsoReleaseIds = alsoReleaseIds;

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

    /// <summary>What the matcher actually compared. Shown because "why" is the screen.</summary>
    public string NormalizedTitle { get; }

    public int? Year { get; }

    public string? Publisher { get; }

    public CoverKey? CoverKey { get; }

    /// <summary>Year in Plex Mono, or an em dash when no source has supplied one yet.</summary>
    public string YearText => Year?.ToString(CultureInfo.InvariantCulture) ?? "—";

    /// <summary>
    /// Every store entry under this member, listed. When two members are both
    /// called "Prey" the entry numbers are the only thing on screen that
    /// tells them apart. Lists the primary entry plus any others carried by
    /// <c>alsoReleaseIds</c>.
    /// </summary>
    public string ReleaseText
    {
        get
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"#{ReleaseId}");
            if (_alsoReleaseIds is null)
            {
                return text;
            }

            foreach (var id in _alsoReleaseIds)
            {
                if (id != ReleaseId)
                {
                    text += string.Create(CultureInfo.InvariantCulture, $" #{id}");
                }
            }

            return text;
        }
    }

    public string PublisherText => Publisher ?? "publisher unknown";

    public bool HasPublisher => Publisher is not null;

    /// <summary>Procedural stand-in; painted whenever no real cover is loaded.</summary>
    public IBrush PlaceholderBrush { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    public partial Bitmap? Cover { get; set; }

    public bool ShowPlaceholder => Cover is null;

    /// <summary>
    /// Fetches the cover at display resolution, off-thread. A miss is a normal
    /// answer — the placeholder is already on screen, so this is a repaint and
    /// never a load gate.
    /// </summary>
    public void RequestCover(double displayWidthPixels)
    {
        if (_covers is null || CoverKey is not { } key || Cover is not null)
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
