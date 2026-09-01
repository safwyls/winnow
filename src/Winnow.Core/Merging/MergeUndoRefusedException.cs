namespace Winnow.Core.Merging;

/// <summary>
/// Thrown when an undo is asked for and cannot be performed faithfully. Refusing
/// is the whole point (a partial reversal is worse than none), so this is the
/// only outcome besides a complete undo. Carries the plan so the caller can say
/// which of the reasons applied without asking again.
/// </summary>
public sealed class MergeUndoRefusedException : InvalidOperationException
{
    public MergeUndoRefusedException(MergeUndoPlan plan, string message)
        : base(message)
        => Plan = plan;

    public MergeUndoRefusedException(string message)
        : base(message)
        => Plan = null;

    public MergeUndoRefusedException()
        : base("The merge cannot be undone.")
        => Plan = null;

    public MergeUndoRefusedException(string message, Exception innerException)
        : base(message, innerException)
        => Plan = null;

    public MergeUndoPlan? Plan { get; }

    public MergeUndoBlocker Blocker => Plan?.PrimaryBlocker ?? MergeUndoBlocker.None;
}
