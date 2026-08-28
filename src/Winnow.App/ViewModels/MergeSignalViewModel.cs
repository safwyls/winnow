using System.Globalization;
using Winnow.Resolve.Matching;

namespace Winnow.App.ViewModels;

/// <summary>
/// One row of the signal diff (design-system §6: "signal diff between them —
/// title distance, year delta, publisher").
///
/// <para>Every value here is decoded from the frozen <c>signals_json</c> the
/// matcher wrote when the pair was queued, never re-scored. That is the whole
/// point of the payload being frozen: the explanation the user is shown cannot
/// drift away from the score they are being asked about after a threshold
/// tune.</para>
///
/// <para>A signal that did not fire is shown, dimmed, with the reason it could
/// not be evaluated. Hiding it would let a 0.65 built entirely on an unverified
/// title look like a 0.65 corroborated by year and publisher, and those are the
/// two cases the user most needs to tell apart — <i>Prey</i> against
/// <i>Prey</i>.</para>
/// </summary>
public sealed class MergeSignalViewModel
{
    private MergeSignalViewModel(
        string label,
        string valueText,
        string contributionText,
        string detail,
        bool fired,
        double contribution)
    {
        Label = label;
        ValueText = valueText;
        ContributionText = contributionText;
        Detail = detail;
        Fired = fired;
        Contribution = contribution;
    }

    /// <summary>Row label, uppercase — the rail and this screen both use Label style (§3).</summary>
    public string Label { get; }

    /// <summary>
    /// The headline number or verdict for this signal, in Plex Mono: the title
    /// distance, the year delta, SAME/DIFFERENT for publisher. An em dash when
    /// the signal did not fire.
    /// </summary>
    public string ValueText { get; }

    /// <summary>Signed points this signal added to the score, e.g. "+0.15".</summary>
    public string ContributionText { get; }

    /// <summary>The matcher's own phrasing, e.g. "2015 vs 2016 (Δ1)".</summary>
    public string Detail { get; }

    public bool Fired { get; }

    public double Contribution { get; }

    /// <summary>Evidence for the pair being the same game (Azure — the neutral informational colour).</summary>
    public bool IsForMatch => Fired && Contribution > 0;

    /// <summary>Evidence against (Amber — attention). Never Flare: Flare is the unread badge, nothing else.</summary>
    public bool IsAgainstMatch => Fired && Contribution < 0;

    /// <summary>
    /// Builds the row for one recorded signal. <paramref name="payload"/> supplies
    /// the headline values the matcher already reduced (title similarity, year
    /// delta, publisher verdict) so the row does not re-derive them from prose.
    /// </summary>
    public static MergeSignalViewModel FromSignal(SoftMatchSignalJson signal, SoftMatchSignalsPayload payload)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(payload);

        var (label, value) = signal.Name switch
        {
            SoftMatchSignalNames.Title => (
                "Title distance",
                Number(1.0 - payload.TitleSimilarity)),

            SoftMatchSignalNames.ReleaseYear => (
                "Year delta",
                payload.YearDelta is { } delta
                    ? string.Create(CultureInfo.InvariantCulture, $"Δ{delta}")
                    : Unknown),

            SoftMatchSignalNames.Publisher => (
                "Publisher",
                payload.PublisherMatch switch
                {
                    true => "SAME",
                    false => "DIFFERENT",
                    null => Unknown,
                }),

            SoftMatchSignalNames.CoverHash => (
                "Cover",
                payload.CoverHashDistance is { } distance
                    ? string.Create(CultureInfo.InvariantCulture, $"{distance}/64")
                    : Unknown),

            SoftMatchSignalNames.BundleEdition => (
                "Edition",
                signal.Agreement >= 1.0 ? "SAME" : "DIFFERENT"),

            _ => (signal.Name.Replace('_', ' '), signal.Fired ? Number(signal.Agreement ?? 0.0) : Unknown),
        };

        return new MergeSignalViewModel(
            label.ToUpperInvariant(),
            signal.Fired ? value : Unknown,
            Signed(signal.Contribution),
            signal.Detail,
            signal.Fired,
            signal.Contribution);
    }

    /// <summary>Em dash: "this signal had nothing to say", which is not the same as zero agreement.</summary>
    private const string Unknown = "—";

    private static string Number(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    // ASCII hyphen, not U+2212: every glyph in Plex Mono is one advance wide, so
    // the plain minus keeps the column aligned without depending on the face
    // carrying a tabular-width true minus.
    private static string Signed(double value)
        => value.ToString("+0.00;-0.00; 0.00", CultureInfo.InvariantCulture);
}
