using System.Globalization;

namespace Winnow.Enrich.SteamWeb.Model;

/// <summary>
/// Provenance strings for the M5 historical backfill. Both label
/// <c>play_records.source</c> rows the backfill writes; neither is ever written
/// by the resolver, so a row carrying one is always historical.
/// </summary>
public static class SteamHistorySources
{
    /// <summary>
    /// The earliest <c>rtime_first_played</c> Year in Review reported for an
    /// appid, across every year fetched.
    /// </summary>
    public const string YearInReview = "steam_yir";

    /// <summary><c>first_playtime</c> from <c>IPlayerService/ClientGetLastPlayedTimes</c>.</summary>
    public const string FirstPlayed = "steam_first_played";
}

/// <summary>
/// One calendar month of play for one appid, as Year in Review reports it:
/// seconds played DURING the month, not a cumulative total.
/// </summary>
/// <param name="Year">Calendar year, UTC.</param>
/// <param name="Month">Calendar month, 1-12.</param>
/// <param name="PlaytimeSeconds">Seconds played during the month. Never negative.</param>
/// <param name="Sessions">Session count for the month, or 0 when absent.</param>
public sealed record SteamMonthlyPlaytime(int Year, int Month, long PlaytimeSeconds, int Sessions)
{
    /// <summary>
    /// The last whole second of the month, UTC. Snapshots are stamped here: a
    /// per-period figure is only meaningful as a cumulative reading once the
    /// period has closed, and <c>observed_at</c> is stored to whole seconds.
    /// </summary>
    public DateTime MonthEndUtc => MonthEnd(Year, Month);

    /// <summary>The last whole second of the month BEFORE this one, UTC.</summary>
    public DateTime PrecedingMonthEndUtc
        => new DateTime(Year, Month, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(-1);

    /// <summary>Chronological sort key, so 2022-12 sorts below 2023-01.</summary>
    public int Ordinal => (Year * 12) + Month;

    /// <summary>The last whole second of the given month, UTC.</summary>
    public static DateTime MonthEnd(int year, int month)
        => new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1).AddSeconds(-1);
}

/// <summary>
/// One appid's entry in a Year in Review response.
/// </summary>
/// <param name="AppId">Steam appid as a string, matching <c>external_ids.provider_id</c>.</param>
/// <param name="Months">
/// Per-month play within the year. Empty is ordinary: Steam reports a game in
/// the year's game list whether or not the monthly breakdown carries it.
/// </param>
/// <param name="TotalPlaytimeSeconds">The year's total for this appid, as reported.</param>
/// <param name="TotalSessions">The year's session count for this appid, or 0.</param>
/// <param name="FirstPlayedUtc">
/// <c>rtime_first_played</c>, or null when absent or zero. Zero means "not
/// tracked" and is never 1970; the shared rule is
/// <see cref="Core.Domain.SteamTime"/>.
/// </param>
public sealed record SteamYearInReviewGame(
    string AppId,
    IReadOnlyList<SteamMonthlyPlaytime> Months,
    long TotalPlaytimeSeconds,
    int TotalSessions,
    DateTime? FirstPlayedUtc);

