using System.Globalization;
using Winnow.Core.Matching;

namespace Winnow.Core.Identity;

/// <summary>
/// One work as the expansion detector sees it. Work id, the title to compare,
/// the year and the publisher. No IO, no release ids — an expansion link is
/// work to work.
/// </summary>
public sealed record ExpansionSubject
{
    /// <summary>The work being compared. Resolved, not a raw release's work id.</summary>
    public required long WorkId { get; init; }

    /// <summary>The title the library shows for this work, raw. Normalised here.</summary>
    public required string Title { get; init; }

    /// <summary>First release year, or null when enrichment has not reached this work.</summary>
    public int? ReleaseYear { get; init; }

    /// <summary>Publisher, or null when unknown. Unknown never vetoes; a known mismatch does.</summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// What the storefronts say about this work's relation to another, or null
    /// when every source is silent. Silence is the condition the title heuristic
    /// exists to fill; speech overrides it.
    /// </summary>
    public StorefrontClaim? Claim { get; init; }

    /// <summary>
    /// The work a storefront names as this one's parent, resolved to a work id,
    /// or null when no source named one or the named parent is not in the
    /// library. Resolution happens outside Core, which has no IO.
    /// </summary>
    public long? ClaimedParentWorkId { get; init; }

    /// <summary>
    /// True when at least one source has an opinion about this work's relations.
    /// A plain Steam type 0 with no parent is deliberately not an opinion: Steam
    /// is documented-silent on expansions, and reading it as speech would mute
    /// the heuristic over the entire Steam library.
    /// </summary>
    public bool MetadataSpeaks => Claim is not null;
}

/// <summary>
/// What the detector observed about one pair. Everything a card needs to say
/// why it is asking, and nothing it would have to recompute.
/// </summary>
/// <param name="BaseCore">The base's normalised core, which is the text the child's title had to start with.</param>
/// <param name="Suffix">The tokens the child adds past that prefix, space-joined. What the title extends by.</param>
/// <param name="PublisherAgrees">True when both publishers are known and equal, false when both are known and differ, null when either is unknown.</param>
/// <param name="YearDelta">Child year minus base year, or null when either year is unknown.</param>
/// <param name="HasSeparatorBoundary">True when the child's raw title splits at a colon, dash, pipe or slash whose left side normalises to exactly the base core. Every separator position is tried, not just the first.</param>
public sealed record ExpansionEvidence(
    string BaseCore,
    string Suffix,
    bool? PublisherAgrees,
    int? YearDelta,
    bool HasSeparatorBoundary);

/// <summary>
/// One proposal: this child's title extends this base's. Nothing that produces
/// a proposal applies it. It is a question for the user, and the user may
/// refuse it.
/// </summary>
public sealed record ExpansionProposal
{
    /// <summary>The work whose title the child extends.</summary>
    public required long BaseWorkId { get; init; }

    /// <summary>The work doing the extending. It stays its own title in the library.</summary>
    public required long ChildWorkId { get; init; }

    /// <summary>What the detector observed, for the card and for a test.</summary>
    public required ExpansionEvidence Evidence { get; init; }

    /// <summary>
    /// How many normalised tokens the base contributed. Longer is stronger, and
    /// it is what breaks a tie between two bases that both prefix one child.
    /// </summary>
    public required int PrefixTokenCount { get; init; }

    /// <summary>
    /// The kind the affirmative answer would write, one of
    /// <see cref="IdentityLinkKinds"/>. expansion_of unless the child's title
    /// carries a variant marker, in which case variant_of, so even the fallback
    /// path stops calling a demo an expansion.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// The word the card shows, one of <see cref="RelationLabels"/>, or null
    /// when nothing named the relation. A title-derived proposal can only ever
    /// name a variant word (demo, beta, playtest); every other label comes from
    /// a storefront.
    /// </summary>
    public string? RelationLabel { get; init; }

    /// <summary>
    /// True when a storefront made this proposal rather than the title
    /// heuristic. Metadata proposals may be shown with higher confidence; they
    /// are still proposals, because only the user's answer writes a link.
    /// </summary>
    public bool FromMetadata { get; init; }
}

