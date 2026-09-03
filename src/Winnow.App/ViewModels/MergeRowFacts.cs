namespace Winnow.App.ViewModels;

/// <summary>
/// What the library's own read model says about one entry, folded to the
/// grain of one work. Read once per load; nothing here is re-derived when an
/// answer is given.
/// </summary>
/// <param name="PlaytimeMinutes">Minutes across every store entry under the work.</param>
/// <param name="LastPlayedAt">The latest last-played across those entries, UTC, or null when never opened.</param>
/// <param name="HasUnread">True when any entry has been patched since it was last played.</param>
/// <param name="AcquiredAt">The earliest ownership date across the entries, UTC, or null when no store recorded one.</param>
/// <param name="Installed">True or false when every entry agrees, null when unknown or mixed.</param>
public sealed record MergeRowFacts(
    long PlaytimeMinutes,
    DateTime? LastPlayedAt,
    bool HasUnread,
    DateTime? AcquiredAt,
    bool? Installed)
{
    /// <summary>The facts for an entry the read model has nothing on.</summary>
    public static MergeRowFacts None { get; } = new(0, null, false, null, null);
}
