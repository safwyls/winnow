namespace Winnow.Core.Identity;

/// <summary>
/// The same-game half of an identity resolution. <see cref="Resolve"/> is TOTAL:
/// every work resolves, to its parent or to itself. It is the function a count,
/// a playtime, a bucket or a recommendation calls. A separate type from
/// <see cref="ExpansionGrouping"/> so a caller cannot accidentally fold an
/// expansion into an identity; the separation is enforced at the type level.
/// </summary>
public sealed class SameGameResolution
{
    private readonly Dictionary<long, long> _parentOf;
    private readonly Dictionary<long, long[]> _childrenOf;

    internal SameGameResolution(Dictionary<long, long> parentOf, Dictionary<long, long[]> childrenOf)
    {
        _parentOf = parentOf;
        _childrenOf = childrenOf;
    }

    /// <summary>A resolution with no links.</summary>
    public static SameGameResolution Empty { get; } = new([], []);

    /// <summary>True when no same-game links exist.</summary>
    public bool IsEmpty => _parentOf.Count == 0;

    /// <summary>How many works are children (linked under a parent).</summary>
    public int LinkedWorkCount => _parentOf.Count;

    /// <summary>
    /// Returns the canonical work id for <paramref name="workId"/>: its parent
    /// if linked, or itself. Total, so every work always resolves.
    /// </summary>
    public long Resolve(long workId)
        => _parentOf.TryGetValue(workId, out var parent) ? parent : workId;

    /// <summary>True when <paramref name="workId"/> is linked as a child.</summary>
    public bool IsChild(long workId) => _parentOf.ContainsKey(workId);

    /// <summary>True when <paramref name="workId"/> has at least one child linked to it.</summary>
    public bool IsParent(long workId) => _childrenOf.ContainsKey(workId);

    /// <summary>The children of <paramref name="parentWorkId"/>, sorted by work id. Empty if none.</summary>
    public IReadOnlyList<long> ChildrenOf(long parentWorkId)
        => _childrenOf.TryGetValue(parentWorkId, out var children) ? children : [];

    /// <summary>
    /// The whole identity group: parent first, then children sorted by work id.
    /// For an unlinked work, returns a single-element list containing itself.
    /// </summary>
    public IReadOnlyList<long> GroupOf(long workId)
    {
        var parent = Resolve(workId);
        var children = ChildrenOf(parent);
        if (children.Count == 0)
        {
            return [parent];
        }

        var group = new long[children.Count + 1];
        group[0] = parent;
        for (var i = 0; i < children.Count; i++)
        {
            group[i + 1] = children[i];
        }

        return group;
    }
}

/// <summary>
/// The expansion half of an identity resolution. Has NO <c>Resolve</c> method:
/// <see cref="BaseOf"/> returns null for a work that is not an expansion, so it
/// cannot be dropped into a position that expects a total resolver. This is
/// deliberate, because an expansion is a separate product whose playtime must
/// not roll up into its base.
/// </summary>
public sealed class ExpansionGrouping
{
    private readonly Dictionary<long, long> _baseOf;
    private readonly Dictionary<long, long[]> _expansionsOf;

    internal ExpansionGrouping(Dictionary<long, long> baseOf, Dictionary<long, long[]> expansionsOf)
    {
        _baseOf = baseOf;
        _expansionsOf = expansionsOf;
    }

    /// <summary>A grouping with no expansion links.</summary>
    public static ExpansionGrouping Empty { get; } = new([], []);

    /// <summary>True when no expansion links exist.</summary>
    public bool IsEmpty => _baseOf.Count == 0;

    /// <summary>How many works are grouped as expansions.</summary>
    public int GroupedWorkCount => _baseOf.Count;

    /// <summary>The base game of <paramref name="workId"/>, or null if it is not an expansion.</summary>
    public long? BaseOf(long workId)
        => _baseOf.TryGetValue(workId, out var baseWorkId) ? baseWorkId : null;

    /// <summary>True when <paramref name="workId"/> is linked as an expansion of another work.</summary>
    public bool IsExpansion(long workId) => _baseOf.ContainsKey(workId);