/// <summary>
/// The result of one <c>ISaleFeatureService/GetUserYearInReview</c> call.
/// </summary>
/// <param name="SteamId">The account that was asked for.</param>
/// <param name="Year">The year that was asked for.</param>
/// <param name="Answered">
/// Whether Steam returned a body Winnow could read. False means the request did
/// not complete (offline, throttled past the retry budget, or a non-200), and
/// the year must be retried later rather than recorded as done.
/// </param>
/// <param name="AccountId">
/// <c>response.stats.account_id</c>, the steam3 id of the account the stats are
/// FOR. Null when the response carried none. Compared against
/// <paramref name="SteamId"/> before anything is imported: the API key
/// identifies the account, so a key belonging to a different account than the
/// one being back-filled would otherwise write one person's history onto
/// another's ownerships.
/// </param>
/// <param name="Games">Per-appid stats. Empty on an answered-but-empty year, and on a failure.</param>
/// <param name="ObservedAt">When the response was fetched, or served from cache (UTC).</param>
/// <param name="FromCache">True when a fresh cache entry answered and no request was made.</param>
public sealed record SteamYearInReview(
    SteamId SteamId,
    int Year,
    bool Answered,
    uint? AccountId,
    IReadOnlyList<SteamYearInReviewGame> Games,
    DateTime ObservedAt,
    bool FromCache)
{
    /// <summary>The unanswered result: retry later, and record no completion marker.</summary>
    public static SteamYearInReview Unanswered(SteamId steamId, int year, DateTime observedAt)
        => new(steamId, year, Answered: false, AccountId: null, Games: [], observedAt, FromCache: false);

    /// <summary>
    /// True when Steam answered and the account id it reported is not the one
    /// asked for. The response describes somebody else's play and must not be
    /// imported.
    /// </summary>
    public bool AccountMismatch => Answered && AccountId is { } id && id != SteamId.AccountId;

    /// <summary>True when Steam answered, for the right account, with nothing to import.</summary>
    public bool AnsweredEmpty => Answered && !AccountMismatch && Games.Count == 0;

    /// <summary>Diagnostics. Carries counts, never the key that fetched them.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"SteamYearInReview(steamid={SteamId}, year={Year}, answered={Answered}, games={Games.Count}, cached={FromCache})");
}

/// <summary>
/// One appid's entry in a <c>IPlayerService/ClientGetLastPlayedTimes</c>
/// response.
/// </summary>
/// <param name="AppId">Steam appid as a string.</param>
/// <param name="PlaytimeForeverMinutes">
/// The account's cumulative total for this appid, in minutes. This is the anchor
/// the monthly reconstruction walks backwards from, so it is present truth and
/// not an estimate.
/// </param>
/// <param name="LastPlayedUtc"><c>last_playtime</c>, or null when absent or a placeholder.</param>
/// <param name="FirstPlayedUtc">
/// <c>first_playtime</c>, or null. Verified live 2026-08-28: this field is
/// <b>0 for many entries</b>, and 0 means "not tracked", never 1970-01-01.
/// </param>
/// <param name="PlaytimeTwoWeeksMinutes">Recent playtime in minutes, or 0 when absent.</param>
public sealed record SteamLastPlayedGame(
    string AppId,
    long PlaytimeForeverMinutes,
    DateTime? LastPlayedUtc,
    DateTime? FirstPlayedUtc,
    long PlaytimeTwoWeeksMinutes);

/// <summary>
/// The result of one <c>IPlayerService/ClientGetLastPlayedTimes</c> call. The
/// endpoint takes no <c>steamid</c>: the key identifies the account, verified
/// live 2026-08-28.
/// </summary>
/// <param name="Answered">Whether Steam returned a body Winnow could read.</param>
/// <param name="Games">Per-appid entries. Empty on a failure.</param>
/// <param name="ObservedAt">When the response was fetched, or served from cache (UTC).</param>
/// <param name="FromCache">True when a fresh cache entry answered and no request was made.</param>
public sealed record SteamLastPlayedTimes(
    bool Answered,
    IReadOnlyList<SteamLastPlayedGame> Games,
    DateTime ObservedAt,
    bool FromCache)
{
    /// <summary>The unanswered result: no anchors, and explicitly not "everything is zero".</summary>
    public static SteamLastPlayedTimes Unanswered(DateTime observedAt)
        => new(Answered: false, Games: [], observedAt, FromCache: false);

    /// <summary>Cumulative minutes per appid; the anchor map the reconstruction takes.</summary>
    public IReadOnlyDictionary<string, long> AnchorsByAppId
        => Games.ToDictionary(static g => g.AppId, static g => g.PlaytimeForeverMinutes, StringComparer.Ordinal);

    /// <summary>How many entries carry a usable <c>first_playtime</c> (the count that is NOT the entry count).</summary>
    public int WithFirstPlayed => Games.Count(static g => g.FirstPlayedUtc is not null);

    /// <summary>Diagnostics. Carries counts, never the key that fetched them.</summary>
    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"SteamLastPlayedTimes(answered={Answered}, games={Games.Count}, first_played={WithFirstPlayed}, cached={FromCache})");
}
