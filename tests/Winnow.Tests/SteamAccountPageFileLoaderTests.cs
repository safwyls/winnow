using Winnow.Core.Ingest;
using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The manual route: user-picked file paths in, one
/// <see cref="SteamAccountPages"/> out, marked as coming from saved files.
/// </summary>
public class SteamAccountPageFileLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("winnow-account-pages").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
    }

    private string CopyFixture(string fixture, string saveAs)
    {
        var path = Path.Combine(_dir, saveAs);
        File.Copy(SteamAccountPageFixtures.PathOf(fixture), path);
        return path;
    }

    [Fact]
    public async Task Both_pages_load_and_are_marked_as_saved_files()
    {
        var licenses = CopyFixture(SteamAccountPageFixtures.LicensesPage1, "one.html");
        var history = CopyFixture(SteamAccountPageFixtures.PurchaseHistory, "two.html");

        var result = await new SteamAccountPageFileLoader().LoadAsync([licenses, history]);

        Assert.True(result.Pages.IsComplete);
        Assert.Equal(SteamAccountPageSource.SavedFile, result.Pages.Source);
        Assert.All(result.Files, f => Assert.Equal(SteamAccountPageFileOutcome.Loaded, f.Outcome));
    }

    [Fact]
    public async Task Which_page_a_file_is_comes_from_its_content_not_its_name()
    {
        // Named the wrong way round on purpose: users save these under whatever
        // their browser suggested.
        var licensesNamedHistory = CopyFixture(SteamAccountPageFixtures.LicensesPage1, "purchase history.html");
        var historyNamedLicenses = CopyFixture(SteamAccountPageFixtures.PurchaseHistory, "licenses.html");

        var result = await new SteamAccountPageFileLoader()
            .LoadAsync([licensesNamedHistory, historyNamedLicenses]);

        Assert.Equal(
            SteamAccountPageKind.Licenses,
            Assert.Single(result.Files, f => f.Path == licensesNamedHistory).Kind);
        Assert.Equal(
            SteamAccountPageKind.PurchaseHistory,
            Assert.Single(result.Files, f => f.Path == historyNamedLicenses).Kind);

        Assert.Equal(
            SteamAccountPageParseOutcome.Parsed,
            SteamAccountPageReader.Read(result.Pages).Licenses.Outcome);
    }

    [Fact]
    public async Task One_page_is_a_partial_but_usable_load()
    {
        var licenses = CopyFixture(SteamAccountPageFixtures.LicensesPage1, "only.html");

        var result = await new SteamAccountPageFileLoader().LoadAsync([licenses]);

        Assert.True(result.AnythingLoaded);
        Assert.False(result.Pages.IsComplete);
        Assert.True(result.Pages.HasLicenses);
        Assert.False(result.Pages.HasHistory);
    }

    [Fact]
    public async Task A_file_that_is_not_an_account_page_is_reported_and_skipped()
    {
        var stranger = CopyFixture(SteamAccountPageFixtures.NotAnAccountPage, "stranger.html");

        var result = await new SteamAccountPageFileLoader().LoadAsync([stranger]);

        var file = Assert.Single(result.Files);
        Assert.Equal(SteamAccountPageFileOutcome.NotRecognized, file.Outcome);
        Assert.Null(file.Kind);
        Assert.NotNull(file.Detail);
        Assert.True(result.Pages.IsEmpty);
    }

    [Fact]
    public async Task A_missing_file_is_reported_and_does_not_stop_the_others()
    {
        var licenses = CopyFixture(SteamAccountPageFixtures.LicensesPage1, "present.html");
        var missing = Path.Combine(_dir, "gone.html");

        var result = await new SteamAccountPageFileLoader().LoadAsync([missing, licenses]);

        Assert.Equal(
            SteamAccountPageFileOutcome.NotFound,
            Assert.Single(result.Files, f => f.Path == missing).Outcome);
        Assert.True(result.Pages.HasLicenses);
    }

    [Fact]
    public async Task A_second_file_of_the_same_kind_is_reported_rather_than_silently_replacing_the_first()
    {
        var first = CopyFixture(SteamAccountPageFixtures.LicensesPage1, "first.html");
        var second = CopyFixture(SteamAccountPageFixtures.LicensesFinalPage, "second.html");

        var result = await new SteamAccountPageFileLoader().LoadAsync([first, second]);

        Assert.Equal(
            SteamAccountPageFileOutcome.Duplicate,
            Assert.Single(result.Files, f => f.Path == second).Outcome);

        // The first one won and is still the document that gets parsed.
        Assert.Equal(979, SteamLicensesPageParser.Parse(result.Pages.LicensesHtml).TotalLicensesReported);
        Assert.True(SteamLicensesPageParser.Parse(result.Pages.LicensesHtml).HasNextPage);
    }

    [Fact]
    public async Task An_empty_path_list_yields_an_empty_capture_rather_than_throwing()
    {
        var result = await new SteamAccountPageFileLoader().LoadAsync([]);

        Assert.True(result.Pages.IsEmpty);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task The_capture_is_stamped_from_the_supplied_clock()
    {
        var when = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(when);
        var licenses = CopyFixture(SteamAccountPageFixtures.LicensesPage1, "one.html");

        var result = await new SteamAccountPageFileLoader(clock: clock).LoadAsync([licenses]);

        Assert.Equal(when, result.Pages.CapturedAt);
    }

    [Fact]
    public async Task The_loaded_pages_go_straight_into_the_reader()
    {
        var licenses = CopyFixture(SteamAccountPageFixtures.LicensesPage1, "one.html");
        var history = CopyFixture(SteamAccountPageFixtures.PurchaseHistory, "two.html");

        var result = await new SteamAccountPageFileLoader().LoadAsync([licenses, history]);
        var parsed = SteamAccountPageReader.Read(result.Pages);

        Assert.True(parsed.AnythingParsed);
        Assert.Equal(13, parsed.Licenses.Rows.Count);
        Assert.Equal(12, parsed.History.Rows.Count);
    }

    [Fact]
    public void The_capture_type_never_renders_its_documents_into_a_string()
    {
        var pages = new SteamAccountPages
        {
            CapturedAt = DateTimeOffset.UtcNow,
            Source = SteamAccountPageSource.SavedFile,
            LicensesHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1),
        };

        var rendered = pages.ToString();

        Assert.DoesNotContain("Lantern Hollow", rendered, StringComparison.Ordinal);
        Assert.Contains("redacted", rendered, StringComparison.Ordinal);
    }
}
