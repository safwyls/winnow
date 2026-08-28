using Winnow.Ingest.Gog;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Reader tests against the sanitized real fixtures in tests/fixtures/gog/.
/// </summary>
public class GalaxyLibraryReaderTests
{
    private static IReadOnlyList<GogLibraryEntry> ReadFixture()
    {
        using var tree = GalaxyFixtureTree.Create();
        using var snapshot = GalaxyDatabaseSnapshot.Take(tree.DatabasePath);
        Assert.NotNull(snapshot);
        return new GalaxyLibraryReader().Read(snapshot);
    }

    [Fact]
    public void The_steam_release_sitting_in_galaxys_library_is_excluded()
    {
        // THE critical finding. LibraryReleases holds steam_1091500 (Cyberpunk
        // 2077) with LicensedReleases.isOwned = 1, alongside the gog_ rows. A
        // reader that does not filter on the releaseKey prefix re-imports a game
        // the Steam ingest already owns — and on a machine with a connected Steam
        // integration that is the whole ~926-game Steam library, duplicated.
        var entries = ReadFixture();

        Assert.DoesNotContain(entries, e => e.ReleaseKey == GogFixtures.ContaminatingSteamReleaseKey);
        Assert.DoesNotContain(entries, e => e.ProductId == "1091500");
        Assert.All(entries, e => Assert.StartsWith("gog_", e.ReleaseKey, StringComparison.Ordinal));
    }

    [Fact]
    public void Neither_obvious_alternative_discriminator_would_have_worked()
    {
        // Recorded as a test so nobody re-derives the "obvious" filter later.
        using var tree = GalaxyFixtureTree.Create();
        using var snapshot = GalaxyDatabaseSnapshot.Take(tree.DatabasePath);
        Assert.NotNull(snapshot);

        using var connection = snapshot.OpenReadOnly();
        using var command = connection.CreateCommand();

        // Platforms is the static list of integrations Galaxy SUPPORTS and has no
        // 'gog' row at all — a join to it to find GOG games returns nothing.
        command.CommandText = "SELECT COUNT(*) FROM Platforms WHERE name = 'gog';";
        Assert.Equal(0L, (long)(command.ExecuteScalar() ?? 0L));

        // Every PlatformConnections row says Disconnected, while the library
        // still holds an owned Steam release. Nothing prunes on disconnect.
        command.CommandText = "SELECT COUNT(*) FROM PlatformConnections WHERE connectionState <> 'Disconnected';";
        Assert.Equal(0L, (long)(command.ExecuteScalar() ?? 0L));

        command.CommandText =
            "SELECT COUNT(*) FROM LibraryReleases lr JOIN LicensedReleases lic ON lic.libraryId = lr.id "
            + "WHERE lr.releaseKey = 'steam_1091500' AND lic.isOwned = 1;";
        Assert.Equal(1L, (long)(command.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public void Playtime_is_minutes_and_last_played_parses_as_utc()
    {
        var entries = ReadFixture();

        var gwent = Assert.Single(entries, e => e.ProductId == GogFixtures.GwentProductId);
        Assert.Equal(54, gwent.PlaytimeMinutes);

        // Proven UTC: the myFriendsActivity GamePiece for the same release
        // carries last_played_date 1498879936, which is this instant in UTC to
        // the second.
        Assert.Equal(new DateTime(2017, 7, 1, 3, 32, 16, DateTimeKind.Utc), gwent.LastPlayedUtc);
        Assert.Equal(DateTimeKind.Utc, gwent.LastPlayedUtc!.Value.Kind);
    }

    [Fact]
    public void Playtime_survives_uninstall_and_is_never_gated_on_install_state()
    {
        var witcher3 = Assert.Single(ReadFixture(), e => e.ProductId == GogFixtures.Witcher3ProductId);

        Assert.False(witcher3.IsInstalled);
        Assert.Null(witcher3.InstallationPath);
        Assert.Equal(50, witcher3.PlaytimeMinutes);
        Assert.Equal(new DateTime(2018, 11, 20, 14, 18, 42, DateTimeKind.Utc), witcher3.LastPlayedUtc);
    }

    [Fact]
    public void A_zero_minute_row_with_no_last_played_row_is_the_never_played_shape()
    {
        // A GameTimes row exists for every release; its existence is not evidence
        // of play, and a missing LastPlayedDates row is not an error.
        var tyrian = Assert.Single(ReadFixture(), e => e.ProductId == GogFixtures.TyrianProductId);

        Assert.Equal(0, tyrian.PlaytimeMinutes);
        Assert.Null(tyrian.LastPlayedUtc);
    }

    [Fact]
    public void The_title_comes_from_the_GamePiece_and_needs_the_user_in_the_lookup()
    {
        // GamePieces.userId is NOT NULL for the 'title' type — leave the user out
        // of the lookup and every title comes back empty.
        var entries = ReadFixture();

        Assert.Equal("GWENT: The Witcher Card Game",
            Assert.Single(entries, e => e.ProductId == GogFixtures.GwentProductId).Title);
        Assert.Equal("The Witcher 3: Wild Hunt - Complete Edition",
            Assert.Single(entries, e => e.ProductId == GogFixtures.Witcher3ProductId).Title);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Title)));
    }

