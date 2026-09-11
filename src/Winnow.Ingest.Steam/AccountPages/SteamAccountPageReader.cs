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
            ReadLicenses(pages),
            SteamPurchaseHistoryPageParser.Parse(pages.HistoryHtml)) { SteamId = pages.SteamId };
    }

    private static SteamLicensesPageResult ReadLicenses(SteamAccountPages pages)
    {
        var first = SteamLicensesPageParser.Parse(pages.LicensesHtml);
        if (pages.Source != SteamAccountPageSource.SavedFile) return first;
        var parsed = new[] { first }.Concat(pages.AdditionalLicensesHtml.Select(SteamLicensesPageParser.Parse)).ToArray();
        if (first.Outcome != SteamAccountPageParseOutcome.Parsed) return first;
        var rows = parsed.SelectMany(p => p.Rows)
            .DistinctBy(r => (r.ItemName, r.AcquiredAtUtc, r.AcquisitionMethod, r.LicenseType, r.PackageId)).ToArray();
        var totals = parsed.Select(p => p.TotalLicensesReported).Distinct().ToArray();
        var complete = !pages.HasFailedSavedInputs && parsed.All(p => p.Outcome == SteamAccountPageParseOutcome.Parsed);
        if (totals is [int total])
        {
            var covered = 0;
            foreach (var page in parsed.OrderBy(p => p.RangeStart))
            {
                if (page.RangeStart is not { } start || page.RangeEnd is not { } end
                    || start < 1 || end < start || end > total || start > covered + 1)
                {
                    complete = false;
                    break;
                }
                covered = Math.Max(covered, end);
            }
            complete &= covered == total;
        }
        else
        {
            // Without ranges, multiple files cannot establish a contiguous walk.
            complete &= parsed.Length == 1 && !first.HasNextPage && totals is [null];
        }
        return first with
        {
            Rows = rows,
            SkippedRows = parsed.Sum(p => p.SkippedRows),
            RowsWithUnmappedAcquisition = rows.Count(r => r.LicenseType is null),
            RowsWithUnparsedDate = rows.Count(r => r.AcquiredAtUtc is null),
            TotalLicensesReported = totals.Length == 1 ? totals[0] : null,
            HasNextPage = !complete,
        };
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
