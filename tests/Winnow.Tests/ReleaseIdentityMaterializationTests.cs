using Dapper;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class ReleaseIdentityMaterializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sqlite_numeric_and_null_metadata_reach_the_executable_index(bool populated)
    {
        using var harness = new SessionWatcherHarness();
        await harness.AddGameAsync("Unenriched first game", "FirstGame.exe");
        var game = await harness.AddGameAsync("Observed game", "ObservedGame.exe");
        var ownerships = new OwnershipRepository(harness.Factory);
        var releases = new ReleaseRepository(harness.Factory);
        var ownership = (await ownerships.GetAsync(game.OwnershipId))!;
        var release = (await releases.GetAsync(ownership.ReleaseId))!;
        int? year = populated ? 2025 : null;
        int? storeType = populated ? 0 : null;
        long? igdbId = populated ? 3_000_000_000L : null;

        using (var lease = harness.Factory.Lease())
        {
            await lease.Connection.ExecuteAsync("""
                UPDATE works SET first_release_year = @year, steam_store_type = @storeType,
                    name_is_provisional = @populated, igdb_id = @igdbId,
                    igdb_parent_id = @igdbId, igdb_version_parent_id = @igdbId
                WHERE id = @WorkId;
                UPDATE releases SET platform = @platform, igdb_version_id = @igdbId
                WHERE id = @ReleaseId;
                """, new
                {
                    year, storeType, populated, igdbId, release.WorkId,
                    ReleaseId = release.Id, platform = populated ? "windows" : null,
                });

            // Microsoft.Data.Sqlite returns boxed Int64 values for INTEGER columns,
            // including booleans and nullable Int32 metadata when populated.
            using var command = lease.Connection.CreateCommand();
            command.CommandText = "SELECT name_is_provisional, first_release_year FROM works WHERE id = $id";
            command.Parameters.AddWithValue("$id", release.WorkId);
            using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.IsType<long>(reader.GetValue(0));
            if (populated)
                Assert.IsType<long>(reader.GetValue(1));
            else
                Assert.True(reader.IsDBNull(1));
        }

        if (populated)
            await new ReleaseYearEvidenceRepository(harness.Factory).ObserveSteamAsync(release.Id, game.SteamAppId, 2026);

        await releases.InsertAsync(new Release { WorkId = release.WorkId, Name = "Unowned edition" });
        await ownerships.InsertAsync(new Ownership { ReleaseId = release.Id, Store = "gog" });

        var identities = await releases.GetIdentitiesAsync();
        Assert.Equal(3, identities.Count);
        var identity = Assert.Single(identities, row => row.ReleaseId == release.Id);
        Assert.Equal(year, identity.FirstReleaseYear);
        Assert.Equal(populated ? 2026 : (int?)null, identity.EditionReleaseYear);
        Assert.Equal(storeType, identity.SteamStoreType);
        Assert.Equal(populated, identity.NameIsProvisional);
        Assert.Equal(igdbId, identity.IgdbId);
        Assert.Equal(igdbId, identity.IgdbParentId);
        Assert.Equal(igdbId, identity.IgdbVersionParentId);
        Assert.Equal(game.SteamAppId, identity.SteamAppId);
        Assert.True(identity.IsOwned);
        Assert.False(Assert.Single(identities, row => row.ReleaseName == "Unowned edition").IsOwned);
        var loadedRelease = (await releases.GetAsync(release.Id))!;
        Assert.Equal(populated ? "windows" : null, loadedRelease.Platform);
        Assert.Equal(igdbId, loadedRelease.IgdbVersionId);

        var index = await harness.IndexBuilder.BuildAsync();
        Assert.Equal(game.OwnershipId, index.Match(game.Exe("ObservedGame.exe"), "ObservedGame"));
        Assert.Equal(game.OwnershipId,
            index.MatchSteamCompatibilityDataPath($"/steamapps/compatdata/{game.SteamAppId}"));
    }
}
