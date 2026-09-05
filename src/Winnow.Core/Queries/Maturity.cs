namespace Winnow.Core.Queries;

/// <summary>
/// Known maturity-evidence sources. The token vocabulary is the contract the
/// enrichment clients must produce: each client maps its upstream payload
/// (IGDB age-rating objects, Steam store content descriptors) into exactly
/// one of these source values and writes <see cref="MaturityRatingCodes"/>
/// and <see cref="MaturityDescriptors"/> tokens.
/// </summary>
public static class MaturitySources
{
    /// <summary>IGDB age-rating endpoint.</summary>
    public const string Igdb = "igdb";

    /// <summary>Steam store content-descriptor metadata.</summary>
    public const string SteamStore = "steam_store";
}

/// <summary>
/// Rating tokens stored in <c>work_maturity.ratings</c>. Each constant is a
/// <c>board:category</c> pair emitted by an enrichment client. Only the
/// highest-age tokens per board appear here; lower categories such as
/// <c>esrb:e</c> or <c>pegi:3</c> are recognised by
/// <see cref="MaturityTiers"/> but have no named constant because no rule
/// refers to them individually.
/// </summary>
public static class MaturityRatingCodes
{
    /// <summary>ESRB Adults Only 18+ (United States/Canada). <see cref="MaturityTier.AdultsOnly"/>.</summary>
    public const string EsrbAdultsOnly = "esrb:ao";

    /// <summary>ESRB Mature 17+ (United States/Canada). <see cref="MaturityTier.Mature"/>.</summary>
    public const string EsrbMature = "esrb:m";

    /// <summary>PEGI 18 (Europe). <see cref="MaturityTier.Restricted18"/>.</summary>
    public const string Pegi18 = "pegi:18";

    /// <summary>PEGI 16 (Europe). <see cref="MaturityTier.Mature"/>.</summary>
    public const string Pegi16 = "pegi:16";

    /// <summary>USK 18 (Germany). <see cref="MaturityTier.Restricted18"/>.</summary>
    public const string Usk18 = "usk:18";

    /// <summary>CERO Z, 18+ (Japan). <see cref="MaturityTier.Restricted18"/>.</summary>
    public const string CeroZ = "cero:z";

    /// <summary>ACB R 18+ (Australia). <see cref="MaturityTier.Restricted18"/>.</summary>
    public const string AcbR18 = "acb:r18";

    /// <summary>ACB X 18+ (Australia). <see cref="MaturityTier.AdultsOnly"/>.</summary>
    public const string AcbX18 = "acb:x18";

    /// <summary>ClassInd 18 (Brazil). <see cref="MaturityTier.Restricted18"/>.</summary>
    public const string ClassInd18 = "classind:18";

    /// <summary>GRAC 18 (South Korea). <see cref="MaturityTier.Restricted18"/>.</summary>
    public const string Grac18 = "grac:18";
}

public static class MaturityDescriptors
{
    public const string AdultOnlySexualContent = "adult_only_sexual_content";

    public const string NudityOrSexualContent = "nudity_or_sexual_content";

    public const string FrequentNudityOrSexualContent = "frequent_nudity_or_sexual_content";

    public const string GeneralMatureContent = "general_mature_content";

    public const string ViolenceOrGore = "violence_or_gore";
}

/// <summary>
/// Ascending maturity scale anchored on the minimum age each rating board
/// states. Each placement is checkable against the board's own published
/// threshold rather than a matter of editorial judgement. Used for the
/// explicit-content gate (<see cref="MaturityRules"/>) and the rating-cap
/// filter (TASK-103).
/// </summary>
public enum MaturityTier
{
    /// <summary>
    /// No evidence, an unknown token, or a classification that is an absence
    /// of one (<c>esrb:rp</c> rating pending, <c>grac:testing</c>). Not a
    /// level on the ordered scale: <see cref="MaturityTiers.IsWithinCap"/>
    /// treats it as within every cap, because hiding a game for lack of data
    /// is the failure mode to avoid.
    /// </summary>
    Unrated = 0,

    /// <summary>Ages 0-7. ESRB E/EC, PEGI 3/7, CERO A, USK 0/6, GRAC All, ClassInd L, ACB G/PG.</summary>
    Everyone = 1,

    /// <summary>Ages 10-11. ESRB E10+, ClassInd 10.</summary>
    Preteen = 2,

    /// <summary>Ages 12-15. ESRB T, PEGI 12, CERO B/C, USK 12, GRAC 12/15, ClassInd 12/14, ACB M/MA 15+.</summary>
    Teen = 3,

