namespace Winnow.Core.Domain;

/// <summary>
/// Steam's epoch conventions for the timestamps it reports — the rule that
/// decides when one of them is a real date and when it is a placeholder.
///
/// <para><b>Why this lives in Core rather than in a reader.</b> Steam reports
/// last-played over two entirely different transports: <c>LastPlayed</c> in
/// <c>localconfig.vdf</c>/<c>appmanifest_*.acf</c> (§4.1) and
/// <c>rtime_last_played</c> from <c>GetOwnedGames</c> (§4.2). Both are Unix
/// epoch seconds and both carry the same placeholders, because the placeholder
/// is a fact about Steam's data, not about VDF or JSON. When each reader owned
/// its own answer they disagreed: the local reader mapped <c>86400</c> to
/// "unknown" while the Web reader mapped it to a literal <c>1970-01-02</c>, so
/// the same game arrived from the two sources as two different observations and
/// appended a new <c>play_records</c> row on every sync, forever. One rule, in
/// the assembly both readers already depend on, is what stops that
/// reappearing — and <see cref="Ingest.CandidateOwnership.LastPlayedAt"/>, the
/// contract both readers emit into, is defined here too.</para>
/// </summary>
public static class SteamTime
{
    /// <summary>
    /// 1980-01-01T00:00:00Z as Unix epoch seconds: the sanity floor below which
    /// a Steam timestamp is a placeholder, not a date.
    ///
    /// <para>Two placeholders are known and verified on disk
    /// (docs/spikes/steam-local-files.md §3, trap 1): <c>"0"</c> on a
    /// never-launched install, and <c>"86400"</c> — 1970-01-02 — on games last
    /// played before Steam tracked timestamps. Steam did not exist before 2003,
    /// so any value in the 1970s is a marker rather than a session; the floor is
    /// set at 1980 to catch whatever other small constants Valve reaches for
    /// without ever being able to reject a genuine play date.</para>
    /// </summary>
    public const long MinValidEpochSeconds = 315_532_800;

    /// <summary>1980-01-01T00:00:00Z — <see cref="MinValidEpochSeconds"/> as a UTC instant.</summary>
    public static readonly DateTime MinValidUtc = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Converts Steam epoch seconds to UTC, or null when the value is absent or
    /// is one of the placeholders <see cref="MinValidEpochSeconds"/> describes.
    /// Null means <b>unknown</b> — never "the epoch", and never a zero date.
    /// </summary>
    public static DateTime? FromEpochSeconds(long? seconds)
        => seconds is { } s && s >= MinValidEpochSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime
            : null;

    /// <summary>
    /// The same rule applied to an instant a caller has already converted:
    /// returns null for anything below <see cref="MinValidUtc"/>. For readers
    /// that build the <see cref="DateTime"/> before they can test it.
    ///
    /// <para>Compares ticks and ignores <see cref="DateTimeKind"/> deliberately:
    /// the gap between the placeholders (1970) and the floor (1980) is a decade,
    /// so no offset a wrongly-kinded value could carry can move it across.</para>
    /// </summary>
    public static DateTime? Sanitize(DateTime? value)
        => value is { } v && v >= MinValidUtc ? value : null;
}
