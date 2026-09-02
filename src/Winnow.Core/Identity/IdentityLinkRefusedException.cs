namespace Winnow.Core.Identity;

/// <summary>
/// Structural reasons an identity link or retraction cannot be performed.
/// Unlike the destructive merge, a link destroys nothing, so what remains are
/// structural refusals only; 0016/0017's six blockers all existed because rows
/// were being destroyed.
/// </summary>
public enum IdentityLinkRefusal
{
    /// <summary>No refusal.</summary>
    None,

    /// <summary>A work cannot be linked to itself.</summary>
    SelfLink,

    /// <summary>The request named no children.</summary>
    NoChildren,

    /// <summary>One or more work ids do not exist in the database.</summary>
    UnknownWork,

    /// <summary>
    /// Depth one: the chosen parent is already a live child of another work.
    /// Refused rather than repaired, because re-parenting a whole group under
    /// its grandparent would be a decision nobody made.
    /// </summary>
    ParentIsAlreadyAChild,

    /// <summary>The kind string is not in <see cref="IdentityLinkKinds.All"/>.</summary>
    UnknownKind,

    /// <summary>The source string is not a recognized <see cref="IdentityLinkSources"/> value.</summary>
    UnknownSource,

    /// <summary>The act id passed to retraction does not exist.</summary>
    ActNotFound,

    /// <summary>
    /// An <c>expansion_of</c> act named a child that is itself a live parent.
    /// Refused rather than repaired.
    ///
    /// <para>Depth one normally re-parents the children of a work that is
    /// becoming a child, inside the same act. That is right for
    /// <c>same_game</c>, where it keeps one statement true. It is wrong here:
    /// grouping Beyond the Sword under Civilization IV, when Beyond the Sword
    /// already holds its GOG twin as one game, would move that twin's
    /// same-game link onto Civilization IV and fold its playtime in. An
    /// expansion link must move no number, so the act is refused and the user
    /// separates the entry first.</para>
    /// </summary>
    ExpansionChildIsAlreadyAParent,
}

/// <summary>
/// Thrown when an identity link or retraction is refused. Carries the
/// <see cref="IdentityLinkRefusal"/> so the caller can report which structural
/// rule was violated without asking again.
/// </summary>
public sealed class IdentityLinkRefusedException : InvalidOperationException
{
    /// <summary>Creates an exception with a specific refusal reason and message.</summary>
    public IdentityLinkRefusedException(IdentityLinkRefusal refusal, string message)
        : base(message) => Refusal = refusal;

    /// <summary>Creates an exception with <see cref="IdentityLinkRefusal.None"/> and a default message.</summary>
    public IdentityLinkRefusedException()
        : this(IdentityLinkRefusal.None, "The identity link was refused.")
    {
    }

    /// <summary>Creates an exception with <see cref="IdentityLinkRefusal.None"/> and a custom message.</summary>
    public IdentityLinkRefusedException(string message)
        : this(IdentityLinkRefusal.None, message)
    {
    }

    /// <summary>Creates an exception with <see cref="IdentityLinkRefusal.None"/> and an inner exception.</summary>
    public IdentityLinkRefusedException(string message, Exception innerException)
        : base(message, innerException) => Refusal = IdentityLinkRefusal.None;

    /// <summary>Which structural rule was violated.</summary>
    public IdentityLinkRefusal Refusal { get; }
}