    /// <summary>Ages 16-17. ESRB M, PEGI 16, CERO D, USK 16, ClassInd 16.</summary>
    Mature = 4,

    /// <summary>
    /// Age 18+, broad boards. PEGI 18, USK 18, CERO Z, GRAC 18, ClassInd 18,
    /// ACB R 18+, ACB RC. Each is routinely awarded for violence alone and is
    /// the ordinary rating a mainstream violent game carries.
    /// </summary>
    Restricted18 = 5,

    /// <summary>
    /// Sexually explicit content, separated by storefronts from the general
    /// catalogue. ESRB AO, ACB X 18+, and Steam's
    /// <c>adult_only_sexual_content</c> descriptor.
    /// </summary>
    AdultsOnly = 6,
}

/// <summary>
/// Maps rating and descriptor tokens to <see cref="MaturityTier"/> values and
/// answers tier queries. The tier map is the single source of truth for both
/// the explicit-content gate (<see cref="MaturityRules"/>) and the rating-cap
/// filter (TASK-103). <see cref="MaturityRules.ExplicitRatingCodes"/> and
/// <see cref="MaturityRules.ExplicitDescriptors"/> are derived from this map,
/// not listed separately, so the gate and the scale cannot drift apart.
/// </summary>
public static class MaturityTiers
{
    private static readonly Dictionary<string, MaturityTier> RatingTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        // ESRB (United States and Canada). AO is the only category above M
        // and the only one storefronts treat as a separate shelf rather than a
        // rating a mainstream release carries. M sits at Mature (17+), not
        // Restricted18, because the ESRB's own age floor is 17.
        ["esrb:rp"] = MaturityTier.Unrated,
        ["esrb:ec"] = MaturityTier.Everyone,
        ["esrb:e"] = MaturityTier.Everyone,
        ["esrb:e10"] = MaturityTier.Preteen,
        ["esrb:t"] = MaturityTier.Teen,
        ["esrb:m"] = MaturityTier.Mature,
        ["esrb:ao"] = MaturityTier.AdultsOnly,

        // PEGI (most of Europe). 18 is awarded for extreme or gross violence
        // on its own — GTA V, Doom Eternal and The Witcher 3 all carry
        // pegi:18. A broad 18+ board rating is a maturity level, not an
        // adult-content flag, so it sits at Restricted18.
        ["pegi:3"] = MaturityTier.Everyone,
        ["pegi:7"] = MaturityTier.Everyone,
        ["pegi:12"] = MaturityTier.Teen,
        ["pegi:16"] = MaturityTier.Mature,
        ["pegi:18"] = MaturityTier.Restricted18,

        // CERO (Japan). Z is the board's top age category and is routinely
        // awarded for violence: every GTA game is CERO Z. Restricted18.
        ["cero:a"] = MaturityTier.Everyone,
        ["cero:b"] = MaturityTier.Teen,
        ["cero:c"] = MaturityTier.Teen,
        ["cero:d"] = MaturityTier.Mature,
        ["cero:z"] = MaturityTier.Restricted18,

        // USK (Germany). 18 is the top category; Cyberpunk 2077 is USK 18.
        // Awarded for violence; sits at Restricted18.
        ["usk:0"] = MaturityTier.Everyone,
        ["usk:6"] = MaturityTier.Everyone,
        ["usk:12"] = MaturityTier.Teen,
        ["usk:16"] = MaturityTier.Mature,
        ["usk:18"] = MaturityTier.Restricted18,

        // GRAC (South Korea). 18 is the top age category. grac:testing is a
        // pending classification with no age signal, mapped to Unrated.
        ["grac:all"] = MaturityTier.Everyone,
        ["grac:12"] = MaturityTier.Teen,
        ["grac:15"] = MaturityTier.Teen,
        ["grac:18"] = MaturityTier.Restricted18,
        ["grac:testing"] = MaturityTier.Unrated,

        // ClassInd (Brazil). 18 is the top age category and covers gratuitous
        // violence and torture. Restricted18.
        ["classind:l"] = MaturityTier.Everyone,
        ["classind:10"] = MaturityTier.Preteen,
        ["classind:12"] = MaturityTier.Teen,
        ["classind:14"] = MaturityTier.Teen,
        ["classind:16"] = MaturityTier.Mature,
        ["classind:18"] = MaturityTier.Restricted18,

