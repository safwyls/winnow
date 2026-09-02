using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Resolve;

/// <summary>
/// One work the scan is willing to talk about, with the store entries hanging
/// off it so a card can draw covers and entry numbers without a second read.
/// </summary>
/// <param name="WorkId">The resolved work id, so a same-game group appears once.</param>
/// <param name="Title">The title the library shows for it.</param>
/// <param name="ReleaseIds">Every store entry under this work, ascending.</param>
public sealed record ExpansionCandidateWork(
    long WorkId, string Title, IReadOnlyList<long> ReleaseIds);

/// <summary>One proposed expansion, with the evidence for it.</summary>
/// <param name="Work">The proposed expansion.</param>
/// <param name="Evidence">What the detector observed about this pair.</param>
public sealed record ExpansionProposalMember(
    ExpansionCandidateWork Work, ExpansionEvidence Evidence);

/// <summary>
/// One base game and every expansion proposed under it: one card, one act. The
/// one-to-many relation presented once, rather than six pairwise questions each
/// invalidating the next.
/// </summary>
/// <param name="Base">The base game the members extend.</param>
/// <param name="Members">The proposed expansions, in the order the detector produced them.</param>
public sealed record ExpansionProposalGroup(
    ExpansionCandidateWork Base, IReadOnlyList<ExpansionProposalMember> Members);

/// <summary>What one scan looked at and produced.</summary>
/// <param name="Works">How many works were compared.</param>
/// <param name="Excluded">How many rows were dropped before comparing: non-games, placeholder names, works with no usable row.</param>
/// <param name="Groups">One entry per base game with at least one proposal, base work id ascending.</param>
/// <param name="Elapsed">How long the pass took. Logged, so a slow library shows up as a number.</param>
public sealed record ExpansionScanReport(
    int Works, int Excluded, IReadOnlyList<ExpansionProposalGroup> Groups, TimeSpan Elapsed)
{
    /// <summary>The report for a library with nothing in it.</summary>
    public static ExpansionScanReport Empty { get; } = new(0, 0, [], TimeSpan.Zero);
}

/// <summary>
/// Finds base-game-and-expansion candidates in the library.
///
/// <para>READ ONLY. It writes nothing, applies nothing and auto-groups
/// nothing; it produces questions. The affirmative answer is an
/// <c>identity_links</c> row at kind <c>expansion_of</c>, written by the
/// screen when the user says so, and the negative answer is a row in
/// <c>expansion_refusals</c>.</para>
///
/// <para>The proposals are DERIVED on every pass rather than stored, for the
/// same reason §6.1's buckets are queries: the detector's guards will be
/// tuned, and a stored proposal computed under an older rule rots. Only the
/// two answers are stored.</para>
///
/// <para>Population note. Epic and GOG DLC never reach the database —
/// <c>EpicLibrarySource</c> skips entries with a non-empty
/// <c>mainGameItem.id</c> and <c>GogLibrarySource</c> skips entries whose
/// <c>GameId</c> differs from their <c>RootGameId</c> — and Steam DLC
/// generally has no separate library entry. So the real population is Steam
/// appids Valve types as games whose titles extend another owned title, which
/// is exactly the Civilization IV case. Nothing here is a general DLC
/// subsystem, and it is not sized like one.</para>
/// </summary>
public sealed class LibraryExpansionScan
{
    private readonly IReleaseRepository _releases;
    private readonly IIdentityLinkRepository _links;
    private readonly IExpansionRefusalRepository _refusals;
    private readonly ExpansionDetectorOptions _options;
    private readonly ILogger<LibraryExpansionScan> _logger;

    /// <summary>Creates the scan.</summary>
    /// <param name="releases">Supplies the release identities every pass compares.</param>
    /// <param name="links">Supplies the live link map, which is how an answered question is recognised.</param>
    /// <param name="refusals">Supplies the stored no answers.</param>
    /// <param name="options">Detector tuning, or null for the shipped guards.</param>
    /// <param name="logger">Optional; the scan logs one line per pass.</param>
    public LibraryExpansionScan(
        IReleaseRepository releases,
        IIdentityLinkRepository links,
        IExpansionRefusalRepository refusals,
        ExpansionDetectorOptions? options = null,
        ILogger<LibraryExpansionScan>? logger = null)
    {
        _releases = releases;
        _links = links;
        _refusals = refusals;
        _options = options ?? ExpansionDetectorOptions.Default;
        _logger = logger ?? NullLogger<LibraryExpansionScan>.Instance;
    }

    /// <summary>
    /// One pass over the library. Returns the groups a card list would draw,
    /// base work id ascending, with every already-answered pair dropped.
    /// </summary>
    /// <param name="ct">Cancellation, checked once per proposal.</param>
    public async Task<ExpansionScanReport> ScanAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var identities = await _releases.GetIdentitiesAsync(ct);
        if (identities.Count == 0)
        {
            return ExpansionScanReport.Empty;
        }

        var resolution = await _links.GetResolutionAsync(ct);
        var refusals = await _refusals.GetAllAsync(ct);

        var works = BuildWorks(identities, resolution.SameGame, out var excluded);

        var subjects = new List<ExpansionSubject>(works.Count);
        foreach (var work in works.Values)
        {
            subjects.Add(work.Subject);
        }

        var refused = new HashSet<(long Base, long Child)>();
        foreach (var refusal in refusals)
        {
            refused.Add((refusal.BaseWorkId, refusal.ChildWorkId));
        }

        var groups = new Dictionary<long, List<ExpansionProposalMember>>();
        var order = new List<long>();

