using Dapper;
using Winnow.App.Services;
using Winnow.Core.Identity;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Stores;
using Xunit;

namespace Winnow.Tests;

public sealed class EditionEvidenceTests
{
    [Theory]
    [InlineData("matching", 1, 1, 0, 0)]
    [InlineData("missing", 0, 0, 1, 0)]
    [InlineData("conflicting", 0, 0, 0, 1)]
    public async Task Coverage_report_distinguishes_eligible_unresolved_and_conflicting_native_pairs(
        string scenario, int eligible, int linked, int unresolved, int conflicting)
    {
        using var fixture = await EditionEvidenceFixture.CreateAsync();
        if (scenario == "missing") fixture.Igdb.Answers[(26, EditionEvidenceFixture.OfferId)] = (IgdbEditionMatchStatus.Missing, null);
        if (scenario == "conflicting") fixture.Igdb.Answers[(1, "620")] = (IgdbEditionMatchStatus.Edition, 43);
        var service = fixture.SyncService();
        Assert.Equal(linked, await service.SyncAsync());
        Assert.Equal(new GamesDbIdentitySyncResult(1, eligible, linked, unresolved, conflicting, 0), service.LastResult);
    }

    [Fact]
    public async Task Exact_native_ids_supply_independent_idempotent_evidence_without_overwriting_legacy_release_fields()
    {
        using var fixture = await EditionEvidenceFixture.CreateAsync();
        var answers = await fixture.AcquireAsync();
        var epic = Assert.IsType<ReleaseEditionEvidence>(answers[new("epic", EditionEvidenceFixture.CatalogId)].Evidence);
        Assert.Equal(42, epic.EditionGameId);
        Assert.Equal(1, epic.VersionParentId);
        Assert.Contains((26, EditionEvidenceFixture.OfferId), fixture.Igdb.Requested);
        Assert.Contains((26, EditionEvidenceFixture.PageId), fixture.Igdb.Requested);
        Assert.DoesNotContain((26, "620"), fixture.Igdb.Requested);
        Assert.DoesNotContain((26, EditionEvidenceFixture.CatalogId), fixture.Igdb.Requested);
        Assert.Null((await fixture.Releases.GetAsync(fixture.Epic.ReleaseId))!.IgdbVersionId);
        Assert.Single(await fixture.Releases.GetExternalIdsAsync(fixture.Epic.ReleaseId));
        var repeated = await fixture.AcquireAsync();
        Assert.Equal(epic.Id, repeated[new("epic", EditionEvidenceFixture.CatalogId)].Evidence!.Id);
        using var lease = fixture.Db.Factory.Lease();
        Assert.Equal(2, await lease.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM release_edition_evidence"));
    }

    [Theory]
    [InlineData("catalogId", "other", false)]
    [InlineData("appName", "WrongArtifact", true)]
    [InlineData("namespace", "wrong-namespace", true)]
    public void Cms_requires_the_exact_native_triple(string field, string replacement, bool conflict)
    {
        var payload = EditionEvidenceFixture.Cms;
        var old = field switch { "catalogId" => EditionEvidenceFixture.CatalogId, "appName" => "Bluebird", _ => EditionEvidenceFixture.Namespace };
        payload = payload.Replace("\"" + field + "\":\"" + old + "\"", "\"" + field + "\":\"" + replacement + "\"");
        Assert.True(StorefrontClient.TryParseEpicEditionIds(payload, Target(), out var ids, out var conflicting));
        Assert.Empty(ids);
        Assert.Equal(conflict, conflicting);
    }

    [Theory]
    [InlineData(IgdbEditionMatchStatus.Missing, null, false)]
    [InlineData(IgdbEditionMatchStatus.NotEdition, 42L, false)]
    [InlineData(IgdbEditionMatchStatus.Ambiguous, null, true)]
    public async Task Steam_metadata_route_cannot_fill_absent_or_ambiguous_epic_edition_evidence(
        IgdbEditionMatchStatus status, long? id, bool conflict)
    {
        using var fixture = await EditionEvidenceFixture.CreateAsync();
        fixture.Igdb.Answers[(26, EditionEvidenceFixture.OfferId)] = (status, id);
        var answers = await fixture.AcquireAsync();
        var epic = answers[new("epic", EditionEvidenceFixture.CatalogId)];
        Assert.Null(epic.Evidence);
        Assert.Equal(conflict, epic.Conflicting);
        Assert.NotNull(answers[new("steam", "620")].Evidence);
    }

    [Fact]
    public async Task Conflicting_native_offer_and_page_cannot_qualify_and_an_unanswered_alias_is_not_a_negative()
    {
        using var fixture = await EditionEvidenceFixture.CreateAsync();
        fixture.Igdb.Answers[(26, EditionEvidenceFixture.PageId)] = (IgdbEditionMatchStatus.Edition, 43);
        var answers = await fixture.AcquireAsync();
        Assert.True(answers[new("epic", EditionEvidenceFixture.CatalogId)].Conflicting);
        fixture.Igdb.Unavailable.Add((26, EditionEvidenceFixture.PageId));
        answers = await fixture.AcquireAsync();
        Assert.Null(answers[new("epic", EditionEvidenceFixture.CatalogId)].Evidence);
    }

    [Theory]
    [InlineData(IgdbEditionMatchStatus.Missing, null, false)]
    [InlineData(IgdbEditionMatchStatus.Edition, 43L, true)]
    public async Task Every_native_identifier_on_one_release_must_resolve_without_conflict(
        IgdbEditionMatchStatus status, long? edition, bool conflict)
    {
        using var fixture = await EditionEvidenceFixture.CreateAsync();
        await fixture.Releases.AddExternalIdAsync(new()
        { ReleaseId = fixture.Steam.ReleaseId, Provider = "steam", ProviderId = "621" });
        fixture.Igdb.Answers[(1, "621")] = (status, edition);
        var answers = await fixture.Acquirer.AcquireAsync([fixture.Epic, fixture.Steam, fixture.Steam with { ProviderId = "621" }]);
        Assert.Null(answers[new("steam", "620")].Evidence);
        Assert.Equal(conflict, answers[new("steam", "620")].Conflicting);
        Assert.Null(answers[new("steam", "621")].Evidence);
    }

    [Theory]
    [InlineData("cms")]
    [InlineData("native-lookup")]
    [InlineData("launch-local")]
    [InlineData("launch-remote")]
    [InlineData("external-id")]
    [InlineData("edition-row")]
    public async Task Link_transaction_refuses_changed_sources_or_release_identity(string changed)
    {
        using var fixture = await EditionEvidenceFixture.CreateAsync();
        var answers = await fixture.AcquireAsync();
        var request = fixture.Request(answers);
        switch (changed)
        {
            case "cms": await fixture.StoreCache.SaveAsync("epic-edition-v1:fez", "{}", fixture.Now); break;
            case "native-lookup": await fixture.Cache.SetAsync("edition-fixture", "26:" + EditionEvidenceFixture.PageId, "{}", fixture.Now); break;
            case "launch-local": await fixture.Cache.SetAsync(SqliteEpicLaunchKeyStore.Provider, EditionEvidenceFixture.CatalogId, "{}", fixture.Now); break;
            case "launch-remote": await fixture.Cache.SetAsync(SqliteEpicCatalogCache.Provider, EditionEvidenceFixture.CatalogId, "{}", fixture.Now); break;
            default:
                using (var lease = fixture.Db.Factory.Lease())
                    await lease.Connection.ExecuteAsync(changed == "external-id"
                        ? "DELETE FROM external_ids WHERE provider='epic'"
                        : "UPDATE release_edition_evidence SET edition_game_id=100");
                break;
        }
        await Assert.ThrowsAsync<IdentityLinkRefusedException>(() => fixture.Links.LinkAsync(request));
        Assert.Empty(await fixture.Links.GetActsAsync());
        Assert.Empty(await fixture.Links.GetHistoryAsync());
    }

    [Fact]
    public async Task Expired_evidence_and_failed_current_release_validation_do_not_record_or_link()
    {
        using var fixture = await EditionEvidenceFixture.CreateAsync();
        var answers = await fixture.AcquireAsync();
        var evidence = answers[new("steam", "620")].Evidence!;
        Assert.Null(await fixture.Evidence.RecordAsync(evidence with { ValidUntilUtc = DateTime.UtcNow.AddSeconds(-1) }));
        Assert.Null(await fixture.Evidence.RecordAsync(evidence with { WorkId = fixture.Epic.WorkId }));
        await Assert.ThrowsAsync<IdentityLinkRefusedException>(() => fixture.Links.LinkAsync(fixture.Request(answers) with
        { ExpectedEditionEvidence = [evidence with { ValidUntilUtc = DateTime.UtcNow.AddSeconds(-1) }] }));
    }

    private static EpicEditionTarget Target() => new(EditionEvidenceFixture.Namespace, EditionEvidenceFixture.CatalogId, "Bluebird");
}
