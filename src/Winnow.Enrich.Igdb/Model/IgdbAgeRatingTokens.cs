using System.Text;

namespace Winnow.Enrich.Igdb.Model;

/// <summary>
/// Maps IGDB's <c>age_ratings</c> rows into the <c>board:tier</c> tokens
/// migration 0024 stores in <c>work_maturity.ratings</c>. Three readings are
/// tried per row in descending order of how firmly the value is established;
/// a row none of the three can name yields no token.
/// </summary>
public static class IgdbAgeRatingTokens
{
    /// <summary>IGDB's deprecated <c>age_ratings.category</c> enum value 1 — ESRB.</summary>
    public const int LegacyCategoryEsrb = 1;

    /// <summary>IGDB's deprecated <c>age_ratings.category</c> enum value 2 — PEGI.</summary>
    public const int LegacyCategoryPegi = 2;

    /// <summary>IGDB's deprecated <c>age_ratings.category</c> enum value 3 — CERO.</summary>
    public const int LegacyCategoryCero = 3;

    /// <summary>IGDB's deprecated <c>age_ratings.category</c> enum value 4 — USK.</summary>
    public const int LegacyCategoryUsk = 4;

    /// <summary>IGDB's deprecated <c>age_ratings.category</c> enum value 5 — GRAC.</summary>
    public const int LegacyCategoryGrac = 5;

    /// <summary>IGDB's deprecated <c>age_ratings.category</c> enum value 6 — CLASS_IND.</summary>
    public const int LegacyCategoryClassInd = 6;

    /// <summary>IGDB's deprecated <c>age_ratings.category</c> enum value 7 — ACB.</summary>
    public const int LegacyCategoryAcb = 7;

    private const string Esrb = "esrb";
    private const string Pegi = "pegi";
    private const string Cero = "cero";
    private const string Usk = "usk";
    private const string Grac = "grac";
    private const string ClassInd = "classind";
    private const string Acb = "acb";

    // IGDB's published age_ratings.rating enum, values 1-39, transcribed whole.
    // The board is implied by the value: 1-5 PEGI, 6-12 ESRB, 13-17 CERO,
    // 18-22 USK, 23-27 GRAC, 28-33 CLASS_IND, 34-39 ACB. The deprecated
    // category field is not needed to read it.
    private static readonly IReadOnlyDictionary<int, string> LegacyRatings = new Dictionary<int, string>
    {
        [1] = Pegi + ":3",
        [2] = Pegi + ":7",
        [3] = Pegi + ":12",
        [4] = Pegi + ":16",
        [5] = Pegi + ":18",
        [6] = Esrb + ":rp",
        [7] = Esrb + ":ec",
        [8] = Esrb + ":e",
        [9] = Esrb + ":e10",
        [10] = Esrb + ":t",
        [11] = Esrb + ":m",
        [12] = Esrb + ":ao",
        [13] = Cero + ":a",
        [14] = Cero + ":b",
        [15] = Cero + ":c",
        [16] = Cero + ":d",
        [17] = Cero + ":z",
        [18] = Usk + ":0",
        [19] = Usk + ":6",
        [20] = Usk + ":12",
        [21] = Usk + ":16",
        [22] = Usk + ":18",
        [23] = Grac + ":all",
        [24] = Grac + ":12",
        [25] = Grac + ":15",
        [26] = Grac + ":18",
        [27] = Grac + ":testing",
        [28] = ClassInd + ":l",
        [29] = ClassInd + ":10",
        [30] = ClassInd + ":12",
        [31] = ClassInd + ":14",
        [32] = ClassInd + ":16",
        [33] = ClassInd + ":18",
        [34] = Acb + ":g",
        [35] = Acb + ":pg",
        [36] = Acb + ":m",
        [37] = Acb + ":ma15",
        [38] = Acb + ":r18",
        [39] = Acb + ":rc",
    };

