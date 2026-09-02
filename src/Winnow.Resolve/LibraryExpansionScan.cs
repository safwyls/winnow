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
    ExpansionCandidateWork Work, ExpansionEvidence Evidence)
{
    /// <summary>
    /// The kind the affirmative answer writes, one of
    /// <see cref="IdentityLinkKinds"/>. <c>expansion_of</c> counts as a title
    /// and does not roll up playtime (the user's decision of 2026-08-31).
    /// <c>variant_of</c> does not count as a title while its parent is owned,
    /// counts when it is the only thing owned, and never rolls up playtime,
    /// though the variant's own hours stay visible on the parent's modal.
    /// </summary>
    public string Kind { get; init; } = IdentityLinkKinds.ExpansionOf;

    /// <summary>
    /// The source's own word for the relation, one of
    /// <see cref="RelationLabels"/>, or null when nothing named it. A card
    /// showing "Demo" or "Remaster" or "Standalone expansion" reads this, not
    /// <see cref="Kind"/>. Three kinds exist (each defined by the numbers it
    /// changes, costing a table rebuild); labels are vocabulary and cost
    /// nothing. IGDB has fifteen type names today and will add more.
    /// </summary>
    public string? RelationLabel { get; init; }

    /// <summary>
    /// True when a storefront proposed this pair rather than the title
    /// heuristic. The distinction matters for confidence, not for authority:
    /// it is still a proposal the user may refuse.
    /// </summary>
    public bool FromMetadata { get; init; }
}

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
        ResolveClaims(works, identities, resolution.SameGame);

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
                works[proposal.ChildWorkId].Card, proposal.Evidence)
            {
                Kind = proposal.Kind,
                RelationLabel = proposal.RelationLabel,
                FromMetadata = proposal.FromMetadata,
            });
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

        // The same three answers again, at the variant_of kind. A demo already
        // grouped under the game it samples is an answered question, and
        // ux_identity_links_live would refuse a second parent for it anyway.
        if (resolution.Variants.ParentOf(proposal.ChildWorkId) is not null
            || resolution.Variants.ParentOf(proposal.BaseWorkId) == proposal.ChildWorkId)
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

        // The rows the library already hides must not be the rows the queue
        // offers. GetIdentitiesAsync returns every release as stored, and demo
        // consolidation runs inside the bucket query, so the scan used to ask
        // about "Civilization V: Demo", "Rust - Staging Branch" and nine more
        // that the grid has never once shown, under the word "expansion", which
        // is wrong twice over. Consolidation is defined over OWNED releases,
        // exactly as LibraryQueryRepository defines it: a base game the user
        // does not own cannot hide anything, and removing the base game brings
        // the demo straight back on the next pass.
        var suppressed = DemoConsolidation.Consolidate(
            identities
                .Where(static i => i.IsOwned)
                .Select(static i => new DemoConsolidationEntry
                {
                    ReleaseId = i.ReleaseId,
                    Title = i.MatchTitle,
                    NameIsProvisional = i.NameIsProvisional,
                    FirstReleaseYear = i.FirstReleaseYear,
                    SteamAppType = i.SteamAppType,
                }));

        foreach (var identity in identities)
        {
            if (identity.IsNonGame || identity.NameIsProvisional
                || suppressed.ContainsKey(identity.ReleaseId))
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
    /// Turns each work's stored storefront facts into a claim, and each
    /// claim's external parent reference into a work id.
    ///
    /// <para>The parent is stored as the storefront named it (a Steam appid or
    /// an IGDB game id) because that is the fact the source stated; resolving
    /// it to a work is a query rather than a stored column. A parent the
    /// library does not hold resolves to nothing, which is not a failure: the
    /// claim still refutes a heuristic guess that names a different owned work,
    /// it simply has no pair to propose.</para>
    ///
    /// <para>Storefront facts belong to a work's own row, so a same-game group
    /// takes them from whichever member carries them: linking a Steam entry to
    /// its Epic twin must not lose the Steam entry's parent pointer.</para>
    /// </summary>
    private static void ResolveClaims(
        Dictionary<long, Candidate> works,
        IReadOnlyList<ReleaseIdentity> identities,
        SameGameResolution resolution)
    {
        var byAppId = new Dictionary<string, long>(StringComparer.Ordinal);
        var byIgdbId = new Dictionary<long, long>();

        foreach (var identity in identities)
        {
            var workId = resolution.Resolve(identity.WorkId);

            if (identity.SteamAppId is { Length: > 0 } appId)
            {
                byAppId.TryAdd(appId, workId);
            }

            if (identity.IgdbId is { } igdbId and > 0)
            {
                byIgdbId.TryAdd(igdbId, workId);
            }
        }

        // The facts belong to the work's OWN row, so a same-game group takes
        // them from whichever member carries them: linking a Steam entry to its
        // Epic twin must not lose the Steam entry's parent pointer.
        var facts = new Dictionary<long, StorefrontFacts>();
        foreach (var identity in identities)
        {
            var workId = resolution.Resolve(identity.WorkId);
            if (identity.StorefrontFacts.IsEmpty || !works.ContainsKey(workId))
            {
                continue;
            }

            facts.TryAdd(workId, identity.StorefrontFacts);
        }

        foreach (var (workId, observed) in facts)
        {
            if (StorefrontRelation.Read(observed) is not { } claim)
            {
                continue;
            }

            long? parentWorkId = null;
            if (claim.SteamParentAppId is { } appId && byAppId.TryGetValue(appId, out var fromApp))
            {
                parentWorkId = fromApp;
            }
            else if (claim.IgdbParentId is { } igdbId && byIgdbId.TryGetValue(igdbId, out var fromIgdb))
            {
                parentWorkId = fromIgdb;
            }

            works[workId].Observe(claim, parentWorkId == workId ? null : parentWorkId);
        }
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

        /// <summary>Attaches the storefront's claim about this work, once the parent has been resolved.</summary>
        public void Observe(StorefrontClaim claim, long? parentWorkId)
            => Subject = Subject with { Claim = claim, ClaimedParentWorkId = parentWorkId };
    }
}
