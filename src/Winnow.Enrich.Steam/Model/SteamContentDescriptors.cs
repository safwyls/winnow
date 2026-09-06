using System.Globalization;
using Winnow.Core.Queries;

namespace Winnow.Enrich.Steam.Model;

/// <summary>
/// Valve's <c>EContentDescriptorID</c> / <c>EUGCContentDescriptorID</c> numbering
/// and its mapping onto the token vocabulary migration 0024 defines. The numbering
/// is corroborated by the pinned fixture captured 2026-08-23
/// (<c>tests/fixtures/steam-store/getitems-v1.json</c>): ELDEN RING carries
/// <c>[2, 5]</c> and its store page prints exactly "Frequent Violence or Gore"
/// and "General Mature Content".
/// </summary>
public static class SteamContentDescriptors
{
    /// <summary>1 — "Some Nudity or Sexual Content".</summary>
    public const int SomeNudityOrSexualContent = 1;

    /// <summary>2 — "Frequent Violence or Gore".</summary>
    public const int FrequentViolenceOrGore = 2;

    /// <summary>3 — "Adult Only Sexual Content". Valve's own text requires age affirmation.</summary>
    public const int AdultOnlySexualContent = 3;

    /// <summary>4 — "Frequent Nudity or Sexual Content". Valve's own text requires age affirmation.</summary>
    public const int FrequentNudityOrSexualContent = 4;

    /// <summary>5 — "General Mature Content".</summary>
    public const int GeneralMatureContent = 5;

    /// <summary>
    /// Descriptor 4 has no token in migration 0024's vocabulary. It is carried
    /// verbatim rather than folded onto descriptor 1's token, because folding
    /// would erase the distinction between "some" and "frequent" — and 4 is one
    /// of the two descriptors Steam itself age-gates, so it is exactly the token
    /// a later retune would want to read.
    /// </summary>
    public const string FrequentNudityOrSexualContentToken = "frequent_nudity_or_sexual_content";

    /// <summary>
    /// Prefix for a descriptor id outside the five known values. Migration 0024's
    /// own text says unknown tokens are stored and ignored: evidence the rule
    /// cannot read today is evidence it can read after a retune.
    /// </summary>
    public const string UnknownTokenPrefix = "steam:";

    /// <summary>
    /// Maps one Valve descriptor id to its maturity token. Known ids land on the
    /// migration 0024 vocabulary; unknown ids are carried as <c>steam:&lt;id&gt;</c>.
    /// </summary>
    public static string TokenFor(int descriptorId) => descriptorId switch
    {
        SomeNudityOrSexualContent => MaturityDescriptors.NudityOrSexualContent,
        FrequentViolenceOrGore => MaturityDescriptors.ViolenceOrGore,
        AdultOnlySexualContent => MaturityDescriptors.AdultOnlySexualContent,
        FrequentNudityOrSexualContent => FrequentNudityOrSexualContentToken,
        GeneralMatureContent => MaturityDescriptors.GeneralMatureContent,
        _ => UnknownTokenPrefix + descriptorId.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Maps a descriptor array to distinct tokens in encounter order.
    /// Returns empty when the array is null or empty.
    /// </summary>
    public static IReadOnlyList<string> TokensFor(IEnumerable<int>? descriptorIds)
    {
        if (descriptorIds is null)
        {
            return [];
        }

        var tokens = new List<string>();
        foreach (var id in descriptorIds)
        {
            var token = TokenFor(id);
            if (!tokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }
}