        // ACB (Australia). R 18+ is legally restricted to adults for
        // high-impact content including violence — the exact token that was
        // hiding GTA V, Cyberpunk 2077 and The Witcher 3 when the explicit
        // gate treated every 18+ board rating as adults-only. It sits at
        // Restricted18. X 18+ is specifically sexually explicit material
        // (actual sexual activity between consenting adults). It is a
        // films-only category in Australia; computer games are classified G,
        // PG, M, MA 15+, R 18+ or RC and cannot legally carry X 18+, so the
        // token is close to inert in practice. It is kept at AdultsOnly
        // because if the token ever does arrive it can only mean sexually
        // explicit content. RC (Refused Classification) cannot be legally sold
        // but the reason is not necessarily sexual, so it sits at Restricted18.
        ["acb:g"] = MaturityTier.Everyone,
        ["acb:pg"] = MaturityTier.Everyone,
        ["acb:m"] = MaturityTier.Teen,
        ["acb:ma15"] = MaturityTier.Teen,
        ["acb:r18"] = MaturityTier.Restricted18,
        ["acb:x18"] = MaturityTier.AdultsOnly,
        ["acb:rc"] = MaturityTier.Restricted18,
    };

    private static readonly Dictionary<string, MaturityTier> DescriptorTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        [MaturityDescriptors.NudityOrSexualContent] = MaturityTier.Mature,
        [MaturityDescriptors.ViolenceOrGore] = MaturityTier.Mature,
        [MaturityDescriptors.GeneralMatureContent] = MaturityTier.Mature,
        [MaturityDescriptors.FrequentNudityOrSexualContent] = MaturityTier.Restricted18,
        [MaturityDescriptors.AdultOnlySexualContent] = MaturityTier.AdultsOnly,
    };

    private static readonly Dictionary<MaturityTier, string> CapTokens = new()
    {
        [MaturityTier.Everyone] = "everyone",
        [MaturityTier.Preteen] = "preteen",
        [MaturityTier.Teen] = "teen",
        [MaturityTier.Mature] = "mature",
        [MaturityTier.Restricted18] = "restricted18",
        [MaturityTier.AdultsOnly] = "adults_only",
    };

    /// <summary>
    /// The tier values in ascending order, excluding
    /// <see cref="MaturityTier.Unrated"/>. Unrated is excluded because it is
    /// not a level on the scale: absence of data is not a rating.
    /// </summary>
    public static IReadOnlyList<MaturityTier> Ordered { get; } =
    [
        MaturityTier.Everyone,
        MaturityTier.Preteen,
        MaturityTier.Teen,
        MaturityTier.Mature,
        MaturityTier.Restricted18,
        MaturityTier.AdultsOnly,
    ];

    /// <summary>
    /// Returns the <see cref="MaturityTier"/> for a single rating token, or
    /// <see cref="MaturityTier.Unrated"/> if the token is null, empty or
    /// unrecognised.
    /// </summary>
    public static MaturityTier ForRating(string? token)
        => Lookup(RatingTiers, token);

    /// <summary>
    /// Returns the <see cref="MaturityTier"/> for a single descriptor token,
    /// or <see cref="MaturityTier.Unrated"/> if the token is null, empty or
    /// unrecognised.
    /// </summary>
    public static MaturityTier ForDescriptor(string? token)
        => Lookup(DescriptorTiers, token);

    /// <summary>
    /// Returns the highest tier across all rating and descriptor tokens stored
    /// for a work. Both arguments are comma-joined column values as stored in
    /// <c>work_maturity</c>.
    /// </summary>
    public static MaturityTier Highest(string? ratings, string? descriptors)
    {
        var highest = MaturityTier.Unrated;

        foreach (var token in MaturityRules.Split(ratings))
        {
            var tier = ForRating(token);
            if (tier > highest)
            {
                highest = tier;
            }
        }

        foreach (var token in MaturityRules.Split(descriptors))
        {
            var tier = ForDescriptor(token);
            if (tier > highest)
            {
                highest = tier;
            }
        }

        return highest;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="tier"/> is at or
    /// below <paramref name="cap"/>. <see cref="MaturityTier.Unrated"/> is
    /// within every cap: a work with no evidence is never hidden.
    /// </summary>
    public static bool IsWithinCap(MaturityTier tier, MaturityTier cap)
        => tier == MaturityTier.Unrated || tier <= cap;

    public static string Token(MaturityTier tier)
        => CapTokens.TryGetValue(tier, out var token) ? token : string.Empty;

    public static MaturityTier ParseToken(string? token, MaturityTier fallback)
    {
        var trimmed = token?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return fallback;
        }

        foreach (var (tier, name) in CapTokens)
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return tier;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Returns every rating-code token mapped to the given tier.
    /// </summary>
    public static IReadOnlyCollection<string> RatingCodesAt(MaturityTier tier)
        => TokensAt(RatingTiers, tier);

    /// <summary>
    /// Returns every descriptor token mapped to the given tier.
    /// </summary>
    public static IReadOnlyCollection<string> DescriptorsAt(MaturityTier tier)
        => TokensAt(DescriptorTiers, tier);

    private static MaturityTier Lookup(Dictionary<string, MaturityTier> map, string? token)
    {
        var trimmed = token?.Trim();
        return string.IsNullOrEmpty(trimmed) ? MaturityTier.Unrated : map.GetValueOrDefault(trimmed);
    }

    private static IReadOnlyCollection<string> TokensAt(Dictionary<string, MaturityTier> map, MaturityTier tier)
        => [.. map.Where(pair => pair.Value == tier).Select(pair => pair.Key).Order(StringComparer.Ordinal)];
}

