using Winnow.Core.Ingest;

namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// Entry point for parsing a <see cref="SteamAccountPages"/> set. Both input
/// routes (the embedded WebView harvest and user-saved files) produce the same
/// <see cref="SteamAccountPages"/>, so this reader never knows which route ran.
/// §4.7 was amended, not violated: the distinction is who fetches.
/// </summary>
public static class SteamAccountPageReader
{
    /// <summary>Parses both pages. An absent page produces <see cref="SteamAccountPageParseOutcome.Absent"/>, not an error.</summary>
    public static SteamAccountPageParseResult Read(SteamAccountPages pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        return new SteamAccountPageParseResult(
            SteamLicensesPageParser.Parse(pages.LicensesHtml),
            SteamPurchaseHistoryPageParser.Parse(pages.HistoryHtml));
    }

    /// <summary>
    /// Determines which page a document is by attempting to parse it, or returns
    /// null. Which page a file is comes from what is inside it, never from its
    /// filename, because the user saved these under whatever their browser
    /// suggested.
    /// </summary>
    public static SteamAccountPageKind? Identify(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        if (SteamPurchaseHistoryPageParser.Parse(html).Outcome == SteamAccountPageParseOutcome.Parsed)
        {
            return SteamAccountPageKind.PurchaseHistory;
        }

        if (SteamLicensesPageParser.Parse(html).Outcome == SteamAccountPageParseOutcome.Parsed)
        {
            return SteamAccountPageKind.Licenses;
        }

        return null;
    }
}
