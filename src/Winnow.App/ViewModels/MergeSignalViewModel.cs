using System.Globalization;
using Winnow.Resolve.Matching;

namespace Winnow.App.ViewModels;

/// <summary>
/// One row of the signal diff (design-system §6: "signal diff between them —
/// title distance, year delta, publisher"). A row shows its label, the
/// headline value, and the matcher's own sentence. Signed contribution points
/// were removed; they exposed the scorer's arithmetic, and nobody on this
/// screen tunes weights.
///
/// <para>Every value here is decoded from the frozen <c>signals_json</c> the
/// matcher wrote when the pair was queued, never re-scored. That is the whole
/// point of the payload being frozen: the explanation the user is shown cannot
/// drift away from the score they are being asked about after a threshold
/// tune.</para>
///
/// <para>A signal that did not fire is shown with the reason it could not be
/// evaluated; hiding it would let a 0.65 built entirely on an unverified title
/// look like a 0.65 corroborated by year and publisher, and those are the two
/// cases the user most needs to tell apart — <i>Prey</i> against
/// <i>Prey</i>. The unfired row keeps full ink; only the value cell steps
/// from Text to TextDim, and the state is carried three ways: the em-dash
/// value, the reason sentence, and that demoted ink.</para>
/// </summary>
public sealed class MergeSignalViewModel
{
    private MergeSignalViewModel(
        string label,
        string valueText,
        string detail,
        bool fired)
    {
        Label = label;
        ValueText = valueText;
        Detail = detail;
        Fired = fired;
    }

    /// <summary>Row label, uppercase — the rail and this screen both use Label style (§3).</summary>
    public string Label { get; }

    /// <summary>
    /// The headline number or verdict for this signal, in Plex Mono: Δ-prefixed
    /// title distance, Δ-prefixed year delta, SAME/DIFFERENT for publisher. An
    /// em dash when the signal did not fire.
    /// </summary>
    public string ValueText { get; }

    /// <summary>The matcher's own phrasing, e.g. "2015 vs 2016 (Δ1)".</summary>
    public string Detail { get; }

    public bool Fired { get; }

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
                "Title",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Δ{Number(1.0 - payload.TitleSimilarity)}")),

            SoftMatchSignalNames.ReleaseYear => (
                "Year",
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
            signal.Detail,
            signal.Fired);
    }

    /// <summary>Em dash: "this signal had nothing to say", which is not the same as zero agreement.</summary>
    private const string Unknown = "—";

    private static string Number(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);
}
