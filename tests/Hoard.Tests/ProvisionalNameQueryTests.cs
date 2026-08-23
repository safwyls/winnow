using Hoard.Core.Domain;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The enrichment sweep's entry point: which works still need a real title, and
/// what external id can be used to look each one up. Backed by the partial index
/// migration 0002 added for this query.
/// </summary>
public class ProvisionalNameQueryTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;

    public ProvisionalNameQueryTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(long WorkId, long ReleaseId)> SeedAsync(
        string name, bool provisional, string appId)
    {
        var workId = await _works.InsertAsync(new Work { Name = name, NameIsProvisional = provisional });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = appId,
        });
        return (workId, releaseId);
    }

    [Fact]
    public async Task Returns_only_works_that_still_hold_a_placeholder()
    {
        var provisional = await SeedAsync("App 33310", provisional: true, "33310");
        await SeedAsync("Portal 2", provisional: false, "620");

        var targets = await _works.GetProvisionalNameTargetsAsync(ExternalIdProviders.Steam);

        var target = Assert.Single(targets);
        Assert.Equal(provisional.WorkId, target.WorkId);
        Assert.Equal(provisional.ReleaseId, target.ReleaseId);
        Assert.Equal("33310", target.ProviderId);
        Assert.Equal(ExternalIdProviders.Steam, target.Provider);
    }

    /// <summary>
    /// The release id is carried alongside the work id because releases.name is
    /// NOT NULL and holds the same placeholder, with no flag of its own — the
    /// two have to be promoted together or the release keeps "App 33310"
    /// forever with nothing to find it by.
    /// </summary>
    [Fact]
    public async Task Carries_the_release_id_so_both_names_can_be_promoted()
    {
        var seeded = await SeedAsync("App 1250", provisional: true, "1250");

        var target = Assert.Single(await _works.GetProvisionalNameTargetsAsync(ExternalIdProviders.Steam));
        Assert.Equal(seeded.ReleaseId, target.ReleaseId);

        await _works.UpdateNameAsync(target.WorkId, "Killing Floor", nameIsProvisional: false);
        await _releases.UpdateNameAsync(target.ReleaseId, "Killing Floor");

        Assert.Equal("Killing Floor", (await _releases.GetAsync(target.ReleaseId))!.Name);
    }

    [Fact]
    public async Task Promoted_works_drop_out_so_a_second_run_has_nothing_to_do()
    {
        var seeded = await SeedAsync("App 240", provisional: true, "240");

        await _works.UpdateNameAsync(seeded.WorkId, "Counter-Strike: Source", nameIsProvisional: false);

        Assert.Empty(await _works.GetProvisionalNameTargetsAsync(ExternalIdProviders.Steam));
    }

    /// <summary>
    /// A work whose external id belongs to another provider must not be handed
    /// to the Steam enrichment pass — its appid would be meaningless there.
    /// </summary>
    [Fact]
    public async Task Filters_by_provider()
    {
        var workId = await _works.InsertAsync(new Work { Name = "App 7", NameIsProvisional = true });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = "App 7" });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Gog,
            ProviderId = "7",
        });

        Assert.Empty(await _works.GetProvisionalNameTargetsAsync(ExternalIdProviders.Steam));
        Assert.Single(await _works.GetProvisionalNameTargetsAsync(ExternalIdProviders.Gog));
    }
}