/// <summary>
/// Why the detector refused a pair. Every value is a stated guard, not a score
/// that fell below a line, so a refusal can be named in a test and on a card.
/// </summary>
public enum ExpansionRefusalReason
{
    /// <summary>No refusal: the pair was proposed.</summary>
    None,

    /// <summary>The two subjects are the same work.</summary>
    SameWork,

    /// <summary>Normalisation left one of the titles with nothing comparable.</summary>
    EmptyTitle,

    /// <summary>
    /// The base's normalised core is shorter than
    /// <see cref="ExpansionDetectorOptions.MinBaseCoreLength"/>. A two-letter
    /// title prefixes half a library.
    /// </summary>
    BaseTooShort,

    /// <summary>
    /// The base's tokens are not a strict ordered prefix of the child's, so
    /// the child does not extend the base. This is also where an edition
    /// bundle lands: the normaliser lifts "Complete Edition" out of the core,
    /// so "Civilization IV: Complete Edition" reduces to the base's own token
    /// list and adds nothing to it.
    /// </summary>
    NotAPrefix,

    /// <summary>
    /// The suffix opens on a number, which names a different numbered entry in
    /// a series rather than an extension. "Portal" / "Portal 2" and
    /// "The Witcher" / "The Witcher 3: Wild Hunt" are refused here. This is the
    /// most dangerous false positive a prefix rule can produce.
    /// </summary>
    SequelOrdinal,

    /// <summary>
    /// The two titles disagree about a rebuild marker. A remaster is a separate
    /// build of the same game, not an extension of it (§9 pitfall 5).
    /// </summary>
    RebuildEdition,

    /// <summary>
    /// Both publishers are known and they differ. An unknown publisher never
    /// refuses; only a known disagreement does.
    /// </summary>
    PublisherMismatch,

    /// <summary>
    /// The child's year is more than a year before the base's. An expansion
    /// does not ship before the thing it expands. The year of slack is there
    /// because the two rows are enriched from different sources and a regional
    /// date can disagree by a year.
    /// </summary>
    ChildPredatesBase,

    /// <summary>
    /// The child's year is further past the base's than
    /// <see cref="ExpansionDetectorOptions.MaxYearGap"/> allows.
    /// </summary>
    YearGapTooWide,

    /// <summary>
    /// The prefix matched and nothing else agreed. The prefix alone is a
    /// coincidence waiting to happen: "Rush" prefixes "Rush Bros" and they are
    /// two games.
    /// </summary>
    NoCorroboration,

    /// <summary>
    /// A storefront refutes the pair. Either the child has a known parent that
    /// is a different work from the proposed base, or a source types it a main
    /// game with no parent at all. On the measured library this alone kills nine
    /// of the sequel false positives, including DOOM to DOOM Eternal, BioShock
    /// to BioShock Infinite and INSIDE to Inside the Backrooms.
    /// </summary>
    MetadataContradicts,

    /// <summary>
    /// A storefront has an opinion about one of the two works, so the title
    /// heuristic does not get a vote. The heuristic is a gap-filler: it proposes
    /// only where every source is silent, which on the measured library is the
    /// delisted staging and experimental branch apps and the non-Steam titles
    /// with no IGDB id.
    /// </summary>
    MetadataSpeaks,
}

/// <summary>
/// Tuning for <see cref="ExpansionDetector"/>. Every number the detector's
/// guards test lives here, because retuning any of them changes what a person
/// is asked to answer. Nothing overrides <see cref="Default"/> today.
/// </summary>
public sealed record ExpansionDetectorOptions
{
    /// <summary>The shipped settings, used when a caller passes none.</summary>
    public static ExpansionDetectorOptions Default { get; } = new();

    /// <summary>
    /// Ceiling on how many years after its base an expansion may ship. Not
    /// measured, chosen: wide enough for a long-lived title still getting
    /// packs, narrow enough to refuse two titles a generation apart.
    /// </summary>
    public int MaxYearGap { get; init; } = 15;

    /// <summary>
    /// Floor on the length of the base's normalised CORE, so it is a floor on
    /// what was actually compared rather than on the raw title.
    /// </summary>
    public int MinBaseCoreLength { get; init; } = 4;

    /// <summary>
    /// Whether a prefix match must be backed by a publisher, a year pair or a
    /// separator boundary. Nothing turns this off today.
    /// </summary>
    public bool RequireCorroboration { get; init; } = true;
}