    /// <summary>True when <paramref name="workId"/> has at least one expansion linked to it.</summary>
    public bool HasExpansions(long workId) => _expansionsOf.ContainsKey(workId);

    /// <summary>The expansions of <paramref name="baseWorkId"/>, sorted by work id. Empty if none.</summary>
    public IReadOnlyList<long> ExpansionsOf(long baseWorkId)
        => _expansionsOf.TryGetValue(baseWorkId, out var expansions) ? expansions : [];
}

/// <summary>
/// Immutable snapshot of every live identity link, built from one query.
/// Splits into <see cref="SameGame"/> and <see cref="Expansions"/> so the two
/// kinds cannot be confused at the call site. The same-game resolver and the
/// expansion grouper are SEPARATE TYPES; a caller cannot accidentally fold an
/// expansion into an identity because the types do not have interchangeable
/// signatures.
/// </summary>
public sealed class IdentityResolution
{
    private IdentityResolution(SameGameResolution sameGame, ExpansionGrouping expansions)
    {
        SameGame = sameGame;
        Expansions = expansions;
    }

    /// <summary>An empty resolution, for the common case where no links exist.</summary>
    public static IdentityResolution Empty { get; } =
        new(SameGameResolution.Empty, ExpansionGrouping.Empty);

    /// <summary>Same-game links. <see cref="SameGameResolution.Resolve"/> is total.</summary>
    public SameGameResolution SameGame { get; }

    /// <summary>Expansion links. <see cref="ExpansionGrouping.BaseOf"/> returns null for non-expansions.</summary>
    public ExpansionGrouping Expansions { get; }

    /// <summary>
    /// Builds a resolution from live links. Throws on a retracted link (a
    /// resolution is built from live rows only) and on a second live parent
    /// for one child, with a message noting that <c>ux_identity_links_live</c>
    /// makes the latter impossible in the database. The check exists so a
    /// hand-built resolution in a test cannot express a state the database
    /// refuses.
    /// </summary>
    public static IdentityResolution FromLiveLinks(IEnumerable<IdentityLink> liveLinks)
    {
        ArgumentNullException.ThrowIfNull(liveLinks);

        var sameGameParent = new Dictionary<long, long>();
        var sameGameChildren = new Dictionary<long, List<long>>();
        var expansionBase = new Dictionary<long, long>();
        var expansionChildren = new Dictionary<long, List<long>>();

        foreach (var link in liveLinks)
        {
            if (!link.IsLive)
            {
                throw new ArgumentException(
                    $"Link {link.Id} is retracted and cannot take part in a resolution.",
                    nameof(liveLinks));
            }

            var (parents, children) = link.Kind switch
            {
                IdentityLinkKinds.SameGame => (sameGameParent, sameGameChildren),
                IdentityLinkKinds.ExpansionOf => (expansionBase, expansionChildren),
                _ => throw new ArgumentException(
                    $"Link {link.Id} has unknown kind '{link.Kind}'.", nameof(liveLinks)),
            };

            if (!parents.TryAdd(link.ChildWorkId, link.ParentWorkId))
            {
                throw new ArgumentException(
                    $"Work {link.ChildWorkId} has two live parents, which "
                    + "ux_identity_links_live makes impossible in the database.",
                    nameof(liveLinks));
            }

            if (!children.TryGetValue(link.ParentWorkId, out var list))
            {
                list = [];
                children[link.ParentWorkId] = list;
            }

            list.Add(link.ChildWorkId);
        }

        return new IdentityResolution(
            new SameGameResolution(sameGameParent, Freeze(sameGameChildren)),
            new ExpansionGrouping(expansionBase, Freeze(expansionChildren)));
    }

    private static Dictionary<long, long[]> Freeze(Dictionary<long, List<long>> source)
    {
        var frozen = new Dictionary<long, long[]>(source.Count);
        foreach (var (parent, children) in source)
        {
            children.Sort();
            frozen[parent] = [.. children];
        }

        return frozen;
    }
}
