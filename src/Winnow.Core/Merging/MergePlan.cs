namespace Winnow.Core.Merging;

/// <summary>
/// The repository's verdict on a merge candidate: what mode it qualifies for,
/// which side survives, and what (if anything) blocks a collapse. A plan with
/// <see cref="MergeMode.NothingToDo"/> carries a <see cref="Blocker"/> that
/// explains why. The plan is a preview; it writes nothing until passed to
/// <see cref="Repositories.IMergeExecutionRepository.ApplyAsync"/>.
/// </summary>
public sealed record MergePlan
{
    public required long CandidateId { get; init; }

    public required MergeMode Mode { get; init; }

    public MergeBlocker Blocker { get; init; } = MergeBlocker.None;

    public long? LeftReleaseId { get; init; }

    public long? RightReleaseId { get; init; }

    public long? SurvivingWorkId { get; init; }

    public long? AbsorbedWorkId { get; init; }

    public long? SurvivingReleaseId { get; init; }

    public long? AbsorbedReleaseId { get; init; }

    public bool WillCollapseReleases => Mode == MergeMode.ReleaseCollapse;

    public bool WillUnifyWorks => Mode != MergeMode.NothingToDo && AbsorbedWorkId is not null;

    public static MergePlan Nothing(long candidateId, MergeBlocker blocker) => new()
    {
        CandidateId = candidateId,
        Mode = MergeMode.NothingToDo,
        Blocker = blocker,
    };
}
