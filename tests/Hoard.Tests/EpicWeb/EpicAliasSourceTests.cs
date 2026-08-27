using System.Net;
using Hoard.Core.Domain;
using Hoard.Ingest.Epic;
using Xunit;

namespace Hoard.Tests.EpicWeb;

/// <summary>
/// <see cref="EpicArtifactAliasSource"/> once it reads the authenticated library
/// as well as the launcher's own files.
///
/// <para><b>The half of the bug this fixes.</b> The alias map is what turns a
/// stored Epic <c>catalogItemId</c> into the <c>AppName</c> that
/// <c>gamesdb.gog.com</c> keys on, and it was built entirely from
/// <c>catcache.bin</c> and the installed manifests. An entitlement the launcher
/// has never cached therefore had no alias, so no gamesdb hop, so no IGDB record,
/// so no name, year, cover or summary — for 29 of 99 Epic rows on the author's
/// library, that was not a low match rate but a question nobody could ask. The
/// library endpoint returns <c>appName</c> on every entitlement it owns.</para>
/// </summary>
public sealed class EpicAliasSourceTests
{
    private static EpicArtifactAliasSource Build(EpicWebTestHost? host, string? dataRoot)
        => new(
            new EpicCatalogReader(),
            new EpicManifestReader(),
            new EpicThirdPartyAppReader(),
            logger: null,
            dataRoot: dataRoot,
            account: host?.Client);

    [Fact]
    public async Task An_entitlement_the_launcher_has_never_cached_still_gets_an_alias()
    {
        using var tree = EpicFixtureTree.Create();
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var aliases = await Build(host, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Epic);

        // Known to catcache.bin, and unchanged.
        Assert.Equal(EpicFixturesWeb.FezAppName, aliases[EpicFixturesWeb.FezCatalogItemId]);

        // Known ONLY to the API. This is the row that had no route to IGDB.
        Assert.Equal("UE_4.0", aliases[EpicFixturesWeb.EngineCatalogItemId]);
        Assert.Equal("BluebirdChapter", aliases[EpicFixturesWeb.DlcCatalogItemId]);
    }

    [Fact]
    public async Task The_local_files_win_every_id_they_know()
    {
        // Additive only. Where both halves carry an id, the launcher's own
        // record stands: it describes what this machine will install and launch.
        using var tree = EpicFixtureTree.Create();
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var withApi = await Build(host, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Epic);
        var localOnly = await Build(null, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Epic);

        foreach (var (id, alias) in localOnly)
        {
            Assert.Equal(alias, withApi[id]);
        }

        Assert.True(withApi.Count > localOnly.Count);
    }

    [Fact]
    public async Task A_failed_library_fetch_leaves_the_local_aliases_exactly_as_they_were()
    {
        // The rule this codebase has already paid for twice: a source that
        // cannot answer must leave the row alone. An unreachable Epic must not
        // be able to shrink the alias map.
        using var tree = EpicFixtureTree.Create();
        using var host = new EpicWebTestHost((request, _) => request.Endpoint switch
        {
            EpicEndpoint.Token => FakeEpicHandler.Json(HttpStatusCode.OK, EpicFixturesWeb.Token()),
            _ => FakeEpicHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
        });
        await host.SignInAsync();

        var aliases = await Build(host, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Epic);
        var localOnly = await Build(null, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Epic);

        Assert.Equal(localOnly.Count, aliases.Count);
        Assert.Equal(EpicFixturesWeb.FezAppName, aliases[EpicFixturesWeb.FezCatalogItemId]);
    }

    [Fact]
    public async Task A_machine_with_no_launcher_can_still_be_served_entirely_by_the_API()
    {
        // The launcher's files are one source among several, not a
        // precondition. Before this change a missing data root returned an empty
        // map and stopped.
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        var missingRoot = Path.Combine(Path.GetTempPath(), "hoard-no-epic-" + Guid.NewGuid().ToString("N"));
        var aliases = await Build(host, missingRoot).GetAliasesAsync(ExternalIdProviders.Epic);

        Assert.Equal(EpicFixturesWeb.FezAppName, aliases[EpicFixturesWeb.FezCatalogItemId]);
        Assert.Equal("UE_4.0", aliases[EpicFixturesWeb.EngineCatalogItemId]);
    }

    [Fact]
    public async Task No_Epic_session_means_the_local_files_are_the_whole_story()
    {
        using var tree = EpicFixtureTree.Create();
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());

        // Deliberately not signed in.
        var aliases = await Build(host, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Epic);
        var localOnly = await Build(null, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Epic);

        Assert.Equal(localOnly.Count, aliases.Count);
        Assert.Equal(0, host.Handler.CountFor(EpicEndpoint.LibraryItems));
    }

    [Fact]
    public async Task Another_store_is_never_answered_for()
    {
        using var tree = EpicFixtureTree.Create();
        using var host = new EpicWebTestHost(EpicWebTestHost.HealthyCatalog());
        await host.SignInAsync();

        Assert.Empty(await Build(host, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Steam));
        Assert.Empty(await Build(host, tree.DataRoot).GetAliasesAsync(ExternalIdProviders.Gog));
    }
}