/// <summary>
/// The explicit-content gate. <see cref="ExplicitRatingCodes"/> and
/// <see cref="ExplicitDescriptors"/> are derived from the tier map at
/// <see cref="MaturityTier.AdultsOnly"/>, not listed independently, so the
/// gate and the scale cannot drift apart.
/// <c>ExplicitContentTests.The_explicit_set_is_exactly_the_adults_only_signals</c>
/// pins the set, so widening the gate fails a test.
/// </summary>
public static class MaturityRules
{
    /// <summary>
    /// The tier at or above which a work is considered explicit. Set to
    /// <see cref="MaturityTier.AdultsOnly"/> — sexually explicit content
    /// separated by storefronts from the general catalogue.
    /// <see cref="MaturityTier.Restricted18"/> is deliberately below this
    /// line: broad 18+ board ratings (PEGI 18, USK 18, CERO Z, ACB R 18+
    /// and others) are routinely awarded for violence alone and are not
    /// adult-content flags.
    /// </summary>
    public const MaturityTier ExplicitTier = MaturityTier.AdultsOnly;

    /// <summary>
    /// The rating-code tokens at <see cref="ExplicitTier"/>, derived from the
    /// tier map. Currently <c>esrb:ao</c> and <c>acb:x18</c>.
    /// </summary>
    public static IReadOnlyCollection<string> ExplicitRatingCodes { get; }
        = MaturityTiers.RatingCodesAt(ExplicitTier);

    /// <summary>
    /// The descriptor tokens at <see cref="ExplicitTier"/>, derived from the
    /// tier map. Currently <c>adult_only_sexual_content</c>.
    /// </summary>
    public static IReadOnlyCollection<string> ExplicitDescriptors { get; }
        = MaturityTiers.DescriptorsAt(ExplicitTier);

    /// <summary>
    /// Returns <see langword="true"/> when the work's highest tier reaches
    /// <see cref="ExplicitTier"/>. Both arguments are comma-joined column
    /// values as stored in <c>work_maturity</c>. A work with no maturity row
    /// is never explicit: absence of data is not a rating.
    /// </summary>
    public static bool IsExplicit(string? ratings, string? descriptors)
        => MaturityTiers.Highest(ratings, descriptors) >= ExplicitTier;

    /// <summary>
    /// Builds a comma-joined column value from a sequence of tokens. Trims
    /// each token, drops blanks, de-duplicates case-insensitively, and returns
    /// null for an empty result. This is how an enrichment client builds the
    /// stored column.
    /// </summary>
    public static string? Join(IEnumerable<string>? tokens)
    {
        if (tokens is null)
        {
            return null;
        }

        var kept = new List<string>();
        foreach (var token in tokens)
        {
            var trimmed = token?.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !kept.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                kept.Add(trimmed);
            }
        }

        return kept.Count == 0 ? null : string.Join(',', kept);
    }

    /// <summary>
    /// Splits a comma-joined column value back into individual tokens. This
    /// is how a reader recovers the tokens a client stored with
    /// <see cref="Join"/>. Returns an empty list for null or whitespace input.
    /// </summary>
    public static IReadOnlyList<string> Split(string? tokens)
    {
        if (string.IsNullOrWhiteSpace(tokens))
        {
            return [];
        }

        var parts = tokens.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? [] : parts;
    }
}
