using Hoard.Core.Domain;

namespace Hoard.App.ViewModels;

/// <summary>
/// One update the user was not around for, as the detail view lists it.
/// design-system.md §5.2: the unread badge exists so the user can go read what
/// changed, so every row that has a page is openable and every row that does
/// not says so by simply not offering a link.
/// </summary>
public sealed class UpdateEventViewModel
{
    private UpdateEventViewModel(string headline, string dateText, string? url, bool isAnnouncement)
    {
        Headline = headline;
        DateText = dateText;
        Url = url;
        IsAnnouncement = isAnnouncement;
    }

    /// <summary>The announcement's own title, or a plain description of a build push.</summary>
    public string Headline { get; }

    /// <summary>Plex Mono, tabular — every date in the app (§3).</summary>
    public string DateText { get; }

    /// <summary>Patch-notes page. Null for build pushes, which have no reader-facing page.</summary>
    public string? Url { get; }

    public bool HasUrl => Url is not null;

    /// <summary>Announcements are the ones with words in them; build pushes are the corroboration.</summary>
    public bool IsAnnouncement { get; }

    public static UpdateEventViewModel Create(UpdateEvent updateEvent)
    {
        var isAnnouncement = updateEvent.Kind == UpdateEventKinds.Announcement;

        // A build push carries a build id and nothing readable, so it is named
        // for what it is rather than dressed up as patch notes it does not have.
        var headline = string.IsNullOrWhiteSpace(updateEvent.Title)
            ? updateEvent.BuildId is { Length: > 0 } build
                ? $"Build {build}"
                : isAnnouncement ? "Announcement" : "Build pushed"
            : updateEvent.Title!;

        // Only http(s) links are offered. Anything else in that column is not
        // something to hand to the shell.
        var url = Uri.TryCreate(updateEvent.Url, UriKind.Absolute, out var uri)
                  && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;

        return new UpdateEventViewModel(
            headline,
            LocalDateText(updateEvent.OccurredAt),
            url,
            isAnnouncement);
    }

    /// <summary>
    /// Timestamps come back from SQLite as TEXT and therefore as
    /// <see cref="DateTimeKind.Unspecified"/>. They are UTC by the storage
    /// contract, so say so before converting — otherwise
    /// <c>ToLocalTime</c> treats an already-local value as UTC and shifts every
    /// date by the offset.
    /// </summary>
    internal static string LocalDateText(DateTime utc)
    {
        var stamped = utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            : utc;
        return stamped.ToLocalTime().ToString("d MMM yyyy");
    }
}