        foreach (var proposal in ExpansionDetector.Detect(subjects, _options))
        {
            ct.ThrowIfCancellationRequested();

            if (!Admits(proposal, resolution, refused))
            {
                continue;
            }

            if (!groups.TryGetValue(proposal.BaseWorkId, out var members))
            {
                groups[proposal.BaseWorkId] = members = [];
                order.Add(proposal.BaseWorkId);
            }

            members.Add(new ExpansionProposalMember(
                works[proposal.ChildWorkId].Card, proposal.Evidence));
        }

        order.Sort();

        var cards = new List<ExpansionProposalGroup>(order.Count);
        foreach (var baseWorkId in order)
        {
            cards.Add(new ExpansionProposalGroup(
                works[baseWorkId].Card, groups[baseWorkId]));
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Expansion scan: {Works} works ({Excluded} excluded), {Groups} base games with "
            + "{Members} proposed expansions, in {Elapsed:n1}s.",
            works.Count, excluded, cards.Count, cards.Sum(c => c.Members.Count),
            stopwatch.Elapsed.TotalSeconds);

        return new ExpansionScanReport(works.Count, excluded, cards, stopwatch.Elapsed);
    }

    /// <summary>
    /// Everything that would make a proposal a question already answered, or a
    /// question the depth-one rule cannot accept. Kept separate from the
    /// detector, which knows about titles and nothing about links.
    /// </summary>
    private static bool Admits(
        ExpansionProposal proposal,
        IdentityResolution resolution,
        HashSet<(long Base, long Child)> refused)
    {
        if (refused.Contains((proposal.BaseWorkId, proposal.ChildWorkId)))
        {
            return false;
        }

        // Already grouped, or grouped the other way round, or held as one game
        // with its base. All three are answers, and the queue must never ask an
        // answered question — the complaint that opened TASK-70.
        if (resolution.Expansions.BaseOf(proposal.ChildWorkId) is not null
            || resolution.Expansions.BaseOf(proposal.BaseWorkId) == proposal.ChildWorkId)
        {
            return false;
        }

        // Depth one, half one: a work that already has a live parent of any
        // kind cannot take a second one, and ux_identity_links_live would
        // refuse the write. Asking would produce a card whose answer throws.
        if (resolution.SameGame.IsChild(proposal.ChildWorkId))
        {
            return false;
        }

        // Depth one, half two: grouping under a base that is itself a child
        // would re-parent the whole group under its grandparent, which is a
        // decision nobody made. The base is already resolved, so this is a
        // belt-and-braces guard rather than an expected case.
        if (resolution.SameGame.IsChild(proposal.BaseWorkId))
        {
            return false;
        }

        // A work that is a same-game PARENT is a fine base. A work that is a
        // same-game parent cannot be a child, so it is not a fine expansion:
        // linking it would displace its own children onto the base.
        return !resolution.SameGame.IsParent(proposal.ChildWorkId);
    }

    /// <summary>
    /// Folds release rows into works, at the RESOLVED work id, dropping the
    /// rows there is no point comparing. The admission rules are the sweep's
    /// exactly — a non-game and a placeholder name are as useless here as they
    /// are to soft matching.
    /// </summary>
    private static Dictionary<long, Candidate> BuildWorks(
        IReadOnlyList<ReleaseIdentity> identities,
        SameGameResolution resolution,
        out int excluded)
    {
        var works = new Dictionary<long, Candidate>();
        excluded = 0;

        foreach (var identity in identities)
        {
            if (identity.IsNonGame || identity.NameIsProvisional)
            {
                excluded++;
                continue;
            }

            var workId = resolution.Resolve(identity.WorkId);

            if (!works.TryGetValue(workId, out var candidate))
            {
                works[workId] = candidate = new Candidate(workId);
            }

            candidate.Add(identity, isOwnRow: identity.WorkId == workId);
        }

        var built = new Dictionary<long, Candidate>(works.Count);
        foreach (var (workId, candidate) in works)
        {
            if (candidate.Build())
            {
                built[workId] = candidate;
            }
            else
            {
                excluded++;
            }
        }

        return built;
    }

    /// <summary>
    /// One work under construction. The title, year and publisher come from
    /// the work's OWN row where it has one: a same-game group is known by the
    /// title the user chose to keep, and taking a linked child's title here
    /// would compare a name the library does not show.
    /// </summary>
    private sealed class Candidate
    {
        private readonly List<long> _releaseIds = [];
        private ReleaseIdentity? _own;
        private ReleaseIdentity? _any;

        public Candidate(long workId) => WorkId = workId;

        public long WorkId { get; }

        public ExpansionSubject Subject { get; private set; } = null!;

        public ExpansionCandidateWork Card { get; private set; } = null!;

        public void Add(ReleaseIdentity identity, bool isOwnRow)
        {
            _releaseIds.Add(identity.ReleaseId);

            if (isOwnRow)
            {
                _own ??= identity;
            }

            _any ??= identity;
        }

        public bool Build()
        {
            var source = _own ?? _any;
            if (source is null)
            {
                return false;
            }

            _releaseIds.Sort();

            var title = string.IsNullOrWhiteSpace(source.WorkName)
                ? source.MatchTitle
                : source.WorkName;

            Subject = new ExpansionSubject
            {
                WorkId = WorkId,
                Title = title,
                ReleaseYear = source.FirstReleaseYear,
                Publisher = source.Publisher,
            };

            Card = new ExpansionCandidateWork(WorkId, title, _releaseIds);
            return true;
        }
    }
}