/// <summary>
/// The expansion detector. Pure, deterministic, no IO.
///
/// <para>It exists because the soft matcher scores title DISTANCE and
/// "Civilization IV" is a long way from "Civilization IV: Beyond the Sword",
/// so no threshold on that matcher will ever propose the pair the user asked
/// about. This one asks a different question: does one title EXTEND another.
/// It proposes only; nothing here writes a link, and every proposal it makes
/// is refusable.</para>
/// </summary>
public static class ExpansionDetector
{
    /// <summary>Separators a store title uses between a game and its pack.</summary>
    private static readonly char[] Separators = [':', '-', '–', '—', '|', '/'];

    /// <summary>
    /// Every proposal over a set of subjects. Each child takes at most one
    /// base — the longest matching prefix, lowest work id on a
    /// tie — so a library holding both "Civilization" and "Civilization IV"
    /// files "Civilization IV: Beyond the Sword" under the nearer of the two.
    /// Ordered by base work id then child work id, so the queue does not
    /// shuffle between loads.
    /// </summary>
    public static IReadOnlyList<ExpansionProposal> Detect(
        IEnumerable<ExpansionSubject> subjects, ExpansionDetectorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        var settings = options ?? ExpansionDetectorOptions.Default;

        var rows = new List<Row>();
        foreach (var subject in subjects)
        {
            ArgumentNullException.ThrowIfNull(subject);
            rows.Add(new Row(subject, TitleNormalizer.Normalize(subject.Title)));
        }

        var byWorkId = new Dictionary<long, Row>(rows.Count);
        foreach (var row in rows)
        {
            byWorkId[row.Subject.WorkId] = row;
        }

        var best = new Dictionary<long, ExpansionProposal>();

        // ── Pass one: the storefronts ───────────────────────────────────────
        //
        // A claim that names a kind and a parent the library actually holds is
        // a proposal in its own right, and a better one than any title
        // comparison: Steam is authoritative for demos, betas, playtests and
        // mods, IGDB for expansions and editions, and between them they answer
        // 28 of the author's 38 title-derived proposals correctly, including
        // the five where longest-owned-prefix-wins had picked the wrong parent
        // outright.
        //
        // It is still a proposal. Metadata may propose with high confidence
        // and only the user's answer writes a link; nothing here auto-merges,
        // which is the rule the whole identity subsystem is built on.
        foreach (var child in rows)
        {
            if (child.Subject.Claim is not { Kind: { } kind } claim
                || kind == IdentityLinkKinds.SameGame
                || child.Subject.ClaimedParentWorkId is not { } parentWorkId
                || parentWorkId == child.Subject.WorkId
                || !byWorkId.TryGetValue(parentWorkId, out var parentRow))
            {
                continue;
            }

            best[child.Subject.WorkId] = new ExpansionProposal
            {
                BaseWorkId = parentWorkId,
                ChildWorkId = child.Subject.WorkId,
                PrefixTokenCount = parentRow.Title.Tokens.Count,
                Kind = kind,
                RelationLabel = claim.Label,
                FromMetadata = true,
                Evidence = Observe(parentRow, child),
            };
        }

        // ── Pass two: the title heuristic, where the storefronts are silent ──
        foreach (var child in rows)
        {
            if (best.ContainsKey(child.Subject.WorkId))
            {
                continue;
            }

            foreach (var candidate in rows)
            {
                if (!TryPropose(candidate, child, settings, out var proposal, out _)
                    || proposal is null)
                {
                    continue;
                }

                if (!best.TryGetValue(child.Subject.WorkId, out var standing)
                    || proposal.PrefixTokenCount > standing.PrefixTokenCount
                    || (proposal.PrefixTokenCount == standing.PrefixTokenCount
                        && proposal.BaseWorkId < standing.BaseWorkId))
                {
                    best[child.Subject.WorkId] = proposal;
                }
            }
        }

        var ordered = new List<ExpansionProposal>(best.Values);
        ordered.Sort(static (a, b) => a.BaseWorkId == b.BaseWorkId
            ? a.ChildWorkId.CompareTo(b.ChildWorkId)
            : a.BaseWorkId.CompareTo(b.BaseWorkId));

        return ordered;
    }

