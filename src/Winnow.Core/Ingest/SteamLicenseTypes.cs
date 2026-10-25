namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// Maps the free-text acquisition method from the licences page to a normalised
/// vocabulary value. Three methods were observed: "Steam Store", "Complimentary",
/// "Gift/Guest Pass". An unrecognised method is left null, counted, and never
/// mapped by guess. Verified 2026-08-29.
/// </summary>
public static class SteamLicenseTypes
{
    public const string SteamStore = "steam_store";
    public const string Complimentary = "complimentary";
    public const string Gift = "gift";
    public const string Retail = "retail";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam Store"] = SteamStore,
        ["Complimentary"] = Complimentary,
        ["Gift/Guest Pass"] = Gift,
        ["Gift"] = Gift,
        ["Guest Pass"] = Gift,
        ["Retail"] = Retail,
        ["Retail Key"] = Retail,
    };

    /// <summary>Returns the normalised licence type for a known acquisition method, or null for an unrecognised one.</summary>
    public static string? Map(string? acquisitionMethod)
        => acquisitionMethod is not null && Known.TryGetValue(acquisitionMethod, out var mapped) ? mapped : null;
}
