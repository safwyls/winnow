using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Identity;
using Winnow.Core.Queries;

namespace Winnow.App.ViewModels;

/// <summary>
/// The ALSO COVERS section of the details modal. Every row is one store
/// entry and carries that entry's OWN minutes beside that same entry's OWN
/// last-played; they are never crossed (F10). The total is a sum across
/// stores — Winnow's own composite, no source reports it — and carries its
/// own coherent last-played, the latest anywhere in the group. The per-store
/// rows stay on screen underneath so the composite can be checked against
/// them.
///
/// <para>Achievements are listed per release and never merged (§6.2). There
/// is no combined percentage on this screen and no property that could
/// produce one.</para>
///
/// <para>Separate retracts one link from the place the user noticed the
/// problem.</para>
/// </summary>
public sealed partial class GameCoverageViewModel : ObservableObject
{
    private readonly Func<long, Task>? _separate;

    public GameCoverageViewModel(
        IdentityCoverage coverage,
        IReadOnlyDictionary<long, string> titleByWork,
        IReadOnlyDictionary<long, ReleaseAchievementSummary> achievementsByRelease,
        Func<long, Task>? separate = null)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(titleByWork);
        ArgumentNullException.ThrowIfNull(achievementsByRelease);

        _separate = separate;

        var rows = new List<CoverageRowViewModel>();
        foreach (var entry in coverage.OwnEntries)
        {
            rows.Add(Row(entry, covered: false, titleByWork, achievementsByRelease));
        }

        foreach (var entry in coverage.CoveredEntries)
        {
            rows.Add(Row(entry, covered: true, titleByWork, achievementsByRelease));
        }

        Rows = rows;
        HasCoverage = coverage.HasCoverage;

        // Both halves of the composite come off one CoveragePlaytime, which is
        // the only way one can be built. There is no path here that pairs a sum
        // with a date from a single store.
        TotalPlaytimeText = GameTileViewModel.BuildPlaytimeText(coverage.Total.PlaytimeMinutes);
        TotalLastPlayedText = coverage.Total.LastPlayedAt is { } played
            ? UpdateEventViewModel.LocalDateText(played)
            : "—";
        IsComposite = coverage.Total.IsComposite;
        StoreCountText = coverage.Total.EntryCount.ToString("N0");
    }

    /// <summary>Every store entry of this game, its own first.</summary>
    public IReadOnlyList<CoverageRowViewModel> Rows { get; }

    /// <summary>
    /// True when at least one other title is covered. The section does not
    /// draw at all when this is false.
    /// </summary>
    public bool HasCoverage { get; }

    /// <summary>The summed minutes across every entry.</summary>
    public string TotalPlaytimeText { get; }

    /// <summary>
    /// The latest last-played across the SAME entries the sum was taken
    /// over.
    /// </summary>
    public string TotalLastPlayedText { get; }

    /// <summary>True when more than one entry contributed to the sum.</summary>
    public bool IsComposite { get; }

    /// <summary>How many entries the sum is over.</summary>
    public string StoreCountText { get; }

    /// <summary>Section heading.</summary>
    public string Heading => "ALSO COVERS";

    /// <summary>Label for the composite figure.</summary>
    public string TotalLabel => "TOTAL";

    /// <summary>Caption stating the total is a sum across the listed entries.</summary>
    public string TotalNote => "Summed across entries below.";

    /// <summary>A write that did not land. Amber, non-blocking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;

    /// <summary>Retracts the one link named by the row.</summary>
    [RelayCommand]
    private async Task SeparateAsync(CoverageRowViewModel? row)
    {
        if (row is null || !row.IsCovered || _separate is null)
        {
            return;
        }

        try
        {
            Problem = null;
            await _separate(row.WorkId);
        }
        catch
        {
            Problem = "Couldn't separate that just now.";
        }
    }

    private CoverageRowViewModel Row(
        CoverageEntry entry,
        bool covered,
        IReadOnlyDictionary<long, string> titleByWork,
        IReadOnlyDictionary<long, ReleaseAchievementSummary> achievementsByRelease)
        => new(
            entry,
            covered,
            titleByWork.TryGetValue(entry.WorkId, out var title) ? title : entry.Title,
            achievementsByRelease.TryGetValue(entry.ReleaseId, out var summary) ? summary : null);
}

