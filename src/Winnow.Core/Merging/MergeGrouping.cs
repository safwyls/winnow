using Winnow.Core.Identity;

namespace Winnow.Core.Merging;

/// <summary>
/// One pending soft-match pair as the grouper reads it: the row id it will
/// write an answer to, the two releases it names, and the two numbers the queue
/// sorts and bands by. Release ids, not work ids, because that is what
/// <c>merge_candidates</c> stores; the grouper resolves them itself.
/// </summary>
public sealed record MergeGroupProposal
{
    /// <summary>The <c>merge_candidates.id</c> an answer writes to.</summary>
    public required long CandidateId { get; init; }

    /// <summary>Left release of the stored pair.</summary>
    public required long LeftReleaseId { get; init; }

    /// <summary>Right release of the stored pair.</summary>
    public required long RightReleaseId { get; init; }

    /// <summary>The matcher's confidence in [0,1].</summary>
    public required double Score { get; init; }

    /// <summary>
    /// The matcher put this pair in its top band. Used only to decide what may
    /// arrive already checked; it is never a merge recommendation.
    /// </summary>
    public bool IsPriority { get; init; }
}

/// <summary>
/// One surviving edge of a group, with both endpoints already resolved to
/// works. An edge whose endpoints resolved to one work is not here: the
/// question it asked has been answered.
/// </summary>
public sealed record MergeGroupEdge
{
    /// <summary>The <c>merge_candidates.id</c> this edge came from.</summary>
    public required long CandidateId { get; init; }

    /// <summary>The resolved work behind <see cref="LeftReleaseId"/>.</summary>
    public required long LeftWorkId { get; init; }

    /// <summary>The resolved work behind <see cref="RightReleaseId"/>.</summary>
    public required long RightWorkId { get; init; }

    /// <summary>Left release of the stored pair.</summary>
    public required long LeftReleaseId { get; init; }

    /// <summary>Right release of the stored pair.</summary>
    public required long RightReleaseId { get; init; }

    /// <summary>The matcher's confidence in [0,1].</summary>
    public required double Score { get; init; }

    /// <summary>True when the matcher put the pair in its top band.</summary>
    public bool IsPriority { get; init; }

    /// <summary>True when this edge runs between <paramref name="a"/> and <paramref name="b"/>, either way round.</summary>
    public bool Joins(long a, long b)
        => (LeftWorkId == a && RightWorkId == b) || (LeftWorkId == b && RightWorkId == a);

    /// <summary>The end of this edge that is not <paramref name="workId"/>, or null when it touches neither end.</summary>
    public long? Other(long workId)
        => LeftWorkId == workId ? RightWorkId
            : RightWorkId == workId ? LeftWorkId
            : null;
}

/// <summary>
/// One member of a group: a resolved work and the store entries hanging off it.
/// A member is a WORK, not a release, so two pending pairs naming the same work
/// are one member rather than two, and a work already carrying several store
/// entries shows as one row.
/// </summary>
public sealed record MergeGroupMember
{
    /// <summary>The resolved work this member is.</summary>
    public required long WorkId { get; init; }

    /// <summary>
    /// The releases the group's pairs named under this work, ascending. Only
    /// the ones the queue asked about; a work may own others.
    /// </summary>
    public required IReadOnlyList<long> ReleaseIds { get; init; }

    /// <summary>Strongest edge this member has to any other member.</summary>
    public required double BestScore { get; init; }

    /// <summary>
    /// True when the grouper would check this member by default: it has a
    /// direct top-band edge to every member already included. Never true on
    /// transitive membership alone.
    /// </summary>
    public bool IsDefaultIncluded { get; init; }
}

/// <summary>
/// One connected component of the pending pairs, which is one card. The unit of
/// the queue is a group and never a pair, so answering a member cannot make a
/// sibling stale: they were never separate cards.
/// </summary>
public sealed record MergeGroup
{
    /// <summary>Members, primary first, then by work id ascending.</summary>
    public required IReadOnlyList<MergeGroupMember> Members { get; init; }

    /// <summary>Every surviving edge inside this component.</summary>
    public required IReadOnlyList<MergeGroupEdge> Edges { get; init; }