    private static readonly IReadOnlyDictionary<string, string> Boards =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ESRB"] = Esrb,
            ["PEGI"] = Pegi,
            ["CERO"] = Cero,
            ["USK"] = Usk,
            ["GRAC"] = Grac,
            ["GCRB"] = Grac,
            ["CLASSIND"] = ClassInd,
            ["ACB"] = Acb,
            ["AUSTRALIANCLASSIFICATIONBOARD"] = Acb,
        };

    private static readonly IReadOnlyDictionary<int, string> BoardsByLegacyCategory =
        new Dictionary<int, string>
        {
            [LegacyCategoryEsrb] = Esrb,
            [LegacyCategoryPegi] = Pegi,
            [LegacyCategoryCero] = Cero,
            [LegacyCategoryUsk] = Usk,
            [LegacyCategoryGrac] = Grac,
            [LegacyCategoryClassInd] = ClassInd,
            [LegacyCategoryAcb] = Acb,
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Tiers =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Esrb] = new HashSet<string>(StringComparer.Ordinal) { "RP", "EC", "E", "E10", "T", "M", "AO" },
            [Pegi] = new HashSet<string>(StringComparer.Ordinal) { "3", "7", "12", "16", "18" },
            [Cero] = new HashSet<string>(StringComparer.Ordinal) { "A", "B", "C", "D", "Z" },
            [Usk] = new HashSet<string>(StringComparer.Ordinal) { "0", "6", "12", "16", "18" },
            [Grac] = new HashSet<string>(StringComparer.Ordinal) { "ALL", "12", "15", "18", "TESTING" },
            [ClassInd] = new HashSet<string>(StringComparer.Ordinal) { "L", "10", "12", "14", "16", "18" },
            [Acb] = new HashSet<string>(StringComparer.Ordinal) { "G", "PG", "M", "MA15", "R18", "X18", "RC" },
        };

    private static readonly IReadOnlyDictionary<string, string> TierAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["THREE"] = "3",
            ["SEVEN"] = "7",
            ["TEN"] = "10",
            ["TWELVE"] = "12",
            ["FOURTEEN"] = "14",
            ["FIFTEEN"] = "15",
            ["SIXTEEN"] = "16",
            ["EIGHTEEN"] = "18",
            ["RATINGPENDING"] = "RP",
            ["EARLYCHILDHOOD"] = "EC",
            ["EVERYONE"] = "E",
            ["EVERYONE10"] = "E10",
            ["TEEN"] = "T",
            ["MATURE"] = "M",
            ["MATURE17"] = "M",
            ["ADULTSONLY"] = "AO",
            ["ADULTSONLY18"] = "AO",
            ["RESTRICTED18"] = "R18",
            ["REFUSEDCLASSIFICATION"] = "RC",
            ["GENERAL"] = "G",
            ["PARENTALGUIDANCE"] = "PG",
        };

    /// <summary>
    /// First reading: the published <c>age_ratings.rating</c> enum. The board is
    /// encoded in the value itself, so no other field is needed.
    /// </summary>
    public static string? FromLegacyRating(int? rating)
        => rating is { } value && LegacyRatings.TryGetValue(value, out var token) ? token : null;

    /// <summary>
    /// Second reading: the expanded <c>organization.name</c> and
    /// <c>rating_category.rating</c> labels. These are current reference fields,
    /// not deprecated enums, so this path survives IGDB dropping the numeric
    /// pair. <c>acb:x18</c> — one of the two rating codes
    /// <see cref="MaturityRules"/> calls explicit — is reachable only through
    /// this path: X 18+ is a films-only category in Australia (games are
    /// classified G, PG, M, MA 15+, R 18+ or RC), so IGDB's legacy rating
    /// enum has no value for it.
    /// </summary>
    public static string? FromLabels(string? organizationName, string? ratingLabel)
    {
        var board = BoardFor(organizationName);
        return board is null ? null : TierToken(board, ratingLabel);
    }

    /// <summary>
    /// Third reading: the published <c>category</c> enum names the board, the
    /// <c>rating_category.rating</c> label names the tier. This is the path that
    /// survives IGDB dropping the deprecated <c>rating</c> value while keeping
    /// <c>category</c>.
    /// </summary>
    public static string? FromLegacyOrganization(int? category, string? ratingLabel)
        => category is { } id
           && BoardsByLegacyCategory.TryGetValue(id, out var board)
            ? TierToken(board, ratingLabel)
            : null;

    private static string? BoardFor(string? organizationName)
    {
        var normalized = Normalize(organizationName);
        return normalized.Length == 0 ? null : Boards.GetValueOrDefault(normalized);
    }

    private static string? TierToken(string board, string? ratingLabel)
    {
        var tier = NormalizeTier(board, ratingLabel);
        return tier is null ? null : board + ":" + tier.ToLowerInvariant();
    }

    private static string? NormalizeTier(string board, string? ratingLabel)
    {
        var normalized = Normalize(ratingLabel);
        if (normalized.Length == 0)
        {
            return null;
        }

        // Rating labels arrive with a leading board prefix ("ESRB_M",
        // "CERO_Z") and sometimes a trailing "PLUS" ("R18PLUS"). Strip
        // both so the alias table and the tier set can match the core name.
        foreach (var prefix in Boards)
        {
            if (prefix.Value == board
                && normalized.Length > prefix.Key.Length
                && normalized.StartsWith(prefix.Key, StringComparison.Ordinal))
            {
                normalized = normalized[prefix.Key.Length..];
                break;
            }
        }

        if (normalized.Length > 4 && normalized.EndsWith("PLUS", StringComparison.Ordinal))
        {
            normalized = normalized[..^4];
        }

        if (TierAliases.TryGetValue(normalized, out var alias))
        {
            normalized = alias;
        }

        return Tiers.TryGetValue(board, out var tiers) && tiers.Contains(normalized) ? normalized : null;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }
}
