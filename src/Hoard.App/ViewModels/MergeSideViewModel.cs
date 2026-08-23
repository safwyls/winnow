using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.App.Services;
using Hoard.Covers;

namespace Hoard.App.ViewModels;

/// <summary>
/// One half of a merge-confirm pair: the cover at 200×300 (§6) and the facts
/// the user needs to tell it from the other half.
///
/// <para>Cover art follows the grid exactly — <see cref="ICoverCache"/> if the
/// host registered one, and the procedural placeholder underneath as the
/// fallback, so a game with no capsule shows its title in Bricolage on a
/// Surface field rather than a hole or a spinner (§7). There is no dormancy
/// ramp here: the question on this screen is "are these the same game", and
/// fading one side by how long ago it was played would be a second visual
/// language answering a question nobody asked.</para>
/// </summary>
public partial class MergeSideViewModel : ObservableObject
{
    private readonly ICoverCache? _covers;

    public MergeSideViewModel(
        long releaseId,
        string title,
        string? normalizedTitle = null,
        int? year = null,
        string? publisher = null,
        CoverKey? coverKey = null,
        ICoverCache? covers = null)
    {
        ReleaseId = releaseId;
        Title = string.IsNullOrWhiteSpace(title) ? $"Release {releaseId}" : title;
        NormalizedTitle = normalizedTitle ?? string.Empty;
        Year = year;
        Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher;
        CoverKey = coverKey;
        _covers = covers;

        var (start, end) = PlaceholderArt.VividColors(Title);
        PlaceholderBrush = PlaceholderArt.Gradient(start, end);
    }

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
    /// The row that names the record itself. A merge is a decision about two
    /// database rows, and when both sides are called "Prey" the release id is
    /// the only thing on screen that distinguishes them.
    /// </summary>
    public string ReleaseText => string.Create(CultureInfo.InvariantCulture, $"#{ReleaseId}");

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
