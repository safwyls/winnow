using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Winnow.Ingest.Steam.AccountPages;

/// <summary>
/// Parses the Steam licences page (<c>store.steampowered.com/account/licenses/</c>)
/// with AngleSharp (per game-library-design.md).
///
/// <para>The table is <c>table.account_table</c>, recognised by a
/// <c>th.license_date_col</c> header. Rows have <c>td.license_date_col</c>, an
/// unclassed item <c>td</c>, and <c>td.license_acquisition_col</c>. Dates render
/// as "MMM d, yyyy".</para>
///
/// <para>The licences page paginates via <c>div.license_paginator_ctn</c> (a span
/// reading "Showing licenses X-Y of Z") plus <c>a.license_paginator_next</c>. It
/// has no load-more control, so a single captured document is usually a partial
/// view and the parser reports that. Verified 2026-08-29.</para>
/// </summary>
public static partial class SteamLicensesPageParser
{
    [GeneratedRegex(@"RemoveFreeLicense\(\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdRegex { get; }

    [GeneratedRegex(@"([\d,]+)\s*-\s*([\d,]+)\s+of\s+([\d,]+)", RegexOptions.CultureInvariant)]
    private static partial Regex PaginatorRegex { get; }

    /// <summary>Parses a licences page document into rows, or returns Absent/NotRecognized.</summary>
    public static SteamLicensesPageResult Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return SteamLicensesPageResult.Absent();
        }

        var document = new HtmlParser().ParseDocument(html);

        var table = document
            .QuerySelectorAll("table.account_table")
            .FirstOrDefault(t => t.QuerySelector("th.license_date_col") is not null);

        if (table is null)
        {
            return SteamLicensesPageResult.NotRecognized(
                "no table.account_table carrying a th.license_date_col header");
        }

        var rows = new List<SteamLicenseRow>();
        var skipped = 0;
        var unmapped = 0;
        var unparsedDate = 0;
        var index = 0;

        foreach (var tr in table.QuerySelectorAll("tr"))
        {
            // The header row is the one with th cells, and it is the anchor this
            // parser recognised the table by, so it is skipped rather than counted.
            if (tr.QuerySelector("th") is not null)
            {
                continue;
            }

            index++;

            var dateCell = tr.QuerySelector("td.license_date_col");
            var acquisitionCell = tr.QuerySelector("td.license_acquisition_col");
            if (dateCell is null || acquisitionCell is null)
            {
                skipped++;
                continue;
            }

            var itemCell = tr
                .QuerySelectorAll("td")
                .FirstOrDefault(td => td != dateCell
                    && td != acquisitionCell
                    && !td.ClassList.Contains("license_date_col")
                    && !td.ClassList.Contains("license_acquisition_col"));

            if (itemCell is null)
            {
                skipped++;
                continue;
            }

            var packageId = ReadPackageId(itemCell);
            var itemName = ReadItemName(itemCell);
            if (itemName.Length == 0)
            {
                skipped++;
                continue;
            }

            var method = SteamPageValues.Collapse(acquisitionCell.TextContent);
            if (method.Length == 0)
            {
                skipped++;
                continue;
            }

            var licenseType = SteamLicenseTypes.Map(method);
            if (licenseType is null)
            {
                unmapped++;
            }

            var acquiredAt = SteamPageValues.TryParseDateUtc(dateCell.TextContent);
            if (acquiredAt is null)
            {
                unparsedDate++;
            }

            rows.Add(new SteamLicenseRow
            {
                RowIndex = index,
                ItemName = itemName,
                AcquiredAtUtc = acquiredAt,
                AcquisitionMethod = method,
                LicenseType = licenseType,
                PackageId = packageId,
            });
        }

        var (total, hasNext) = ReadPaginator(document);

        return new SteamLicensesPageResult
        {
            Outcome = SteamAccountPageParseOutcome.Parsed,
            Rows = rows,
            SkippedRows = skipped,
            RowsWithUnmappedAcquisition = unmapped,
            RowsWithUnparsedDate = unparsedDate,
            TotalLicensesReported = total,
            HasNextPage = hasNext,
        };
    }

    private static string? ReadPackageId(IElement itemCell)
    {
        foreach (var anchor in itemCell.QuerySelectorAll("a"))
        {
            var href = anchor.GetAttribute("href");
            if (href is null)
            {
                continue;
            }

            var match = PackageIdRegex.Match(href);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static string ReadItemName(IElement itemCell)
    {
        // The remove-license control lives inside the item cell, so its text
        // ("Remove") would otherwise land in the middle of the product name.
        var clone = (IElement)itemCell.Clone(deep: true);
        foreach (var control in clone.QuerySelectorAll(".free_license_remove_link"))
        {
            control.Remove();
        }

        return SteamPageValues.Collapse(clone.TextContent);
    }

    private static (int? Total, bool HasNext) ReadPaginator(IHtmlDocument document)
    {
        var hasNext = document.QuerySelector("a.license_paginator_next") is not null;

        foreach (var span in document.QuerySelectorAll(".license_paginator_ctn span"))
        {
            var match = PaginatorRegex.Match(SteamPageValues.Collapse(span.TextContent));
            if (!match.Success)
            {
                continue;
            }

            var digits = match.Groups[3].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
            {
                return (total, hasNext);
            }
        }

        return (null, hasNext);
    }
}
