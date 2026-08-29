using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Domain;

namespace Winnow.App.ViewModels;

/// <summary>
/// One update/patch row in the detail panel. Tracks two independent flags:
/// <see cref="IsSinceYouPlayed"/> (landed after last session, immutable) and
/// <see cref="IsAcknowledged"/> (user marked as read, migration 0012). The Flare
/// dot binds to <see cref="IsUnread"/> = since-you-played AND not acknowledged.
/// </summary>
public sealed partial class UpdateEventViewModel : ObservableObject
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
    /// Landed after the user's last recorded session — a fact about the gap,
    /// and an immutable one. False when there is no last-played date at all: a
    /// game you have never opened has nothing to be behind on (§5.2).
    ///
    /// <para>This is what the section heading and the rail's caption count.
    /// Dismissing the flag does not make an update stop having landed while you
    /// were away, so neither of those sentences changes when it is
    /// dismissed.</para>
    /// </summary>
    public bool IsSinceYouPlayed { get; }

    /// <summary>
    /// Whether the user has said they have read this one — set by
    /// <see cref="GameDetailsViewModel"/> from the release's standing
    /// acknowledgement watermark, and by nothing else. True for every row at or
    /// before that instant, which is the same comparison the bucket query's
    /// <c>major_update</c> CTE makes when it decides the dot is out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnread))]
    public partial bool IsAcknowledged { get; set; }

    /// <summary>
    /// What the Flare dot marks, here and nowhere else in this view: landed
    /// after your last session and not yet declared read.
    ///
    /// <para>§2 makes Flare the rarest colour in the interface and gives it one
    /// job. Leaving it on a row the user has just dismissed would be that one
    /// job quietly becoming two — "unread" and "recent" — which is exactly how
    /// the badge stops meaning anything.</para>
    /// </summary>
    public bool IsUnread => IsSinceYouPlayed && !IsAcknowledged;

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
