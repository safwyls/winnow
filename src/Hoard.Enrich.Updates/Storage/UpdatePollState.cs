using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hoard.Enrich.Updates.Storage;

/// <summary>
/// What the poller remembers about one appid between passes. Persisted as JSON
/// in <c>metadata_cache</c> under provider <c>update-poll</c>, with the row's
/// <c>fetched_at</c> carrying <see cref="LastPolledAt"/> — so "when did we last
/// look" is a column the store can order by, not a field buried in a blob.
///
/// <para>All of this is an optimisation, never a source of truth. Losing it
/// costs a re-observation and nothing else: migration 0004's
/// <c>ux_update_events_identity</c> makes the re-write a no-op.</para>
/// </summary>
public sealed record UpdatePollState
{
    /// <summary>When this app was last polled. Null means never.</summary>
    [JsonIgnore]
    public DateTime? LastPolledAt { get; init; }

    /// <summary>
    /// The <c>gid</c> of the newest patch note seen. Compared alongside
    /// <see cref="LastNewsDate"/> so an edited-in-place announcement (same date,
    /// new gid) still registers as news.
    /// </summary>
    [JsonPropertyName("news_gid")]
    public string? LastNewsGid { get; init; }

    /// <summary>
    /// The high-water mark: the <c>date</c> of the newest patch note seen.
    /// <c>GetNewsForApp</c> has no "since" parameter, so this comparison is the
    /// entire change-detection mechanism.
    /// </summary>
    [JsonPropertyName("news_date")]
    public DateTime? LastNewsDate { get; init; }

    /// <summary>
    /// The <c>timeupdated</c> of the newest build push recorded for this app.
    /// steamcmd.net reports a persistent point-in-time value, so re-reading it
    /// returns the same number until the next push — this is what stops a
    /// re-poll from re-emitting the same event before the UNIQUE index has to.
    /// </summary>
    [JsonPropertyName("build_time_updated")]
    public DateTime? LastBuildTimeUpdated { get; init; }

    /// <summary>
    /// While set and in the future, this app is polled <b>daily</b> rather than
    /// on its slot: an announcement has landed and the correlating build push
    /// has not, so the pair may still complete.
    ///
    /// <para>This is the Stardew Valley case from the spike — its build arrived
    /// two days <i>after</i> its announcement — and it is why the correlation
    /// window has to be measured in days and the watch has to exist at all. It
    /// expires at announcement + <c>CorrelationWindowDays</c>, because after that
    /// a push can no longer correlate with it.</para>
    /// </summary>
    [JsonPropertyName("watch_until")]
    public DateTime? WatchUntil { get; init; }

    /// <summary>True when this app has never been observed, so the next observation is a baseline.</summary>
    [JsonIgnore]
    public bool IsBaseline => LastNewsDate is null && LastNewsGid is null;

    /// <summary>
    /// Whether <paramref name="publishedAt"/>/<paramref name="gid"/> is news
    /// relative to what is remembered. A strictly later date, or the same date
    /// under a different gid.
    /// </summary>
    public bool IsNewsSince(DateTime publishedAt, string gid)
        => LastNewsDate is not { } known
            || publishedAt > known
            || !string.Equals(LastNewsGid, gid, StringComparison.Ordinal);

    internal static UpdatePollState FromCache(UpdateCacheEntry entry)
    {
        var state = entry.PayloadJson is { } json ? Deserialize(json) : new UpdatePollState();
        return state with { LastPolledAt = DateTime.SpecifyKind(entry.FetchedAt, DateTimeKind.Utc) };
    }

    internal string ToJson() => JsonSerializer.Serialize(this);

    private static UpdatePollState Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<UpdatePollState>(json) ?? new UpdatePollState();
        }
        catch (JsonException)
        {
            // Unreadable state is treated as absent, which re-baselines this one
            // app. That is a request, not a bug — and far better than a whole
            // background pass throwing over one corrupt cache row.
            return new UpdatePollState();
        }
    }
}