/// <summary>
/// One store entry of one game. Minutes and last-played on this row belong
/// to this entry and to no other — the F10 discipline stated at the grain
/// it applies.
/// </summary>
public sealed class CoverageRowViewModel
{
    public CoverageRowViewModel(
        CoverageEntry entry,
        bool covered,
        string title,
        ReleaseAchievementSummary? achievements)
    {
        ArgumentNullException.ThrowIfNull(entry);

        WorkId = entry.WorkId;
        ReleaseId = entry.ReleaseId;
        OwnershipId = entry.OwnershipId;
        Title = title;
        Store = entry.Store;
        StoreBadge = entry.Store.ToUpperInvariant();
        IsCovered = covered;
        PlaytimeText = GameTileViewModel.BuildPlaytimeText(entry.PlaytimeMinutes);
        LastPlayedText = entry.LastPlayedAt is { } played
            ? UpdateEventViewModel.LocalDateText(played)
            : entry.PlaytimeMinutes <= 0 ? "Never played" : "Not recorded";

        // §6.2 literally: this release's own achievements, on this release's own
        // row, never averaged with the other release's.
        Achievements = achievements is { HasAny: true }
            ? new ReleaseAchievementRowViewModel(achievements, StoreBadge)
            : null;
    }

    /// <summary>
    /// The work this entry belongs to, unresolved. Separate retracts the
    /// link whose child is this work.
    /// </summary>
    public long WorkId { get; }

    public long ReleaseId { get; }

    public long OwnershipId { get; }

    /// <summary>This entry's own title, not the primary's.</summary>
    public string Title { get; }

    public string Store { get; }

    public string StoreBadge { get; }

    /// <summary>
    /// True when this entry belongs to a covered title rather than to the
    /// game the modal is about. Only these rows offer Separate.
    /// </summary>
    public bool IsCovered { get; }

    public string PlaytimeText { get; }

    public string LastPlayedText { get; }

    /// <summary>
    /// This release's achievement row, or null when the release defines
    /// none. Null renders as no row, not as zero of zero.
    /// </summary>
    public ReleaseAchievementRowViewModel? Achievements { get; }

    public bool HasAchievements => Achievements is not null;

    /// <summary>Button label for the retraction control.</summary>
    public string SeparateLabel => "Separate";

    /// <summary>Tooltip for the retraction control.</summary>
    public string SeparateTooltip => "Unlink this title";

    /// <summary>
    /// Screen-reader name. Must name the title and the store because two
    /// rows can share a title.
    /// </summary>
    public string SeparateAutomationName => $"Separate {Title} ({StoreBadge})";
}

/// <summary>
/// One release's achievements (§6.2). The percentage on this row is this
/// release's own, computed from this release's own two numbers. Nothing
/// anywhere blends it with another release's.
/// </summary>
public sealed class ReleaseAchievementRowViewModel
{
    public ReleaseAchievementRowViewModel(ReleaseAchievementSummary summary, string storeBadge)
    {
        ArgumentNullException.ThrowIfNull(summary);

        ReleaseId = summary.ReleaseId;
        StoreBadge = storeBadge;
        Unlocked = summary.Unlocked;
        Total = summary.Total;
        CountText = $"{summary.Unlocked:N0}/{summary.Total:N0}";
        PercentText = summary.PercentComplete is { } pct
            ? $"{pct:0.#}%"
            : string.Empty;
    }

    public long ReleaseId { get; }

    public string StoreBadge { get; }

    public int Unlocked { get; }

    public int Total { get; }

    /// <summary>Unlocked/total as "12/45". Plex Mono tabular.</summary>
    public string CountText { get; }

    /// <summary>This release's own completion. Never a group figure.</summary>
    public string PercentText { get; }

    /// <summary>Row label.</summary>
    public string Label => "ACHIEVEMENTS";
}
