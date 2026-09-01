namespace Winnow.Core.Merging;

/// <summary>
/// How far a merge can go. A merge always unifies at the Work layer; it
/// collapses the two Releases into one only when they represent the same
/// edition. Skyrim SE is not Skyrim (section 6), so distinct editions stay two
/// releases under one work.
/// </summary>
public enum MergeMode
{
    /// <summary>No merge is possible or needed.</summary>
    NothingToDo,

    /// <summary>
    /// All releases of the absorbed work move to the surviving work; the two
    /// releases remain distinct rows. Used when the editions differ (platform,
    /// edition note, or IGDB version id disagree), when achievements exist on
    /// both sides, or when conflicting update events would be lost by collapsing.
    /// </summary>
    WorkOnly,

    /// <summary>
    /// Work unification plus release collapse: the absorbed release's external
    /// ids, ownerships and dependents are repointed onto the surviving release,
    /// and the absorbed release row is deleted. Only permitted when the two
    /// releases are the same edition of the same game.
    /// </summary>
    ReleaseCollapse,
}

/// <summary>
/// Storage strings for <see cref="MergeMode"/> values that appear in the
/// <c>merge_applications</c> table. <see cref="MergeMode.NothingToDo"/> has no
/// storage form because only an applied merge is recorded.
/// </summary>
public static class MergeModes
{
    public const string WorkOnly = "work_only";

    public const string ReleaseCollapse = "release_collapse";

    public static string ToStorage(MergeMode mode) => mode switch
    {
        MergeMode.WorkOnly => WorkOnly,
        MergeMode.ReleaseCollapse => ReleaseCollapse,
        _ => throw new ArgumentOutOfRangeException(
            nameof(mode), mode, "Only an applied merge has a stored mode."),
    };

    /// <summary>
    /// Reads a stored mode back. The CHECK on <c>merge_applications.mode</c>
    /// admits only these two values, so anything else is a row written outside
    /// the schema.
    /// </summary>
    public static MergeMode FromStorage(string mode) => mode switch
    {
        WorkOnly => MergeMode.WorkOnly,
        ReleaseCollapse => MergeMode.ReleaseCollapse,
        _ => throw new ArgumentOutOfRangeException(
            nameof(mode), mode, "Not a stored merge mode."),
    };
}
