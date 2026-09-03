using System.Globalization;

namespace Winnow.Recommend;

/// <summary>
/// The vocabulary a feed card's one sentence may speak. Each member is a fact
/// about the game in front of the user, never a phrase: the wording lives in
/// <see cref="ReasonPhrasebook"/>, several per member, so what the feed says
/// and how it says it can be changed independently.
/// </summary>
public enum ReasonSignal
{
    /// <summary>No signal. Only ever the secondary.</summary>
    None = 0,

    /// <summary>Zero minutes and no play date: bought, never opened.</summary>
    NeverOpened,

    /// <summary>A play date beside zero minutes: launched, but no store measured it.</summary>
    LaunchedUnmeasured,

    /// <summary>Real minutes below §6.1's refund line: opened, sampled, dropped.</summary>
    Sampled,

    /// <summary>Minutes at or past the refund line: committed, then abandoned.</summary>
    Bounced,

    /// <summary>Bucket stale_but_patched: a major update landed after the user walked away.</summary>
    PatchedSinceYouLeft,

    /// <summary>Dormant for a known length of time.</summary>
    Dormant,

    /// <summary>Real minutes with no date at all — Steam's pre-timestamp sentinel.</summary>
    UndatedDormancy,

    /// <summary>Two or more distinct play episodes: someone trying to like a game.</summary>
    TriedToLikeIt,

    /// <summary>Carries a descriptor the user's hours concentrate in.</summary>
    TasteMatch,

    /// <summary>On disk right now.</summary>
    Installed,

    /// <summary>The same work owned on more than one store.</summary>
    BoughtTwice,

    /// <summary>A fair shake of hours, deeply dormant, and proven silence since.</summary>
    ProbablyDone,

    /// <summary>Online-multiplayer-only in a library that plays single-player.</summary>
    OnlineOnlyMismatch,

    /// <summary>Single-player-only in a library that plays online.</summary>
    SoloOnlyMismatch,

    /// <summary>Played inside the fresh-play window.</summary>
    PlayedRecently,

    /// <summary>The feed showed this release in the last few days.</summary>
    ShownRecently,

    /// <summary>Nothing else was true. The last-resort frame.</summary>
    Rotation,
}

/// <summary>
/// The numbers a reason is allowed to cite, read off the same facts the score
/// was computed from, so the sentence cannot state a figure the ranking never
/// saw. Every optional member is null when the fact is unknown rather than
/// zeroed, because a template that cannot be filled truthfully is dropped, and
/// a zero would render as a claim.
/// </summary>
public sealed record ReasonEvidence
{
    /// <summary>The release whose card this is. Also the deterministic variant selector.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>The game's display name.</summary>
    public required string Title { get; init; }

    /// <summary>Store slug the surfaced copy is owned on.</summary>
    public string Store { get; init; } = string.Empty;

    /// <summary>Latest cumulative minutes.</summary>
    public long PlaytimeMinutes { get; init; }

    /// <summary>Calendar year of the last recorded play, when one is known.</summary>
    public int? LastPlayedYear { get; init; }

    /// <summary>Days since last played, when a date is known.</summary>
    public double? DormancyDays { get; init; }

    /// <summary>Major updates observed after the last play, when history was read.</summary>
    public int? UpdatesSinceLastPlayed { get; init; }

    /// <summary>Title of the newest such update, sanitised for inline quoting.</summary>
    public string? LatestUpdateTitle { get; init; }

    /// <summary>Distinct play episodes the history shows.</summary>
    public int? ReturnEpisodes { get; init; }

    /// <summary>Distinct stores the work is owned on.</summary>
    public int StoreCount { get; init; } = 1;

    /// <summary>Display name of the descriptor the user's hours concentrate in.</summary>
    public string? TasteFacetName { get; init; }

