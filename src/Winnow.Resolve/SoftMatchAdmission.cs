using Winnow.Resolve.Matching;

namespace Winnow.Resolve;

/// <summary>
/// What the library looks like to soft matching right now: which releases are
/// eligible to be compared at all, which work each belongs to, and the blocking
/// keys each is indexed under.
///
/// <para>This exists so <see cref="SoftMatchResolver"/> can answer a question the
/// proposal list alone cannot: <i>could this pending pair still be proposed
/// today?</i> A sweep only ever submits the pairs its current blocking pass
/// produced, so without this a pending row whose release was reclassified as a
/// non-game, renamed out of its old blocking key, or folded into the other
/// side's work is never submitted for withdrawal again — and sits in the review
/// queue forever, unanswerable except by answering it.</para>
///
/// <para>Built by <see cref="LibrarySoftMatchSweep"/>, which owns the admission
/// rules; the resolver only reads it.</para>
/// </summary>
public sealed class SoftMatchAdmission
{
    private readonly Dictionary<long, Member> _members;

    /// <summary>An admission covering nothing — every pending pair is unproposable.</summary>
    public static SoftMatchAdmission Empty { get; } = new([]);

    private SoftMatchAdmission(Dictionary<long, Member> members) => _members = members;

    /// <summary>Releases admitted to matching.</summary>
    public int Count => _members.Count;

    /// <summary>Starts an admission that <see cref="Builder.Add"/> fills in.</summary>
    public static Builder CreateBuilder(int capacity = 0) => new(capacity);

    /// <summary>True when this release is currently eligible to be compared.</summary>
    public bool IsAdmitted(long releaseId) => _members.ContainsKey(releaseId);

    /// <summary>The subject the matcher would score this release as, or null when it is not admitted.</summary>
    public MatchSubject? Subject(long releaseId)
        => _members.TryGetValue(releaseId, out var member) ? member.Subject : null;

    /// <summary>
    /// True when a fresh sweep could still put this pair in front of the user:
    /// both sides admitted, belonging to different works, and sharing at least
    /// one blocking key. It deliberately says nothing about the score — that is
    /// the matcher's job, and the caller applies it separately.
    /// </summary>
    public bool CouldPropose(long leftReleaseId, long rightReleaseId)
    {
        if (leftReleaseId == rightReleaseId
            || !_members.TryGetValue(leftReleaseId, out var left)
            || !_members.TryGetValue(rightReleaseId, out var right))
        {
            return false;
        }

        // Two releases of one work are already correctly modelled as separate
        // (§9 pitfall 5); offering to merge them is offering to collapse
        // Release into Work. Blocking never emits such a pair, so a pending one
        // can only be a leftover from before the works were joined.
        if (left.WorkId == right.WorkId)
        {
            return false;
        }

        // Blocking is not an optimisation here, it is the definition of
        // "proposable": a pair sharing no key is a pair no sweep will ever
        // generate again, however similar the titles once looked.
        foreach (var key in left.BlockingKeys)
        {
            if (right.BlockingKeys.Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct Member(
        MatchSubject Subject, long WorkId, IReadOnlySet<string> BlockingKeys);

    /// <summary>Accumulates admitted releases. Not thread-safe; one sweep builds one.</summary>
    public sealed class Builder
    {
        private readonly Dictionary<long, Member> _members;

        internal Builder(int capacity) => _members = new Dictionary<long, Member>(capacity);

        public void Add(MatchSubject subject, long workId, IReadOnlySet<string> blockingKeys)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(blockingKeys);

            _members[subject.ReleaseId] = new Member(subject, workId, blockingKeys);
        }

        public SoftMatchAdmission Build() => new(_members);
    }
}
