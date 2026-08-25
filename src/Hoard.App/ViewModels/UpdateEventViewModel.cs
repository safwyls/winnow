using Hoard.Core.Domain;

namespace Hoard.App.ViewModels;

/// <summary>
/// One update to this game, as the detail view lists it.
/// design-system.md §5.2: the unread badge exists so the user can go read what
/// changed, so every row that has a page is openable and every row that does
/// not says so by simply not offering a link.
///
/// <para>A row also knows whether it landed <b>after</b> the user's last
/// session. That is the distinction the whole product is built on, and until
/// now the detail view did not draw it: it listed every update a release had
/// ever had under the heading "N updates since you played", which for a game
/// with a 2023 patch and no recorded session was simply false. Rows that really
/// are since-you-played carry the Flare dot; the rest are history.</para>
/// </summary>
public sealed class UpdateEventViewModel
{
    private UpdateEventViewModel(
        string headline,
        string dateText,
        DateTime occurredAtUtc,
        GameLink? link,
        bool isAnnouncement,
        bool isSinceYouPlayed)
    {
        Headline = headline;
        DateText = dateText;
        OccurredAtUtc = occurredAtUtc;
        Link = link;
        IsAnnouncement = isAnnouncement;
        IsSinceYouPlayed = isSinceYouPlayed;
    }

    /// <summary>The announcement's own title, or a plain description of a build push.</summary>
    public string Headline { get; }

    /// <summary>Plex Mono, tabular — every date in the app (§3).</summary>
    public string DateText { get; }

    /// <summary>When it landed, UTC. Drives the mark's position on the gap rail.</summary>
    public DateTime OccurredAtUtc { get; }

    /// <summary>
    /// Patch-notes page, validated. Null for build pushes (no reader-facing
    /// page) and for anything whose stored URL is not http(s).
    /// </summary>
    public GameLink? Link { get; }

    public bool HasLink => Link is not null;

    /// <summary>Announcements are the ones with words in them; build pushes are the corroboration.</summary>
    public bool IsAnnouncement { get; }

    /// <summary>
    /// Landed after the user's last recorded session — the fact the Flare dot
    /// marks, here and nowhere else in this view. False when there is no last-
    /// played date at all: a game you have never opened has nothing to be
    /// behind on (§5.2).
    /// </summary>
    public bool IsSinceYouPlayed { get; }

    public static UpdateEventViewModel Create(UpdateEvent updateEvent, DateTime? lastPlayedUtc = null)
    {
        var isAnnouncement = updateEvent.Kind == UpdateEventKinds.Announcement;

        // A build push carries a build id and nothing readable, so it is named
        // for what it is rather than dressed up as patch notes it does not have.
        var headline = string.IsNullOrWhiteSpace(updateEvent.Title)
            ? updateEvent.BuildId is { Length: > 0 } build
                ? $"Build {build}"
                : isAnnouncement ? "Announcement" : "Build pushed"
            : updateEvent.Title!;

        var occurred = AsUtc(updateEvent.OccurredAt);

        return new UpdateEventViewModel(
            headline,
            LocalDateText(updateEvent.OccurredAt),
            occurred,
            // §7's copy: the row's link is named for what is on the other side
            // of it. GameLink refuses anything that is not http(s).
            GameLink.Create("Patch notes", updateEvent.Url, updateEvent.Url),
            isAnnouncement,
            lastPlayedUtc is { } played && occurred > AsUtc(played));
    }

    /// <summary>
    /// Timestamps come back from SQLite as TEXT and therefore as
    /// <see cref="DateTimeKind.Unspecified"/>. They are UTC by the storage
    /// contract, so say so before comparing or converting — otherwise
    /// <c>ToLocalTime</c> treats an already-local value as UTC and shifts every
    /// date by the offset.
    /// </summary>
    internal static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    internal static string LocalDateText(DateTime utc)
        => AsUtc(utc).ToLocalTime().ToString("d MMM yyyy");
}
