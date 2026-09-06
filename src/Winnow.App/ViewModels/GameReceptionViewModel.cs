using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.App.ViewModels;

/// <summary>
/// One attributed figure on the reception line: a source, a score, a count and
/// a tooltip. The count travels with the score and is on the line, because a 9
/// from four people and a 9 from four thousand are different claims.
/// </summary>
public sealed class ReceptionFigureViewModel
{
    /// <summary>Label naming the population behind the score.</summary>
    public required string Source { get; init; }

    /// <summary>The score as display text (e.g. "78" or "91%").</summary>
    public required string Value { get; init; }

    /// <summary>The count as display text, on the line beside the score.</summary>
    public required string Count { get; init; }

    /// <summary>The full attribution on hover. Steam's own label appears here, not on the line.</summary>
    public required string Tooltip { get; init; }

    /// <summary>Accessible name for the figure group.</summary>
    public required string AutomationName { get; init; }

    /// <summary>
    /// True for every figure after the first. Lets the item template draw a
    /// divider before each one without drawing one before the first.
    /// </summary>
    public bool IsSeparated { get; init; }
}

/// <summary>
/// Band 1's reception line. Three attributed figures in a fixed order: IGDB's
/// own user body, IGDB's aggregation of external critics, Steam's reviewers.
/// Never blended, because they are three populations answering three different
/// questions and Winnow computes no verdict of its own.
///
/// <para>A source with no figure writes no row in <c>work_ratings</c>, so
/// absence is a property of the data rather than of the view. No figure at all
/// means no line — never a zero and never an empty scale.</para>
///
/// <para>Steam's own label (e.g. "Very Positive") is deliberately kept off the
/// line and appears with the percentage and the count on hover.</para>
/// </summary>
public sealed class GameReceptionViewModel
{
    private GameReceptionViewModel(IReadOnlyList<ReceptionFigureViewModel> figures)
        => Figures = figures;

    /// <summary>The figures on the line, in the fixed order the design specifies.</summary>
    public IReadOnlyList<ReceptionFigureViewModel> Figures { get; }

    /// <summary>True when at least one source contributed a figure.</summary>
    public bool HasFigures => Figures.Count > 0;

    /// <summary>Accessible name for the whole line.</summary>
    public string AutomationName => GameReceptionCopy.LineAutomationName;

    /// <summary>
    /// Builds the line from the stored ratings. Returns null when no source has
    /// a figure, which is what makes "nothing rather than an empty scale" a
    /// property of the data.
    /// </summary>
    public static GameReceptionViewModel? From(IReadOnlyList<WorkRating>? ratings)
    {
        if (ratings is not { Count: > 0 })
        {
            return null;
        }

        var figures = new List<ReceptionFigureViewModel>(3);

        Add(ratings, RatingSources.IgdbUsers, figures, static (score, count) =>
            (GameReceptionCopy.SourceIgdbUsers,
             score.ToString("N0"),
             GameReceptionCopy.IgdbUsersCount(count),
             GameReceptionCopy.IgdbUsersTooltip(score, count),
             GameReceptionCopy.IgdbUsersAutomationName(score, count)));

        Add(ratings, RatingSources.IgdbCritics, figures, static (score, count) =>
            (GameReceptionCopy.SourceIgdbCritics,
             score.ToString("N0"),
             GameReceptionCopy.IgdbCriticsCount(count),
             GameReceptionCopy.IgdbCriticsTooltip(score, count),
             GameReceptionCopy.IgdbCriticsAutomationName(score, count)));

        var steam = ratings.FirstOrDefault(r => r.Source == RatingSources.Steam);
        if (steam is { HasFigure: true })
        {
            var percent = (int)Math.Round(steam.Score!.Value, MidpointRounding.AwayFromZero);
            var count = steam.RatingCount!.Value;

            figures.Add(new ReceptionFigureViewModel
            {
                Source = GameReceptionCopy.SourceSteam,
                Value = $"{percent:N0}%",
                Count = GameReceptionCopy.SteamCount(count),
                Tooltip = GameReceptionCopy.SteamTooltip(steam.Label, percent, count),
                AutomationName = GameReceptionCopy.SteamAutomationName(steam.Label, percent, count),
                IsSeparated = figures.Count > 0,
            });
        }

        return figures.Count == 0 ? null : new GameReceptionViewModel(figures);
    }

    private static void Add(
        IReadOnlyList<WorkRating> ratings,
        string source,
        List<ReceptionFigureViewModel> figures,
        Func<int, int, (string Source, string Value, string Count, string Tooltip, string AutomationName)> build)
    {
        var rating = ratings.FirstOrDefault(r => r.Source == source);
        if (rating is not { HasFigure: true })
        {
            return;
        }

        var score = (int)Math.Round(rating.Score!.Value, MidpointRounding.AwayFromZero);
        var parts = build(score, rating.RatingCount!.Value);

        figures.Add(new ReceptionFigureViewModel
        {
            Source = parts.Source,
            Value = parts.Value,
            Count = parts.Count,
            Tooltip = parts.Tooltip,
            AutomationName = parts.AutomationName,
            IsSeparated = figures.Count > 0,
        });
    }
}
