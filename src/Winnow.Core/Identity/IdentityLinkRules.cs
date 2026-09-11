namespace Winnow.Core.Identity;

/// <summary>The structural admission shared by link commands and derived proposals.</summary>
public static class IdentityLinkRules
{
    /// <summary>
    /// Checks a proposed link against a live snapshot. A same-game command may
    /// move a child's existing group with it; expansions and variants may not.
    /// Existing membership of the child is replaced only by an explicit command.
    /// </summary>
    public static IdentityLinkRefusal GetRefusal(
        string kind, long parentWorkId, long childWorkId, IdentityResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        if (!IdentityLinkKinds.All.Contains(kind))
        {
            return IdentityLinkRefusal.UnknownKind;
        }

        if (parentWorkId == childWorkId)
        {
            return IdentityLinkRefusal.SelfLink;
        }

        if (resolution.IsChild(parentWorkId))
        {
            return IdentityLinkRefusal.ParentIsAlreadyAChild;
        }

        return kind != IdentityLinkKinds.SameGame && resolution.IsParent(childWorkId)
            ? IdentityLinkRefusal.ExpansionChildIsAlreadyAParent
            : IdentityLinkRefusal.None;
    }
}
