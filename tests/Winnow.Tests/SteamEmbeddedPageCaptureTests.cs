using Winnow.Core.Ingest;
using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamEmbeddedPageCaptureTests
{
    [Theory]
    [InlineData(SteamAccountPageSource.EmbeddedSession)]
    [InlineData(SteamAccountPageSource.SavedFile)]
    public void Separate_captured_licence_pages_are_all_parsed_and_gaps_remain_incomplete(SteamAccountPageSource source)
    {
        var first = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1);
        var last = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesFinalPage);
        var pages = new SteamAccountPages
        {
            LicensesHtml = first, AdditionalLicensesHtml = [last],
            Source = source, CapturedAt = DateTimeOffset.UtcNow, SteamId = "76561198000000001",
        };

        var parsed = SteamAccountPageReader.Read(pages);
        var expected = new[] { first, last }.SelectMany(html => SteamLicensesPageParser.Parse(html).Rows)
            .Select(row => (row.ItemName, row.AcquiredAtUtc, row.AcquisitionMethod, row.LicenseType, row.PackageId)).Distinct().Count();
        Assert.Equal(expected, parsed.Licenses.Rows.Count);
        Assert.True(parsed.Licenses.IsTruncated);
        Assert.Equal(979, parsed.Licenses.TotalLicensesReported);
        Assert.Equal(pages.SteamId, parsed.SteamId);
    }

    [Theory]
    [InlineData(SteamAccountPageSource.EmbeddedSession)]
    [InlineData(SteamAccountPageSource.SavedFile)]
    public void Complete_walk_requires_contiguous_ranges_and_keeps_duplicate_facts_unique(SteamAccountPageSource source)
    {
        var first = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1)
            .Replace("1-100 of 979", "1-100 of 200", StringComparison.Ordinal);
        var last = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesFinalPage)
            .Replace("901-979 of 979", "101-200 of 200", StringComparison.Ordinal);
        var pages = new SteamAccountPages
        {
            LicensesHtml = first, AdditionalLicensesHtml = [last],
            Source = source, CapturedAt = DateTimeOffset.UtcNow,
        };
        var parsed = SteamAccountPageReader.Read(pages);
        Assert.False(parsed.Licenses.IsTruncated);
        Assert.Null(parsed.SteamId);
        Assert.Equal(parsed.Licenses.Rows.Count, SteamAccountPageReader.Read(pages with { AdditionalLicensesHtml = [last, last] }).Licenses.Rows.Count);
        Assert.True(SteamAccountPageReader.Read(pages with { HasFailedSavedInputs = true }).Licenses.IsTruncated);
        Assert.True(SteamAccountPageReader.Read(pages with { AdditionalLicensesHtml = [last.Replace("101-200 of 200", "102-200 of 200", StringComparison.Ordinal)] }).Licenses.IsTruncated);
    }
}