    /// <summary>
    /// The whole rule, for one ordered pair: does <paramref name="child"/>
    /// extend <paramref name="baseGame"/>. Public so a test can ask WHY a pair
    /// was refused rather than only that it was, which is what keeps each guard
    /// pinned by its own case.
    /// </summary>
    /// <param name="baseGame">The candidate base game.</param>
    /// <param name="child">The candidate expansion.</param>
    /// <param name="options">Tuning, or null for <see cref="ExpansionDetectorOptions.Default"/>.</param>
    /// <param name="proposal">The proposal when true, null when false.</param>
    /// <param name="reason">Which guard refused, or <see cref="ExpansionRefusalReason.None"/>.</param>
    /// <returns>True when the pair is proposed.</returns>
    public static bool TryPropose(
        ExpansionSubject baseGame,
        ExpansionSubject child,
        ExpansionDetectorOptions? options,
        out ExpansionProposal? proposal,
        out ExpansionRefusalReason reason)
    {
        ArgumentNullException.ThrowIfNull(baseGame);
        ArgumentNullException.ThrowIfNull(child);

        return TryPropose(
            new Row(baseGame, TitleNormalizer.Normalize(baseGame.Title)),
            new Row(child, TitleNormalizer.Normalize(child.Title)),
            options ?? ExpansionDetectorOptions.Default,
            out proposal,
            out reason);
    }