    [Fact]
    public void Install_state_comes_from_InstalledBaseProducts_via_ProductsToReleaseKeys()
    {
        var gwent = Assert.Single(ReadFixture(), e => e.ProductId == GogFixtures.GwentProductId);

        Assert.True(gwent.IsInstalled);
        Assert.Equal(
            @"C:\Program Files\GOG Galaxy\Games\GWENT The Witcher Card Game", gwent.InstallationPath);
        Assert.Equal(59534219748634025L, gwent.BuildId);

        // The DB's installationDate is UTC. The registry's INSTALLDATE for the
        // same install is LOCAL time and reads seven hours earlier on a UTC-7
        // machine; mixing them shifts every GOG date by the user's offset.
        Assert.Equal(new DateTime(2026, 8, 26, 6, 17, 36, DateTimeKind.Utc), gwent.InstalledAtUtc);
    }

    [Fact]
    public void Dlc_rows_are_flagged_rather_than_silently_dropped_by_the_reader()
    {
        var newGamePlus = Assert.Single(ReadFixture(), e => e.ProductId == GogFixtures.NewGamePlusProductId);

        Assert.True(newGamePlus.IsDlc);
    }

    [Fact]
    public void Purchase_date_is_read_and_the_release_keys_table_is_not_an_ownership_list()
    {
        var entries = ReadFixture();

        Assert.Equal(
            new DateTime(2016, 11, 28, 17, 40, 14, DateTimeKind.Utc),
            Assert.Single(entries, e => e.ProductId == GogFixtures.GwentProductId).PurchasedAtUtc);

        // gog_2074191081 is in ReleaseKeys/ReleaseProperties/ProductsToReleaseKeys
        // but NOT in LibraryReleases — a known release the user does not own.
        Assert.DoesNotContain(entries, e => e.ProductId == "2074191081");
    }
}

public class GalaxyDatabaseSnapshotTests
{
    [Fact]
    public void A_snapshot_is_a_private_copy_that_is_deleted_on_dispose()
    {
        using var tree = GalaxyFixtureTree.Create();

        string copyPath;
        using (var snapshot = GalaxyDatabaseSnapshot.Take(tree.DatabasePath))
        {
            Assert.NotNull(snapshot);
            copyPath = snapshot.DatabasePath;
            Assert.NotEqual(tree.DatabasePath, copyPath);
            Assert.True(File.Exists(copyPath));
            Assert.Equal(40, snapshot.ReadUserVersion());
        }

        Assert.False(File.Exists(copyPath));
    }