    /// <summary>
    /// The matched descriptor's playtime weight divided by the weight of the
    /// user's single strongest descriptor, landing in [0,1]. Null when the
    /// library has no taste evidence or this game carries no matched
    /// descriptor, meaning absent evidence rather than a zero match. The
    /// scorer already computes this number for the taste-affinity weight;
    /// carrying it into the evidence lets a phrasing be gated on measured
    /// strength rather than on a descriptor name being present.
    /// </summary>
    public double? TasteAffinity { get; init; }
}

/// <summary>
/// A card's reason as structure rather than prose: what to say first, what to
/// add, and the facts to say it with. This is the seam: the scorer decides
/// WHAT is true, <see cref="ReasonBuilder"/> and <see cref="ReasonPhrasebook"/>
/// decide how it reads. A caller that wants its own wording renders from here
/// rather than parsing the sentence back apart.
/// </summary>
public sealed record RecommendationReason
{
    /// <summary>The strongest true thing about this game. Never <see cref="ReasonSignal.None"/>.</summary>
    public required ReasonSignal Primary { get; init; }

    /// <summary>One supporting fact the primary did not already tell, or None.</summary>
    public ReasonSignal Secondary { get; init; } = ReasonSignal.None;

    /// <summary>
    /// Every supporting fact the scorer proved about this card, strongest
    /// first, with <see cref="Secondary"/> as its head. A shelf caps how
    /// many of its cards may cite the same fact (see
    /// <see cref="ShelfReasonLedger"/>), and a card whose strongest fact is
    /// already spent needs somewhere honest to go. Reaching down this list
    /// is honest because every entry fired for this card; nothing on it is
    /// a new claim. When the list runs out the card says less, it never
    /// borrows. Defaults to empty; a caller that sets only
    /// <see cref="Secondary"/> gets the original single-fact behaviour.
    /// </summary>
    public IReadOnlyList<ReasonSignal> SupportingSignals { get; init; } = [];

    /// <summary>The numbers both clauses may cite.</summary>
    public required ReasonEvidence Evidence { get; init; }
}

/// <summary>Token substitution for phrasebook templates. Invariant culture throughout.</summary>
internal static class ReasonTokens
{
    /// <summary>Longest update title a reason may quote before it is elided.</summary>
    public const int MaxUpdateTitleChars = 48;

    /// <summary>
    /// Resolves one template token against the evidence, or null when this
    /// game has no such fact — which is how a variant that cannot be told
    /// truthfully is filtered out rather than rendered with a hole in it.
    ///
    /// <para>The null-means-skip mechanism is also the honesty gate.
    /// <c>{strongFacet}</c> is <c>{facet}</c> with a proof attached: it
    /// resolves to the same descriptor name only at or above
    /// <see cref="RecommendationTuning.OnTasteMinAffinity"/>, so a phrasing
    /// claiming strength is unusable on a game whose match is faint. No new
    /// query or cross-card bookkeeping was needed; a null return is enough
    /// to retire the variant.</para>
    /// </summary>
    public static string? Resolve(
        string token, ReasonEvidence evidence, RecommendationTuning tuning) => token switch
    {
        "title" => Blank(evidence.Title),
        "store" => Blank(evidence.Store),
        "minutes" => evidence.PlaytimeMinutes > 0 ? Phrases.Duration(evidence.PlaytimeMinutes) : null,
        "year" => evidence.LastPlayedYear?.ToString(CultureInfo.InvariantCulture),
        "age" => evidence.DormancyDays is { } days ? Phrases.Age(days) : null,
        "updates" => evidence.UpdatesSinceLastPlayed is { } n and > 0
            ? (n == 1 ? "an update" : string.Create(CultureInfo.InvariantCulture, $"{n} updates"))
            : null,
        "updateCount" => evidence.UpdatesSinceLastPlayed is { } c and > 0
            ? c.ToString(CultureInfo.InvariantCulture)
            : null,
        "updateTitle" => Sanitize(evidence.LatestUpdateTitle),
        "episodes" => evidence.ReturnEpisodes is { } e and >= 2
            ? e.ToString(CultureInfo.InvariantCulture)
            : null,
        "stores" => evidence.StoreCount >= 2
            ? evidence.StoreCount.ToString(CultureInfo.InvariantCulture)
            : null,
        "facet" => Blank(evidence.TasteFacetName),
        "strongFacet" => evidence.TasteAffinity is { } affinity
            && affinity >= tuning.OnTasteMinAffinity
                ? Blank(evidence.TasteFacetName)
                : null,
        _ => null,
    };

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Makes a store-authored update title safe to quote inside a feed card's
    /// single sentence. Quote characters are stripped first so a removed quote
    /// cannot hide the whitespace that follows a period. Whitespace is then
    /// collapsed, sentence terminators are removed according to
    /// <see cref="IsTerminator"/>, and the result is capped at
    /// <see cref="MaxUpdateTitleChars"/>.
    ///
    /// <para>Fixed 2026-09-02 (TASK-74): the original rule replaced every
    /// <c>.</c> <c>!</c> <c>?</c> <c>;</c> with a space, which destroyed
    /// version numbers in update titles. Three of six cards on one shelf
    /// rendered damaged titles ("1 4 10 5 Hotfix Patch Notes") because the
    /// periods inside version strings were being stripped. The database
    /// stored the titles correctly; the damage was done here on the way to
    /// the card. No test caught it because every fixture title was prose
    /// with no version number. The rule was narrowed rather than dropped;
    /// see <see cref="IsTerminator"/> for what now qualifies.</para>
    /// </summary>
    public static string? Sanitize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var unquoted = new System.Text.StringBuilder(title.Length);
        foreach (var ch in title)
        {
            if (ch is not ('"' or '“' or '”'))
            {
                unquoted.Append(ch);
            }
        }