    private static bool TryPropose(
        Row baseGame,
        Row child,
        ExpansionDetectorOptions options,
        out ExpansionProposal? proposal,
        out ExpansionRefusalReason reason)
    {
        proposal = null;

        if (baseGame.Subject.WorkId == child.Subject.WorkId)
        {
            reason = ExpansionRefusalReason.SameWork;
            return false;
        }

        if (baseGame.Title.IsEmpty || child.Title.IsEmpty)
        {
            reason = ExpansionRefusalReason.EmptyTitle;
            return false;
        }

        // A base of one or two characters prefixes half a library. The floor is
        // on the CORE, so it is a floor on what was actually compared.
        if (baseGame.Title.Core.Length < options.MinBaseCoreLength)
        {
            reason = ExpansionRefusalReason.BaseTooShort;
            return false;
        }

        if (!IsStrictPrefix(baseGame.Title.Tokens, child.Title.Tokens))
        {
            reason = ExpansionRefusalReason.NotAPrefix;
            return false;
        }

        var suffix = child.Title.Tokens.Skip(baseGame.Title.Tokens.Count).ToArray();

        // THE SEQUEL GUARD, and the single most dangerous false positive this
        // detector could produce. "Portal" prefixes "Portal 2" and "The
        // Witcher" prefixes "The Witcher 3: Wild Hunt", and neither is an
        // expansion of anything. A suffix that OPENS with a number is naming a
        // different numbered entry in a series, so it is refused outright.
        // "Half-Life 2: Episode One" survives, because its suffix opens on
        // "episode" and the number follows.
        if (IsNumber(suffix[0]))
        {
            reason = ExpansionRefusalReason.SequelOrdinal;
            return false;
        }

        // A remaster is a separate BUILD of the same game, not an extension of
        // it (§9 pitfall 5). The soft matcher vetoes on the same disagreement.
        if (!baseGame.Title.RebuildEditions.SequenceEqual(
                child.Title.RebuildEditions, StringComparer.Ordinal))
        {
            reason = ExpansionRefusalReason.RebuildEdition;
            return false;
        }

        // ── THE GAP-FILLER RULE ─────────────────────────────────────────────
        //
        // Three guards, in this order, and the order is what makes a refusal
        // name the right mechanism.
        //
        // 1. A source states outright that the child extends nothing. IGDB
        //    game_type main_game with a null parent_game is that statement, and
        //    it refutes DOOM -> DOOM Eternal, BioShock -> BioShock Infinite,
        //    INSIDE -> Inside the Backrooms and six more on the measured
        //    library without the detector needing to understand any of them.
        if (child.Subject.Claim is { RefutesExtension: true })
        {
            reason = ExpansionRefusalReason.MetadataContradicts;
            return false;
        }

        // 2. A source names a parent, and it is not this base. Dishonored:
        //    Death of the Outsider is a standalone expansion of Dishonored 2,
        //    not of Dishonored; Counter-Strike: Condition Zero Deleted Scenes
        //    belongs to Condition Zero, not Counter-Strike; Arma 2: DayZ Mod
        //    belongs to Operation Arrowhead. Longest-owned-prefix-wins picks the
        //    wrong parent in every one of those, and the storefront picks the
        //    right one.
        if (child.Subject.ClaimedParentWorkId is { } claimedParent
            && claimedParent != baseGame.Subject.WorkId)
        {
            reason = ExpansionRefusalReason.MetadataContradicts;
            return false;
        }

        // 3. Anything else a source has an opinion about is not the heuristic's
        //    to guess at, on EITHER side of the pair. Where a storefront speaks
        //    the relation is proposed from the storefront, with the storefront's
        //    own word on it; the heuristic fills the gaps the storefronts leave,
        //    which on the measured library is the delisted staging and
        //    experimental branch apps and the non-Steam titles with no IGDB id.
        if (child.Subject.MetadataSpeaks || baseGame.Subject.MetadataSpeaks)
        {
            reason = ExpansionRefusalReason.MetadataSpeaks;
            return false;
        }

        var basePublisher = TitleNormalizer.NormalizePublisher(baseGame.Subject.Publisher);
        var childPublisher = TitleNormalizer.NormalizePublisher(child.Subject.Publisher);
        bool? publisherAgrees =
            basePublisher.Length > 0 && childPublisher.Length > 0
                ? string.Equals(basePublisher, childPublisher, StringComparison.Ordinal)
                : null;

        if (publisherAgrees == false)
        {
            reason = ExpansionRefusalReason.PublisherMismatch;
            return false;
        }

        var baseYear = baseGame.Subject.ReleaseYear ?? baseGame.Title.ParsedYear;
        var childYear = child.Subject.ReleaseYear ?? child.Title.ParsedYear;
        int? yearDelta = baseYear is not null && childYear is not null
            ? childYear.Value - baseYear.Value
            : null;

        if (yearDelta is { } delta)
        {
            // An expansion does not ship before the thing it expands. One year
            // of slack, because the two rows are enriched from different
            // sources and a regional date can disagree by a year.
            if (delta < -1)
            {
                reason = ExpansionRefusalReason.ChildPredatesBase;
                return false;
            }

            if (delta > options.MaxYearGap)
            {
                reason = ExpansionRefusalReason.YearGapTooWide;
                return false;
            }
        }

        // The child's RAW title, cut at a separator whose left side normalises
        // to exactly the base core: "Civilization IV: Beyond the Sword". Every
        // separator is tried, not just the first, or the hyphen inside
        // "Half-Life 2 - Episode One" would decide the question.
        var separator = HasSeparatorBoundary(child.Title.Original, baseGame.Title.Core);

        // The prefix on its own is a coincidence waiting to happen: "Rush" and
        // "Rush Bros" are two games and one is a prefix of the other. Something
        // beyond the prefix has to agree.
        //
        // The rule this replaced was satisfied by `yearDelta is not null`,
        // which means merely that BOTH YEARS ARE KNOWN. 947 of the author's
        // 1,033 works carry a first_release_year, so on an enriched library the
        // guard fired for 8.3% of pairs and was a no-op for the rest -- and the
        // test pinning it passed year: null on both sides, the one shape where
        // it did fire, so the suite reported a guard production did not have.
        // INSIDE and Inside the Backrooms, two completely unrelated games, were
        // proposed under it because both years were known.
        //
        // Two known years are not evidence of anything. Corroboration is now
        // one of two things:
        //
        //   * a SEPARATOR BOUNDARY, where the child's raw title splits at a
        //     colon, dash, pipe or slash whose left side normalises to exactly
        //     the base core -- "Sid Meier's Civilization IV: Beyond the Sword";
        //   * or an AGREEING PUBLISHER together with a year gap that is
        //     actually consistent with an expansion, meaning both years are
        //     known and the child did not ship first.
        //
        // A year pair on its own corroborates nothing, and neither does a
        // publisher on its own.
        var yearSupports = yearDelta is >= 0;
        if (options.RequireCorroboration
            && !separator
            && !(publisherAgrees == true && yearSupports))
        {
            reason = ExpansionRefusalReason.NoCorroboration;
            return false;
        }

        reason = ExpansionRefusalReason.None;

        // A title carrying a DemoConsolidation marker is a sample, not a
        // product. Eleven of the author's 38 proposals were demos, betas,
        // playtests and staging branches offered under the word "expansion";
        // the fallback path now names them for what they are even when no
        // storefront could be asked.
        var variantLabel = Queries.DemoConsolidation.VariantLabel(child.Subject.Title);

        proposal = new ExpansionProposal
        {
            BaseWorkId = baseGame.Subject.WorkId,
            ChildWorkId = child.Subject.WorkId,
            PrefixTokenCount = baseGame.Title.Tokens.Count,
            Kind = variantLabel is null
                ? IdentityLinkKinds.ExpansionOf
                : IdentityLinkKinds.VariantOf,
            RelationLabel = variantLabel,
            Evidence = new ExpansionEvidence(
                baseGame.Title.Core,
                string.Join(' ', suffix),
                publisherAgrees,
                yearDelta,
                separator),
        };

        return true;
    }