    [Fact]
    public void Reading_a_wal_database_never_creates_wal_or_shm_beside_the_store_file()
    {
        // The hazard that makes copying mandatory rather than tidy: opening a WAL
        // database with mode=ro CREATES -wal and -shm files next to it. mode=ro
        // restricts writes to the DATABASE, not to the DIRECTORY. Pointed at
        // GOG's storage directory that is a write into a store-owned directory,
        // which §4.1 forbids absolutely.
        using var tree = GalaxyFixtureTree.Create(walMode: true);
        Assert.Equal([GogPaths.ClientDatabaseFileName], tree.StorageDirectoryEntries());

        using (var snapshot = GalaxyDatabaseSnapshot.Take(tree.DatabasePath))
        {
            Assert.NotNull(snapshot);
            var entries = new GalaxyLibraryReader().Read(snapshot);
            Assert.NotEmpty(entries);
        }

        Assert.Equal([GogPaths.ClientDatabaseFileName], tree.StorageDirectoryEntries());
    }

    [Fact]
    public void A_missing_database_yields_null_rather_than_throwing()
        => Assert.Null(GalaxyDatabaseSnapshot.Take(
            Path.Combine(Path.GetTempPath(), "winnow-does-not-exist", "galaxy-2.0.db")));
}

public class GogGameInfoReaderTests
{
    [Fact]
    public void Base_game_info_carries_the_installer_locale_title_not_the_store_title()
    {
        var info = new GogGameInfoReader().Read(GogFixtures.PathOf("goggame-1971477531.info"));

        Assert.NotNull(info);
        Assert.Equal("1971477531", info.GameId);
        Assert.Equal("1971477531", info.RootGameId);
        Assert.False(info.IsDlc);
        Assert.Equal("59534219748634025", info.BuildId);
        Assert.Equal("Gwent.exe", info.PrimaryPlayTaskPath);

        // The localisation trap, and it is the reverse of what you would guess:
        // this is the Polish title on an English install (installer_language =
        // english), because it is what the publisher stamped into that installer
        // build. Galaxy's GamePieces title is the canonical English one.
        Assert.Equal("GWINT: Wiedźmińska Gra Karciana", info.Name);
    }

    [Fact]
    public void GameId_differing_from_RootGameId_is_the_no_galaxy_dlc_discriminator()
    {
        var info = new GogGameInfoReader().Read(GogFixtures.PathOf("goggame-1430742983.info"));

        Assert.NotNull(info);
        Assert.Equal("1430742983", info.GameId);
        Assert.Equal("1207664643", info.RootGameId);
        Assert.True(info.IsDlc);
    }

    [Fact]
    public void Missing_file_returns_null_without_throwing()
        => Assert.Null(new GogGameInfoReader().Read(
            Path.Combine(Path.GetTempPath(), "winnow-does-not-exist", "goggame-1.info")));
}

public class GogRegistryFixtureTests
{
    [Fact]
    public void The_registry_gameName_is_the_polish_title_and_the_ids_agree_with_galaxy()
    {
        var games = RegFile.InstalledGames(GogFixtures.PathOf("gog-games.reg"));
        var gwent = Assert.Single(games);

        Assert.Equal(GogFixtures.GwentProductId, gwent.GameId);
        Assert.Equal(
            @"C:\Program Files\GOG Galaxy\Games\GWENT The Witcher Card Game", gwent.InstallPath);
        Assert.Equal("59534219748634025", gwent.BuildId);

        // Prefer the database's title and treat the registry as an id/path
        // source: this one is Polish AND diacritic-stripped, so it is not even
        // string-equal to the .info file's version of the "same" title.
        Assert.Equal("GWINT: Wiedzminska Gra Karciana", gwent.GameName);
        Assert.NotEqual("GWINT: Wiedźmińska Gra Karciana", gwent.GameName);

        // INSTALLDATE is LOCAL time; Galaxy's installationDate for this install
        // is 2026-08-26 06:17:36 UTC. Seven hours apart on a UTC-7 machine.
        Assert.Equal("2026-08-25 23:17:36", gwent.InstallDateLocal);
    }
}