    /// <summary>The work the ladder proposes as the title the library keeps.</summary>
    public required long PrimaryWorkId { get; init; }

    /// <summary>Which rung of the ladder decided <see cref="PrimaryWorkId"/>.</summary>
    public required MergeSurvivorReason PrimaryReason { get; init; }

    /// <summary>Strongest edge in the component. The queue sorts on this.</summary>
    public required double Score { get; init; }

    /// <summary>True when the strongest edge is in the matcher's top band.</summary>
    public bool IsPriority { get; init; }

    /// <summary>Two members is the ordinary case and keeps the pair layout.</summary>
    public bool IsPair => Members.Count == 2;
}

/// <summary>
/// Turns pending pairs into groups. Pure and BCL-only, so the whole shape of the
/// queue can be tested without a database.
///
/// <para>Three steps, in order. Resolve both ends of every pair through the live
/// link map. Drop every pair whose two ends resolve to one work, because that
/// question is answered. Take the connected components of what is left, and
/// return one group per component.</para>
/// </summary>
public static class MergeGrouping
{
    /// <summary>
    /// Builds the groups. <paramref name="workOfRelease"/> maps each release to
    /// the work it sits on; a release missing from it is skipped along with its
    /// pair, because a pair whose sides cannot be placed cannot be grouped.
    /// <paramref name="works"/> supplies the ladder's three facts per work; a
    /// work missing from it falls back to a bare candidate, which still sorts by
    /// id.
    /// </summary>
    public static IReadOnlyList<MergeGroup> Build(
        IEnumerable<MergeGroupProposal> proposals,
        IReadOnlyDictionary<long, long> workOfRelease,
        IReadOnlyDictionary<long, SurvivorCandidate> works,
        SameGameResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(workOfRelease);
        ArgumentNullException.ThrowIfNull(works);
        ArgumentNullException.ThrowIfNull(resolution);

        var edges = new List<MergeGroupEdge>();
        var releasesOfWork = new Dictionary<long, SortedSet<long>>();

        foreach (var proposal in proposals)
        {
            if (!workOfRelease.TryGetValue(proposal.LeftReleaseId, out var rawLeft)
                || !workOfRelease.TryGetValue(proposal.RightReleaseId, out var rawRight))
            {
                continue;
            }

            var left = resolution.Resolve(rawLeft);
            var right = resolution.Resolve(rawRight);

            // A pair whose two sides are already one game is not a question any
            // more. Dropping it here is what makes the BLOCKED card unreachable
            // and what stops an answered sibling leaving a stale card behind.
            if (left == right)
            {
                continue;
            }

            Remember(releasesOfWork, left, proposal.LeftReleaseId);
            Remember(releasesOfWork, right, proposal.RightReleaseId);

            edges.Add(new MergeGroupEdge
            {
                CandidateId = proposal.CandidateId,
                LeftWorkId = left,
                RightWorkId = right,
                LeftReleaseId = proposal.LeftReleaseId,
                RightReleaseId = proposal.RightReleaseId,
                Score = proposal.Score,
                IsPriority = proposal.IsPriority,
            });
        }

        return edges.Count == 0 ? [] : Assemble(edges, releasesOfWork, works);
    }

    /// <summary>
    /// Runs the ladder across a whole group. The rung order is a strict total
    /// order over works, so folding it pairwise picks the same winner whatever
    /// order the members arrive in. <paramref name="preferredWorkId"/> is the
    /// user's radio: null keeps the ladder; a value naming a member overrides
    /// every rung and reports <see cref="MergeSurvivorReason.ChosenByYou"/>; a
    /// value naming no member throws, so a stale choice can never link in a
    /// direction nobody asked for.
    /// </summary>
    public static SurvivorDecision ChoosePrimary(
        IReadOnlyList<SurvivorCandidate> candidates, long? preferredWorkId = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("A group needs at least one member.", nameof(candidates));
        }

