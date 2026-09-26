namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// How one page parse attempt ended. NotRecognized means the document's overall
/// structure does not match the expected table, so it is refused with a reason
/// instead of producing junk rows. Absent means the page was not captured at all.
/// </summary>
public enum SteamAccountPageParseOutcome
{
    /// <summary>The document was recognised and rows were extracted.</summary>
    Parsed = 0,

    /// <summary>The document was present but its structure did not match. Carries a reason.</summary>
    NotRecognized = 1,

    /// <summary>No document was provided for this page.</summary>
    Absent = 2,
}
