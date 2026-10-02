using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class SteamPagesContractParityTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task Frontend_page_JSON_reaches_the_production_importer_with_documents_time_and_account_intact(int source, bool includeHistory)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-steam-page-contract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
        try
        {
            await app.StartAsync();
            using var client = WinnowApiClient.Attach(directory);
            const string capturedAt = "2026-09-29T09:30:00.123Z";
            var body = JsonSerializer.SerializeToElement(new
            {
                licensesHtml = Licenses(1, "Fixture café"),
                additionalLicensesHtml = new[] { Licenses(2, "Fixture two") },
                historyHtml = includeHistory ? History : null,
                capturedAt,
                source,
                steamId = source == 0 ? "76561198000000001" : null,
            });
            var pages = body.Deserialize<SteamAccountPages>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Contains("content redacted", pages.ToString(), StringComparison.Ordinal);
            Assert.Contains("bytes", pages.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("Fixture café", pages.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("19.99", pages.ToString(), StringComparison.Ordinal);
            var response = await client.SendAsync<JsonElement, SteamImportResponse>(HttpMethod.Post, "imports/steam/pages", body);
            Assert.Equal("Parsed", response.LicensesOutcome);
            Assert.Equal(includeHistory ? "Parsed" : "Absent", response.HistoryOutcome);
            Assert.Equal(2, response.LicenseRowsParsed);
            Assert.Equal(2, response.LicenseFactsRecorded);
            Assert.Equal(includeHistory ? 1 : 0, response.HistoryRowsParsed);
            Assert.False(response.LicensesTruncated);
            var facts = app.Services.GetRequiredService<IAccountFactRepository>();
            var licenses = await facts.GetLicensesAsync(AccountFactSources.Steam);
            Assert.Equal(new[] { "Fixture café", "Fixture two" }, licenses.Select(x => x.ItemName).Order());
            Assert.All(licenses, fact =>
            {
                Assert.Equal(DateTimeOffset.Parse(capturedAt).UtcDateTime, fact.CapturedAt);
                Assert.Equal(source == 0 ? "39734273" : null, fact.AccountRef);
            });
            var transactions = await facts.GetTransactionsAsync(AccountFactSources.Steam);
            Assert.Equal(includeHistory ? 1 : 0, transactions.Count);
            Assert.All(transactions, fact => Assert.Equal(source == 0 ? "39734273" : null, fact.AccountRef));
            var repeat = await client.SendAsync<JsonElement, SteamImportResponse>(HttpMethod.Post, "imports/steam/pages", body);
            Assert.Equal(0, repeat.LicenseFactsRecorded);
            Assert.Equal(2, repeat.LicenseFactsAlreadyRecorded);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Whitespace_in_the_frontend_DTO_is_absent_and_does_not_record_account_facts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-steam-page-contract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
        try
        {
            await app.StartAsync();
            using var client = WinnowApiClient.Attach(directory);
            var body = JsonSerializer.SerializeToElement(new { licensesHtml = "  ", capturedAt = "2026-09-29T09:30:00Z", source = 0 });
            var response = await client.SendAsync<JsonElement, SteamImportResponse>(HttpMethod.Post, "imports/steam/pages", body);
            Assert.Equal(0, response.LicenseFactsRecorded);
            Assert.Equal(0, response.TransactionFactsRecorded);
            Assert.Empty(await app.Services.GetRequiredService<IAccountFactRepository>().GetLicensesAsync(AccountFactSources.Steam));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    private static string Licenses(int index, string title) => $$"""
        <div class="license_paginator_ctn"><span>Showing licenses {{index}}-{{index}} of 2</span></div>
        <table class="account_table"><tr><th class="license_date_col">Date</th><th>Item</th><th class="license_acquisition_col">Acquisition Method</th></tr>
        <tr><td class="license_date_col">Sep 28, 2026</td><td>{{title}}</td><td class="license_acquisition_col">Steam Store</td></tr></table>
        """;
    private const string History = """
        <table class="wallet_history_table"><thead><tr><th class="wht_date">Date</th><th class="wht_items">Items</th><th class="wht_type">Type</th><th class="wht_total">Total</th></tr></thead>
        <tbody><tr class="wallet_table_row"><td class="wht_date">Sep 28, 2026</td><td class="wht_items"><div style="clear:both">Fixture café</div></td><td class="wht_type">Purchase</td><td class="wht_total">$19.99</td></tr></tbody></table>
        """;
}
