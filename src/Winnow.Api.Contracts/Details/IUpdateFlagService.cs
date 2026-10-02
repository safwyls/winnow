using Winnow.Core.Domain;

namespace Winnow.App.Services;

/// <summary>Result of dismissing or restoring an unread-update flag. Three cases: stored, nothing to do, or not stored.</summary>
public enum UpdateFlagResult
{
    /// <summary>The write landed. For a dismissal, <see cref="UpdateFlagOutcome.AcknowledgedThrough"/> carries the watermark.</summary>
    Stored = 0,

    /// <summary>There was nothing to do (no correlated update to dismiss, or no standing acknowledgement to undo).</summary>
    NothingToDo = 1,

    /// <summary>The write did not land (no repository registered, or storing threw).</summary>
    NotStored = 2,
}

/// <summary>Outcome of a dismiss or restore operation.</summary>
/// <param name="Result">Which of the three things happened.</param>
/// <param name="AcknowledgedThrough">The stored watermark (the flagging build push's <c>occurred_at</c>). Null unless stored.</param>
public sealed record UpdateFlagOutcome(UpdateFlagResult Result, DateTime? AcknowledgedThrough)
{
    /// <summary>True only when storage actually took the write.</summary>
    public bool Saved => Result == UpdateFlagResult.Stored;

    /// <summary>The answer when there is nowhere to write, or when writing threw.</summary>
    public static UpdateFlagOutcome NotStored { get; } = new(UpdateFlagResult.NotStored, null);

    /// <summary>The answer when the release had nothing to dismiss or nothing to give back.</summary>
    public static UpdateFlagOutcome NothingToDo { get; } = new(UpdateFlagResult.NothingToDo, null);
}

/// <summary>
/// App-layer seam for update-flag acknowledgements (migration 0012). Computes
/// the watermark from the flagging build push's <c>occurred_at</c>, mirroring
/// <c>LibraryQueryRepository</c>'s <c>major_update</c> CTE. Never throws.
/// </summary>
public interface IUpdateFlagService
{
    /// <summary>
    /// Dismisses the update flag on <paramref name="releaseId"/> by recording a
    /// watermark from the events the user was actually shown (not re-read, to
    /// avoid acknowledging a push that arrived after the panel opened).
    /// </summary>
    Task<UpdateFlagOutcome> DismissAsync(
        long releaseId, IReadOnlyList<UpdateEvent> events, CancellationToken ct = default);

    /// <summary>Revokes the standing acknowledgement on this release so the flag reappears.</summary>
    Task<UpdateFlagOutcome> RestoreAsync(long releaseId, CancellationToken ct = default);

    /// <summary>Returns the standing acknowledgement watermark, or null if none exists or the read fails.</summary>
    Task<DateTime?> GetStandingAsync(long releaseId, CancellationToken ct = default);

    /// <summary>
    /// Returns the effective read-through instant for a watermark, extending it
    /// over correlated announcements within the correlation window. Null in, null out.
    /// </summary>
    DateTime? ReadThrough(
        long releaseId, IReadOnlyList<UpdateEvent> events, DateTime? acknowledgedThrough);
}