    /// <summary>
    /// The same observations the heuristic records, made about a pair a
    /// storefront proposed. The card shows the numbers whether or not they are
    /// the reason for the proposal, and a suffix is only reported when the
    /// child's title really does extend the parent's.
    /// </summary>
    private static ExpansionEvidence Observe(Row baseGame, Row child)
    {
        var suffix = IsStrictPrefix(baseGame.Title.Tokens, child.Title.Tokens)
            ? string.Join(' ', child.Title.Tokens.Skip(baseGame.Title.Tokens.Count))
            : string.Empty;

        var basePublisher = TitleNormalizer.NormalizePublisher(baseGame.Subject.Publisher);
        var childPublisher = TitleNormalizer.NormalizePublisher(child.Subject.Publisher);
        bool? publisherAgrees = basePublisher.Length > 0 && childPublisher.Length > 0
            ? string.Equals(basePublisher, childPublisher, StringComparison.Ordinal)
            : null;

        var baseYear = baseGame.Subject.ReleaseYear ?? baseGame.Title.ParsedYear;
        var childYear = child.Subject.ReleaseYear ?? child.Title.ParsedYear;

        return new ExpansionEvidence(
            baseGame.Title.Core,
            suffix,
            publisherAgrees,
            baseYear is not null && childYear is not null ? childYear.Value - baseYear.Value : null,
            HasSeparatorBoundary(child.Title.Original, baseGame.Title.Core));
    }

    private static bool IsStrictPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> whole)
    {
        if (prefix.Count == 0 || whole.Count <= prefix.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(prefix[i], whole[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumber(string token)
    {
        foreach (var c in token)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return token.Length > 0;
    }

    private static bool HasSeparatorBoundary(string rawChildTitle, string baseCore)
    {
        for (var i = 0; i < rawChildTitle.Length; i++)
        {
            if (Array.IndexOf(Separators, rawChildTitle[i]) < 0)
            {
                continue;
            }

            var left = TitleNormalizer.Normalize(rawChildTitle[..i]);
            if (string.Equals(left.Core, baseCore, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A subject beside the normalised title it was compared under.</summary>
    private sealed record Row(ExpansionSubject Subject, NormalizedTitle Title);
}

/// <summary>
/// How one proposal's evidence reads on a card. Kept here rather than in the
/// view model so the wording of the fact and the fact itself do not drift
/// apart, and because it is the same string a test asserts on.
/// </summary>
public static class ExpansionEvidenceText
{
    /// <summary>
    /// The year gap, signed, or an em dash when either year is unknown. Never
    /// zero-filled: an unknown year is not a gap of nothing.
    /// </summary>
    public static string YearText(ExpansionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.YearDelta is { } delta
            ? delta.ToString("+0;-0;0", CultureInfo.InvariantCulture)
            : "—";
    }

    /// <summary>
    /// The publisher verdict: SAME, DIFFERENT, or an em dash when either side
    /// is unknown. Unknown is its own answer, never rendered as agreement.
    /// </summary>
    public static string PublisherText(ExpansionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.PublisherAgrees switch
        {
            true => "SAME",
            false => "DIFFERENT",
            null => "—",
        };
    }
}
