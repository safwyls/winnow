using Microsoft.Extensions.Logging.Abstractions;
using Dapper;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Auth;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Ingest.Steam.AccountPages;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamAccountPageProvenanceTests
{
    private static SteamAccountPages Pages(uint? account) => new()
    {
        SteamId = account is { } id ? SteamId.FromAccountId(id)!.Value.ToString() : null,
        Source = account is null ? SteamAccountPageSource.SavedFile : SteamAccountPageSource.EmbeddedSession,
        LicensesHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.LicensesPage1),
        HistoryHtml = SteamAccountPageFixtures.Read(SteamAccountPageFixtures.PurchaseHistory),
        CapturedAt = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task Identical_transactions_and_licenses_from_two_accounts_remain_distinct_after_restart()
    {
        using var db = new TempDatabase();
        var owned = new OwnershipRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Lantern Hollow" });
        var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Lantern Hollow" });
        var ownership = await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "steam" });
        var members = new OwnershipAccountRepository(db.Factory);
        foreach (var account in new[] { "10001", "10002" })
            await members.UpsertAsync(new(ownership, account, null, null, "steam_local", DateTime.UtcNow));
        var facts = new AccountFactRepository(db.Factory);
        var acquisitions = new AccountAcquisitionRepository(db.Factory);
        SteamAccountPageImportService Importer() => new(owned, releases, facts, acquisitions, db.Factory,
            new LibrarySyncGate(), NullLogger<SteamAccountPageImportService>.Instance);

        var a = await Importer().ImportAsync(Pages(10001));
        var b = await Importer().ImportAsync(Pages(10002));
        Assert.Equal(a.TransactionFactsRecorded, b.TransactionFactsRecorded);
        Assert.Equal(a.LicenseFactsRecorded, b.LicenseFactsRecorded);
        Assert.True(a.TransactionFactsRecorded > 0);
        Assert.Equal(1, a.OwnershipsFilled);
        Assert.Equal(1, b.OwnershipsFilled);
        var again = await Importer().ImportAsync(Pages(10001));
        Assert.Equal(0, again.TransactionFactsRecorded);
        Assert.Equal(0, again.OwnershipsFilled);

        var reopenedFacts = new AccountFactRepository(db.Factory);
        var transactions = await reopenedFacts.GetTransactionsAsync("steam");
        var licenses = await reopenedFacts.GetLicensesAsync("steam");
        Assert.Equal(new[] { "10001", "10002" }, transactions.Select(f => f.AccountRef).Distinct().Order());
        Assert.Equal(a.TransactionFactsRecorded, transactions.Count(f => f.AccountRef == "10001"));
        Assert.Equal(a.TransactionFactsRecorded, transactions.Count(f => f.AccountRef == "10002"));
        Assert.Equal(a.LicenseFactsRecorded, licenses.Count(f => f.AccountRef == "10002"));
        Assert.Null((await owned.GetAsync(ownership))!.AcquiredAt);
        Assert.Equal(2, (await acquisitions.GetAsync([ownership])).Count);
        var stats = await new AccountStatsRepository(db.Factory).GetAsync("steam");
        Assert.Equal(2, stats.KnownAccountCount);
        Assert.Equal(0, stats.UnknownAccountFactCount);
        Assert.Equal(a.TransactionFactsRecorded * 2, stats.TransactionCount);

        var csv = await new AcquisitionExport(owned, releases, acquisitions).ReadAsync();
        Assert.Contains("\"10001\"\r\n", csv.Content);
        Assert.Contains("\"10002\"\r\n", csv.Content);
        Assert.Equal(3, csv.Content.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task Unknown_saved_pages_never_borrow_the_current_account_or_fill_its_projection()
    {
        using var db = new TempDatabase();
        var owned = new OwnershipRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Lantern Hollow" });
        var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Lantern Hollow" });
        var ownership = await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "steam", AccountRef = "10001" });
        var settings = new SettingsRepository(db.Factory);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "10001");
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        var facts = new AccountFactRepository(db.Factory);
        var acquisitions = new AccountAcquisitionRepository(db.Factory);
        var importer = new SteamAccountPageImportService(owned, releases, facts, acquisitions, db.Factory,
            new LibrarySyncGate(), NullLogger<SteamAccountPageImportService>.Instance);
        await importer.ImportAsync(Pages(null));
        Assert.All(await facts.GetTransactionsAsync("steam"), f => Assert.Null(f.AccountRef));
        Assert.All(await facts.GetLicensesAsync("steam"), f => Assert.Null(f.AccountRef));
        Assert.Null(Assert.Single(await acquisitions.GetAsync([ownership])).AccountRef);
        Assert.NotNull((await owned.GetAsync(ownership))!.AcquiredAt);
        var reader = new AccountAcquisitionReader(acquisitions, settings);
        var projected = Assert.Single(await reader.ProjectAsync(await owned.GetAllAsync()));
        Assert.Null(projected.AcquiredAt);
        Assert.Null(projected.PricePaidCents);
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.All);
        Assert.NotNull(Assert.Single(await reader.ProjectAsync(await owned.GetAllAsync())).AcquiredAt);

        // A named capture cannot resolve through the global title index either.
        var named = await importer.ImportAsync(Pages(10002));
        Assert.Equal(0, named.OwnershipsFilled);
        Assert.Single(await acquisitions.GetAsync([ownership]));
        var model = new AccountStatsViewModel(new AccountStatsRepository(db.Factory));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("unknown account", model.IntroMessage);
        Assert.All(model.SpendRows, row => Assert.Empty(row.AmountText));
    }

    [Theory]
    [InlineData("a", "a", "b")]
    [InlineData("a", "a", null)]
    [InlineData("a", null, null)]
    [InlineData(null, "a", "b")]
    public void Changed_or_lost_capture_identity_is_rejected_permanently(string? expected, string? before, string? after)
    {
        var identity = new SteamAccountPageIdentity();
        Assert.False(identity.TryAccept(expected, before, after));
        Assert.True(identity.Rejected);
        Assert.False(identity.TryAccept("a", "a", "a"));
    }

    [Fact]
    public void Captured_pages_must_agree_on_account_and_parser_retains_it_without_reading_HTML_identity()
    {
        var identity = new SteamAccountPageIdentity();
        Assert.True(identity.TryAccept(null, "a", "a"));
        Assert.False(identity.TryAccept(null, "b", "b"));
        var pages = Pages(10001);
        Assert.Equal(pages.SteamId, SteamAccountPageReader.Read(pages).SteamId);
        Assert.Null(SteamAccountPageReader.Read(Pages(null)).SteamId);
        Assert.DoesNotContain(pages.SteamId!, pages.ToString());
    }

    [Fact]
    public async Task Different_account_acquisitions_project_the_selected_evidence_and_withhold_aggregate_conflicts()
    {
        using var db = new TempDatabase();
        var owned = new OwnershipRepository(db.Factory);
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Two accounts" });
        var release = await new ReleaseRepository(db.Factory).InsertAsync(new Release { WorkId = work, Name = "Two accounts" });
        var id = await owned.InsertAsync(new Ownership { ReleaseId = release, Store = "steam" });
        var repository = new AccountAcquisitionRepository(db.Factory);
        var first = new OwnershipAcquisitionObservation
        {
            OwnershipId = id, AccountRef = "10001", AcquiredAt = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), LicenseType = "retail",
            PricePaidCents = 500, PriceSource = "steam_account_history", Source = "steam", CapturedAt = DateTime.UtcNow,
        };
        await repository.TryAppendAsync(first);
        await repository.TryAppendAsync(first with { AccountRef = "10002", AcquiredAt = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LicenseType = "gift", PricePaidCents = 1200 });
        var settings = new SettingsRepository(db.Factory);
        var reader = new AccountAcquisitionReader(repository, settings);
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "10002");
        var selected = Assert.Single(await reader.ProjectAsync(await owned.GetAllAsync()));
        Assert.Equal(new DateTime(2024, 1, 1), selected.AcquiredAt);
        Assert.Equal("gift", selected.LicenseType);
        Assert.Equal(1200, selected.PricePaidCents);
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.All);
        var all = Assert.Single(await reader.ProjectAsync(await owned.GetAllAsync()));
        Assert.Equal(new DateTime(2020, 1, 1), all.AcquiredAt);
        Assert.Null(all.LicenseType);
        Assert.Null(all.PricePaidCents);
    }

    [Fact]
    public async Task Migration_preserves_unknown_legacy_facts_and_scopes_future_deduplication()
    {
        using var db = new TempDatabase(migrate: false);
        using var connection = db.Factory.Open();
        static string Migration(string suffix)
        {
            var assembly = typeof(Winnow.Data.DatabaseInitializer).Assembly;
            using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix)))!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        connection.Execute(Migration("0014_account_transaction_facts.sql"));
        connection.Execute("""
            INSERT INTO account_transactions(source, kind, transaction_type_raw, item_names_json, item_count, captured_at)
            VALUES ('steam','purchase','Purchase','["Same game"]',1,'2020-01-01');
            INSERT INTO account_licenses(source,item_name,acquisition_method_raw,captured_at)
            VALUES ('steam','Same game','Steam Store','2020-01-01');
            """);
        connection.Execute(Migration("0035_account_page_identity.sql"));
        var facts = new AccountFactRepository(db.Factory);
        var oldTransaction = Assert.Single(await facts.GetTransactionsAsync("steam"));
        var oldLicense = Assert.Single(await facts.GetLicensesAsync("steam"));
        Assert.Null(oldTransaction.AccountRef);
        Assert.Null(oldLicense.AccountRef);
        Assert.Equal(1, oldTransaction.Id);
        Assert.Null(await facts.TryAppendAsync(oldTransaction));
        Assert.Null(await facts.TryAppendAsync(oldLicense));
        Assert.NotNull(await facts.TryAppendAsync(oldTransaction with { AccountRef = "10001" }));
        Assert.NotNull(await facts.TryAppendAsync(oldTransaction with { AccountRef = "10002" }));
        Assert.NotNull(await facts.TryAppendAsync(oldLicense with { AccountRef = "10001" }));
        Assert.NotNull(await facts.TryAppendAsync(oldLicense with { AccountRef = "10002" }));
        Assert.Equal(3, (await facts.GetTransactionsAsync("steam")).Count);
        Assert.Equal(3, (await facts.GetLicensesAsync("steam")).Count);
    }
}