        var collapsed = string.Join(' ', unquoted.ToString().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var cleaned = new System.Text.StringBuilder(collapsed.Length);
        for (var i = 0; i < collapsed.Length; i++)
        {
            var ch = collapsed[i];
            cleaned.Append(IsTerminator(collapsed, i) ? ' ' : ch);
        }

        var result = string.Join(' ', cleaned.ToString().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (result.Length == 0)
        {
            return null;
        }

        return result.Length <= MaxUpdateTitleChars
            ? result
            : result[..MaxUpdateTitleChars].TrimEnd() + "…";
    }

    /// <summary>
    /// A <c>.</c> <c>!</c> <c>?</c> or <c>;</c> is a terminator only when
    /// the contiguous run of terminator characters it belongs to ends at
    /// the end of the string or at whitespace. Everything else survives.
    ///
    /// <para>This beats a digit-dot-digit exemption because a period inside
    /// a version number is never followed by a space. Keying the test on
    /// what follows the run rather than on what flanks each character
    /// handles trailing-letter shapes like <c>7.9.1b</c> and <c>2.03.a</c>
    /// for free. A digit-dot-digit test gets <c>2.03.a</c> wrong because
    /// the second period sits between a digit and a letter.</para>
    ///
    /// <para>The run grouping handles <c>Hotfix!!</c>: both marks belong to
    /// one run that ends at the end of the string, so both go. Without it
    /// the first <c>!</c> would survive, being followed by another
    /// <c>!</c> rather than by whitespace.</para>
    ///
    /// <para>This rule and <c>ReasonContractTests.SentenceCount</c> define
    /// a terminator identically: <c>[.!?]</c> followed by whitespace or
    /// end-of-string, outside quoted spans. What Sanitize removes is
    /// exactly what the one-sentence contract would count.</para>
    /// </summary>
    private static bool IsTerminator(string collapsed, int index)
    {
        if (!IsTerminatorChar(collapsed[index]))
        {
            return false;
        }

        var end = index;
        while (end < collapsed.Length && IsTerminatorChar(collapsed[end]))
        {
            end++;
        }

        return end == collapsed.Length || char.IsWhiteSpace(collapsed[end]);
    }

    private static bool IsTerminatorChar(char ch) => ch is '.' or '!' or '?' or ';';
}