        if (preferredWorkId is { } named)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.WorkId == named)
                {
                    return new SurvivorDecision(named, null, MergeSurvivorReason.ChosenByYou);
                }
            }

            throw new ArgumentOutOfRangeException(
                nameof(preferredWorkId), named, "That title is not one of this group.");
        }

        if (candidates.Count == 1)
        {
            return new SurvivorDecision(
                candidates[0].WorkId, null, MergeSurvivorReason.AlreadyOneGame);
        }

        var winner = candidates[0];
        for (var i = 1; i < candidates.Count; i++)
        {
            winner = Better(winner, candidates[i]);
        }

        // The rung that actually decided is the one separating the winner from
        // the best of the rest. Saying "Added first" out loud is the point: it
        // is the honest admission that nothing else discriminated.
        SurvivorCandidate? runnerUp = null;
        foreach (var candidate in candidates)
        {
            if (candidate.WorkId != winner.WorkId)
            {
                runnerUp = runnerUp is null ? candidate : Better(runnerUp, candidate);
            }
        }

        return SurvivorLadder.Choose(winner, runnerUp!);
    }

    private static SurvivorCandidate Better(SurvivorCandidate a, SurvivorCandidate b)
        => SurvivorLadder.Choose(a, b).SurvivingWorkId == a.WorkId ? a : b;

    private static void Remember(
        Dictionary<long, SortedSet<long>> releasesOfWork, long workId, long releaseId)
    {
        if (!releasesOfWork.TryGetValue(workId, out var releases))
        {
            releasesOfWork[workId] = releases = [];
        }

        releases.Add(releaseId);
    }

    private static List<MergeGroup> Assemble(
        List<MergeGroupEdge> edges,
        Dictionary<long, SortedSet<long>> releasesOfWork,
        IReadOnlyDictionary<long, SurvivorCandidate> works)
    {
        var componentOf = new Dictionary<long, int>();
        var components = new List<List<long>>();
        var adjacency = new Dictionary<long, List<long>>();

        foreach (var edge in edges)
        {
            Link(adjacency, edge.LeftWorkId, edge.RightWorkId);
            Link(adjacency, edge.RightWorkId, edge.LeftWorkId);
        }

        // Breadth-first over the resolved graph. Work ids are visited in
        // ascending order so the components, and therefore the cards, come out
        // the same on every load.
        var roots = new List<long>(adjacency.Keys);
        roots.Sort();

        foreach (var root in roots)
        {
            if (componentOf.ContainsKey(root))
            {
                continue;
            }

            var index = components.Count;
            var members = new List<long>();
            var queue = new Queue<long>();
            queue.Enqueue(root);
            componentOf[root] = index;

            while (queue.Count > 0)
            {
                var workId = queue.Dequeue();
                members.Add(workId);
                foreach (var neighbour in adjacency[workId])
                {
                    if (componentOf.TryAdd(neighbour, index))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }

            members.Sort();
            components.Add(members);
        }

        var edgesOf = new List<List<MergeGroupEdge>>(components.Count);
        for (var i = 0; i < components.Count; i++)
        {
            edgesOf.Add([]);
        }

        foreach (var edge in edges)
        {
            edgesOf[componentOf[edge.LeftWorkId]].Add(edge);
        }

        var groups = new List<MergeGroup>(components.Count);
        for (var i = 0; i < components.Count; i++)
        {
            groups.Add(Compose(components[i], edgesOf[i], releasesOfWork, works));
        }

        // Strongest first, then by the lowest member id, so the order is total
        // and does not shuffle between loads.
        groups.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0
                ? byScore
                : a.Members[0].WorkId.CompareTo(b.Members[0].WorkId);
        });

        return groups;
    }

    private static void Link(Dictionary<long, List<long>> adjacency, long from, long to)
    {
        if (!adjacency.TryGetValue(from, out var list))
        {
            adjacency[from] = list = [];
        }

        if (!list.Contains(to))
        {
            list.Add(to);
        }
    }

    private static MergeGroup Compose(
        List<long> workIds,
        List<MergeGroupEdge> edges,
        Dictionary<long, SortedSet<long>> releasesOfWork,
        IReadOnlyDictionary<long, SurvivorCandidate> works)
    {
        var candidates = new List<SurvivorCandidate>(workIds.Count);
        foreach (var workId in workIds)
        {
            candidates.Add(works.TryGetValue(workId, out var known)
                ? known
                : new SurvivorCandidate { WorkId = workId });
        }

        var decision = ChoosePrimary(candidates);

        var bestScore = new Dictionary<long, double>();
        var groupScore = double.MinValue;
        var priority = false;
        foreach (var edge in edges)
        {
            bestScore[edge.LeftWorkId] = Math.Max(
                bestScore.GetValueOrDefault(edge.LeftWorkId, double.MinValue), edge.Score);
            bestScore[edge.RightWorkId] = Math.Max(
                bestScore.GetValueOrDefault(edge.RightWorkId, double.MinValue), edge.Score);

            if (edge.Score > groupScore)
            {
                groupScore = edge.Score;
                priority = edge.IsPriority;
            }
        }

        var included = DefaultIncluded(workIds, edges, decision.SurvivingWorkId, bestScore);

        // Primary first, then by work id, because the primary is the title the
        // whole card is about and reading order should say so.
        var ordered = new List<long>(workIds.Count) { decision.SurvivingWorkId };
        foreach (var workId in workIds)
        {
            if (workId != decision.SurvivingWorkId)
            {
                ordered.Add(workId);
            }
        }

        var members = new List<MergeGroupMember>(ordered.Count);
        foreach (var workId in ordered)
        {
            members.Add(new MergeGroupMember
            {
                WorkId = workId,
                ReleaseIds = releasesOfWork.TryGetValue(workId, out var releases)
                    ? [.. releases]
                    : [],
                BestScore = bestScore.GetValueOrDefault(workId, 0.0),
                IsDefaultIncluded = included.Contains(workId),
            });
        }

        return new MergeGroup
        {
            Members = members,
            Edges = edges,
            PrimaryWorkId = decision.SurvivingWorkId,
            PrimaryReason = decision.Reason,
            Score = groupScore,
            IsPriority = priority,
        };
    }

    // A CLIQUE, not a component. A member arrives checked only when it has a
    // direct top-band edge to every member already checked, so Prey (2006) and
    // Prey (2017) cannot arrive pre-checked through a shared neighbour. A member
    // that reaches the group only through a sibling is shown with its evidence
    // and left unchecked for the user to decide.
    //
    // TWO MEMBERS ARE EXEMPT, and deliberately. The rule guards TRANSITIVITY:
    // it stops the closure asserting something no proposal asserted. A group of
    // two has no closure — the card asks exactly the question the proposal
    // asked — so gating it on the band would be reading the band as a merge
    // recommendation, which it is not. The band means "show the user this one
    // first" and nothing else, and the queue floor has already decided the pair
    // is worth asking about.
    private static HashSet<long> DefaultIncluded(
        List<long> workIds,
        List<MergeGroupEdge> edges,
        long primaryWorkId,
        Dictionary<long, double> bestScore)
    {
        var included = new HashSet<long> { primaryWorkId };

        if (workIds.Count == 2)
        {
            included.Add(workIds[0] == primaryWorkId ? workIds[1] : workIds[0]);
            return included;
        }

        var others = new List<long>(workIds.Count);
        foreach (var workId in workIds)
        {
            if (workId != primaryWorkId)
            {
                others.Add(workId);
            }
        }

        others.Sort((a, b) =>
        {
            var byScore = bestScore.GetValueOrDefault(b, 0.0)
                .CompareTo(bestScore.GetValueOrDefault(a, 0.0));
            return byScore != 0 ? byScore : a.CompareTo(b);
        });

        foreach (var workId in others)
        {
            var joinsAll = true;
            foreach (var member in included)
            {
                if (!HasPriorityEdge(edges, workId, member))
                {
                    joinsAll = false;
                    break;
                }
            }

            if (joinsAll)
            {
                included.Add(workId);
            }
        }

        return included;
    }

    private static bool HasPriorityEdge(List<MergeGroupEdge> edges, long a, long b)
    {
        foreach (var edge in edges)
        {
            if (edge.IsPriority && edge.Joins(a, b))
            {
                return true;
            }
        }

        return false;
    }
}
