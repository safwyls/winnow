namespace Winnow.Enrich.Updates;

/// <summary>
/// What one <c>PollDueBatchAsync</c> pass did. Returned rather than only logged
/// to measure each source's wire traffic and recorded signals separately.
/// </summary>
public sealed record UpdatePollReport
{
    /// <summary>Steam releases that could ever show the badge (opened, not retired).</summary>
    public int Eligible { get; init; }

    /// <summary>Of those, how many this pass's schedule selected before the batch cap.</summary>
    public int Due { get; init; }

    /// <summary>How many were actually polled — <see cref="Due"/> capped by <c>MaxAppsPerBatch</c>.</summary>
    public int Polled { get; init; }

    /// <summary>Requests to api.steampowered.com. Cached no-feed negatives are not counted.</summary>
    public int NewsRequests { get; init; }

    /// <summary>
    /// Requests to api.steamcmd.net — the number that has to stay small. Cache
    /// hits are not counted.
    /// </summary>
    public int BuildInfoRequests { get; init; }

    /// <summary><c>announcement</c> rows created.</summary>
    public int AnnouncementsRecorded { get; init; }

    /// <summary><c>build_push</c> rows created.</summary>
    public int BuildPushesRecorded { get; init; }

    /// <summary>Apps that answered 403 — no news feed. Expected in double digits for a large library.</summary>
    public int NoFeed { get; init; }

    /// <summary>Apps left on the daily watch list: announced, build not yet landed.</summary>
    public int Watching { get; init; }

    /// <summary>Failed source operations. Their apps become eligible to retry the next day.</summary>
    public int Failures { get; init; }
}
