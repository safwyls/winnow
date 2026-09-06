using System.Globalization;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Cover tile primary actions, with URIs asserted literally.
/// </summary>
public sealed class TileActionsTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    // ══ Play / Install, per store ═══════════════════════════════════════════

    [Fact]
    public void Steam_on_disk_plays_and_off_disk_installs()
    {
        Assert.Equal("Play", Tile(ExternalIdProviders.Steam, installed: true).PrimaryActionLabel);
        Assert.True(Tile(ExternalIdProviders.Steam, installed: true).IsPlayAction);
        Assert.False(Tile(ExternalIdProviders.Steam, installed: true).IsInstallAction);
        Assert.Equal(
            "steam://run/620",
            Tile(ExternalIdProviders.Steam, installed: true).PrimaryAction!.Uri);

        Assert.Equal("Install", Tile(ExternalIdProviders.Steam, installed: false).PrimaryActionLabel);
        Assert.False(Tile(ExternalIdProviders.Steam, installed: false).IsPlayAction);
        Assert.True(Tile(ExternalIdProviders.Steam, installed: false).IsInstallAction);
        Assert.Equal(
            "steam://install/620",
            Tile(ExternalIdProviders.Steam, installed: false).PrimaryAction!.Uri);
    }

    /// <summary>
    /// GOG plays through <c>goggalaxy://launchGame/gog_&lt;productId&gt;</c>,
    /// lowercased by the <see cref="Uri"/> round trip.
    /// </summary>
    [Fact]
    public void Gog_on_disk_plays_through_galaxy()
    {
        var tile = Tile(ExternalIdProviders.Gog, installed: true);

        Assert.Equal("Play", tile.PrimaryActionLabel);
        Assert.Equal("goggalaxy://launchgame/gog_1971477531", tile.PrimaryAction!.Uri);
    }

    [Fact]
    public void Gog_off_disk_opens_its_install_screen()
    {
        var tile = Tile(ExternalIdProviders.Gog, installed: false);

        Assert.Equal("Install", tile.PrimaryActionLabel);
        Assert.Equal("goggalaxy://installationscreen/1971477531", tile.PrimaryAction!.Uri);
    }

    /// <summary>
    /// The exact URI the Epic Games Launcher wrote into its own desktop shortcut
    /// for Fez, reproduced from the three ids this app holds. The catalog item
    /// id in it is the one this database stores in <c>external_ids</c>.
    /// </summary>
    [Fact]
    public void Epic_on_disk_plays_through_the_launchers_composite_key()
    {
        var tile = Tile(ExternalIdProviders.Epic, installed: true);

        Assert.Equal("Play", tile.PrimaryActionLabel);
        Assert.Equal(
            "com.epicgames.launcher://apps/41f47fd0d3e248bc938a5815d6d64daa"
            + "%3A7a70b499513441c792b541d53505e0b2%3ABluebird?action=launch&silent=true",
            tile.PrimaryAction!.Uri);
    }

    /// <summary>
    /// The label is Install, the kind is <see cref="GameLinkKind.Install"/>,
    /// and the literal URI carries <c>?action=install</c> with Fez's real three
    /// ids. The URI is asserted literally because the verb is undocumented — the
    /// measured string is the only authority for it, and the documented
    /// <c>installer</c> is exactly the wrong "fix" a future reader might apply.
    /// </summary>
    [Fact]
    public void Epic_off_disk_installs_through_the_launchers_composite_key()
    {
        var tile = Tile(ExternalIdProviders.Epic, installed: false);

        Assert.Equal("Install", tile.PrimaryActionLabel);
        Assert.Equal(
            "com.epicgames.launcher://apps/41f47fd0d3e248bc938a5815d6d64daa"
            + "%3A7a70b499513441c792b541d53505e0b2%3ABluebird?action=install",
            tile.PrimaryAction!.Uri);
        Assert.Equal(GameLinkKind.Install, tile.PrimaryAction!.Kind);
    }

    /// <summary>
    /// The §10.3 guard: with no complete launch key, nothing is drawn — no
    /// primary action, no label. After the ingest fix essentially every Epic
    /// row holds its triple, so this case should be empty in practice; the
    /// guard is the point, and it must not rot just because nothing hits it.
    /// </summary>
    [Fact]
    public void Epic_off_disk_without_all_three_ids_draws_no_install_button()
    {
        var tile = Tile(ExternalIdProviders.Epic, installed: false, withEpicKey: false);

        Assert.Null(tile.EpicLaunchKey);
        Assert.Null(tile.PrimaryAction);
        Assert.False(tile.HasPrimaryAction);
        Assert.Equal(string.Empty, tile.PrimaryActionLabel);
    }

    /// <summary>
    /// The same missing-key case seen from Band 3: no primary action, no links,
    /// and the sentence drawn is the missing-identifier one, not a route-shaped
    /// one. This test pins the retirement of <c>NoInstallRoute</c> — the honest
    /// replacement is <see cref="NoWayIn.NoStoreId"/>, not a new route reason.
    /// </summary>
    [Fact]
    public void Epic_off_disk_with_no_launch_key_names_the_id_it_lacks()
    {
        var tile = Tile(ExternalIdProviders.Epic, installed: false, withEpicKey: false);
        var details = new GameDetailsViewModel(tile, "Started", [], Now);

        Assert.Equal(NoWayIn.NoStoreId, tile.NoWayIn);
        Assert.False(details.HasPrimaryAction);
        Assert.False(details.HasLinks);
        Assert.True(details.HasNoWayInSentence);
        Assert.Equal(GameActionBandCopy.NoStoreId, details.NoWayInSentence);
    }

    /// <summary>
    /// A band that has a primary action or at least one link must never
    /// produce a no-way-in sentence. Showing a reason alongside a working
    /// button would contradict the button.
    /// </summary>
    [Theory]
    [InlineData(ExternalIdProviders.Steam, true)]
    [InlineData(ExternalIdProviders.Steam, false)]
    [InlineData(ExternalIdProviders.Steam, null)]
    [InlineData(ExternalIdProviders.Gog, true)]
    [InlineData(ExternalIdProviders.Gog, false)]
    [InlineData(ExternalIdProviders.Gog, null)]
    [InlineData(ExternalIdProviders.Epic, true)]
    [InlineData(ExternalIdProviders.Epic, false)]
    public void A_store_that_can_be_reached_says_nothing_about_why_it_cannot(string store, bool? installed)
    {
        var tile = Tile(store, installed);
        var details = new GameDetailsViewModel(tile, "Started", [], Now);

        Assert.Equal(NoWayIn.None, tile.NoWayIn);
        Assert.False(details.HasNoWayInSentence);
        Assert.Null(details.NoWayInSentence);
    }

    /// <summary>
    /// An Epic copy with a launch key but a null install state must report
    /// <see cref="NoWayIn.InstallStateUnknown"/>, not the missing-id reason.
    /// The install state is three-valued and the third value is "nothing
    /// looked", which the sentence must say rather than hide.
    /// </summary>
    [Fact]
    public void An_epic_copy_whose_install_state_nobody_read_is_named_as_that()
    {
        var tile = Tile(ExternalIdProviders.Epic, installed: null);
        var details = new GameDetailsViewModel(tile, "Started", [], Now);

        Assert.Equal(NoWayIn.InstallStateUnknown, tile.NoWayIn);
        Assert.Equal(GameActionBandCopy.InstallStateUnknown, details.NoWayInSentence);
    }

    /// <summary>
    /// An installed Epic copy with no launch key cannot play and has no links,
    /// so the band must name the missing store identifier as the reason. The
    /// key arrives via a background catalogue backfill, so this is "not yet"
    /// rather than "never".
    /// </summary>
    [Fact]
    public void An_epic_copy_on_disk_with_no_launch_key_names_the_id_it_lacks()
    {
        var tile = Tile(ExternalIdProviders.Epic, installed: true, withEpicKey: false);
        var details = new GameDetailsViewModel(tile, "Started", [], Now);

        Assert.Equal(NoWayIn.NoStoreId, tile.NoWayIn);
        Assert.Equal(GameActionBandCopy.NoStoreId, details.NoWayInSentence);
    }

    /// <summary>
    /// Both <c>TODO(docs-writer)</c> and <c>PLACEHOLDER_*</c> markers have
    /// reached a user of this project. This test guards every sentence in
    /// <see cref="GameActionBandCopy"/> against both, so a stub that
    /// survives a commit is caught before it ships.
    /// </summary>
    [Fact]
    public void No_sentence_is_ever_a_placeholder()
    {
        foreach (var reason in Enum.GetValues<NoWayIn>())
        {
            var sentence = GameActionBandCopy.NoWayInSentence(reason);

            if (reason == NoWayIn.None)
            {
                Assert.Null(sentence);
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("TODO", sentence, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PLACEHOLDER", sentence, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ══ The third install state ═════════════════════════════════════════════

    /// <summary>
    /// Null install state is "nothing looked", which is neither Play nor
    /// Install. Every store declines rather than guessing — folding null into
    /// false once cost this project the whole library's install state, and here
    /// it would cost the button its honesty in both directions at once.
    /// </summary>
    [Theory]
    [InlineData(ExternalIdProviders.Steam)]
    [InlineData(ExternalIdProviders.Gog)]
    [InlineData(ExternalIdProviders.Epic)]
    public void An_unknown_install_state_is_named_neither_way(string store)
    {
        var tile = Tile(store, installed: null);

        Assert.Null(tile.Installed);
        Assert.False(tile.IsOnDisk);
        Assert.Null(tile.PrimaryAction);
        Assert.Equal(string.Empty, tile.PrimaryActionLabel);
    }

    /// <summary>And the detail panel drops the chip rather than saying "Unknown".</summary>
    [Fact]
    public void An_unknown_install_state_renders_no_chip_in_the_detail_panel()
    {
        var known = new GameDetailsViewModel(
            Tile(ExternalIdProviders.Steam, installed: false), "Never played", [], Now);
        var unknown = new GameDetailsViewModel(
            Tile(ExternalIdProviders.Steam, installed: null), "Never played", [], Now);

        Assert.True(known.HasInstallState);
        Assert.Equal("Not installed", known.InstallText);
        Assert.False(unknown.HasInstallState);
    }

    // ══ Ids that are not ids ════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("12a")]
    [InlineData("../7")]
    [InlineData("1 7")]
    [InlineData(null)]
    public void A_malformed_gog_product_id_never_reaches_a_url(string? productId)
    {
        Assert.False(StoreActions.IsGogProductId(productId));
        Assert.Null(StoreActions.PrimaryFor(
            ExternalIdProviders.Gog, installed: true, null, productId, null));
    }

    [Theory]
    [InlineData("ns/../evil", "7a70b4", "Bluebird")]
    [InlineData("ns", "7a70b4?x=1", "Bluebird")]
    [InlineData("ns", "7a70b4", "Blue bird")]
    [InlineData("", "7a70b4", "Bluebird")]
    [InlineData(null, "7a70b4", "Bluebird")]
    public void A_malformed_epic_key_is_no_key(string? ns, string? catalogItemId, string? artifact)
        => Assert.Null(EpicLaunchKey.Create(ns, catalogItemId, artifact));

    [Fact]
    public void An_epic_tile_with_no_launch_key_offers_no_action_even_when_installed()
    {
        var tile = Tile(ExternalIdProviders.Epic, installed: true, withEpicKey: false);

        Assert.Null(tile.EpicLaunchKey);
        Assert.Null(tile.PrimaryAction);
    }

    // ══ The launcher schemes, at the security boundary ══════════════════════

    [Theory]
    [InlineData("com.epicgames.launcher://apps/a%3Ab%3AC?action=launch&silent=true")]
    [InlineData("com.epicgames.launcher://apps/a%3Ab%3AC?action=install")]
    [InlineData("goggalaxy://launchGame/gog_1")]
    [InlineData("goggalaxy://installationScreen/1")]
    public void The_two_launcher_schemes_are_openable(string uri)
        => Assert.NotNull(GameLink.Create("Go", uri));

    /// <summary>
    /// Adding two schemes must not have widened the door. The refusals that
    /// mattered before still matter, and a near-miss on a launcher scheme is not
    /// a launcher scheme.
    /// </summary>
    [Theory]
    [InlineData("com.epicgames.launcher.evil://apps/x")]
    [InlineData("goggalaxyx://launchGame/gog_1")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    public void Everything_adjacent_to_them_is_still_refused(string uri)
        => Assert.Null(GameLink.Create("Go", uri));

    // ══ Secondary links ═════════════════════════════════════════════════════

    [Fact]
    public void Gog_gets_the_one_route_that_was_watched_working()
    {
        var links = StoreActions.LinksFor(ExternalIdProviders.Gog, null, "1971477531");

        var link = Assert.Single(links);
        Assert.Equal("Show in GOG Galaxy", link.Label);
        Assert.Equal("goggalaxy://opengameview/gog_1971477531", link.Uri);
    }

    /// <summary>
    /// Epic gets no store link: the in-launcher route exists but needs a product
    /// slug, and nothing in this database holds one. Absent, not invented.
    /// </summary>
    [Fact]
    public void Epic_gets_no_links()
        => Assert.Empty(StoreActions.LinksFor(ExternalIdProviders.Epic, null, null));

    [Fact]
    public void Steam_keeps_its_store_page_and_patch_notes()
    {
        var links = StoreActions.LinksFor(ExternalIdProviders.Steam, "620", null);

        Assert.Equal(["Store page", "All patch notes"], links.Select(l => l.Label));
    }

    /// <summary>
    /// The detail panel offers the tile's action rather than deriving a second
    /// one. Two implementations of "which one is this" is how one surface ends
    /// up saying Play while the other says Install.
    /// </summary>
    [Fact]
    public void The_detail_panel_offers_exactly_the_tiles_action()
    {
        var tile = Tile(ExternalIdProviders.Gog, installed: true);
        var details = new GameDetailsViewModel(tile, "Started", [], Now);

        Assert.Same(tile.PrimaryAction, details.PrimaryAction);
        Assert.True(details.HasLinks);
    }

    // ══ Launching (M3b) ════════════════════════════════════════════════

    /// <summary>
    /// Every tile launches through the library's own command, not its own view.
    /// </summary>
    [Fact]
    public async Task Every_tile_launches_through_the_librarys_own_command()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anvil");
        var library = await fixture.LoadAsync();

        var anvil = library.VisibleTiles.Single(t => t.Title == "Anvil");

        Assert.Same(library.LaunchCommand, anvil.PrimaryActionCommand);
    }

    /// <summary>
    /// The detail panel presses the same command on the same tile. Two routes to
    /// one launch, and only one of them declaring an intent would be a Play
    /// button whose attribution depended on which surface it was clicked from.
    /// </summary>
    [Fact]
    public void The_detail_panel_launches_through_the_same_command_as_the_tile()
    {
        var tile = Tile(ExternalIdProviders.Steam, installed: true);
        tile.PrimaryActionCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });

        var details = new GameDetailsViewModel(tile, "Started", [], Now);

        Assert.Same(tile, details.Tile);
        Assert.Same(tile.PrimaryActionCommand, details.Tile.PrimaryActionCommand);
    }

    /// <summary>
    /// Play is a launch; Install is not. Only the first is worth declaring an
    /// attribution window for or waiting on — a download produces no process for
    /// minutes or hours — and the string on the button must not be what decides
    /// which is which, or a rename becomes a bug.
    /// </summary>
    [Fact]
    public void The_kind_of_action_is_carried_by_the_link_not_by_its_label()
    {
        Assert.True(Tile(ExternalIdProviders.Steam, installed: true).PrimaryAction!.StartsGame);
        Assert.False(Tile(ExternalIdProviders.Steam, installed: false).PrimaryAction!.StartsGame);

        Assert.True(Tile(ExternalIdProviders.Gog, installed: true).PrimaryAction!.StartsGame);
        Assert.False(Tile(ExternalIdProviders.Gog, installed: false).PrimaryAction!.StartsGame);

        Assert.True(Tile(ExternalIdProviders.Epic, installed: true).PrimaryAction!.StartsGame);
        Assert.False(Tile(ExternalIdProviders.Epic, installed: false).PrimaryAction!.StartsGame);

        // A store page is neither, and nothing about it should ever be waited on.
        var storePage = StoreActions.LinksFor(ExternalIdProviders.Steam, "620", null)[0];
        Assert.False(storePage.StartsGame);
        Assert.Equal(GameLinkKind.Link, storePage.Kind);
    }

    // ══ Grid command wiring ══════════════════════════════════════════════════

    [Fact]
    public async Task The_grid_actions_carry_the_librarys_own_commands()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anvil");
        var library = await fixture.LoadAsync();

        var anvil = library.VisibleTiles[0];

        Assert.Same(library.LaunchCommand, anvil.PrimaryActionCommand);
        Assert.Same(library.OpenDetailsCommand, anvil.OpenDetailsCommand);
    }

    /// <summary>The §7 name, not the query's key — the rail's own vocabulary.</summary>
    [Fact]
    public async Task The_tile_names_the_bucket_the_way_the_rail_does()
    {
        using var fixture = new LibraryFixture();
        await fixture.SeedAsync("Anvil");
        var library = await fixture.LoadAsync();

        Assert.Equal("Never played", library.VisibleTiles[0].BucketLabel);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A tile as the library builds one, with whichever store's ids the case
    /// needs. Every store gets its real id shape: a Steam appid is digits, a GOG
    /// product id is digits, and the Epic key is Fez's actual three parts.
    /// </summary>
    private static GameTileViewModel Tile(
        string store,
        bool? installed,
        bool withEpicKey = true)
        => TileFixture.Tile(
            nowUtc: Now,
            ownershipId: 1,
            releaseId: 1,
            title: "Fez",
            store: store,
            bucket: LibraryBuckets.NeverPlayed,
            ownership: installed is { } definite
                ? new Ownership { ReleaseId = 1, Store = store, Installed = definite }
                : null,
            steamAppId: "620",
            gogProductId: "1971477531",
            epicLaunchKey: withEpicKey
                ? EpicLaunchKey.Create(
                    "41f47fd0d3e248bc938a5815d6d64daa",
                    "7a70b499513441c792b541d53505e0b2",
                    "Bluebird")
                : null,
            bucketLabel: "Never played");

    /// <summary>
    /// The smallest real library the grid command wiring needs: a migrated
    /// SQLite file and the real repositories.
    /// </summary>
    private sealed class LibraryFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 700000;

        private IWorkRepository Works => field ??= new WorkRepository(_db.Factory);

        private IReleaseRepository Releases => field ??= new ReleaseRepository(_db.Factory);

        private IOwnershipRepository Ownerships => field ??= new OwnershipRepository(_db.Factory);

        private IPlayRecordRepository Plays => field ??= new PlayRecordRepository(_db.Factory);

        public async Task<LibraryViewModel> LoadAsync()
        {
            var library = new LibraryViewModel(
                new LibraryQueryRepository(_db.Factory),
                Ownerships,
                Releases,
                Works,
                new UpdateEventRepository(_db.Factory));

            await library.LoadCommand.ExecuteAsync(null);
            return library;
        }

        public async Task SeedAsync(string title)
        {
            var workId = await Works.InsertAsync(new Work { Name = title });
            var releaseId = await Releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = title,
                Platform = "windows",
            });

            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = (++_appId).ToString(CultureInfo.InvariantCulture),
            });

            var ownershipId = await Ownerships.InsertAsync(new Ownership
            {
                ReleaseId = releaseId,
                Store = "steam",
            });

            await Plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownershipId,
                PlaytimeMinutes = 0,
                LastPlayedAt = null,
                Source = "test",
                ObservedAt = Now,
            });
        }

        public void Dispose() => _db.Dispose();
    }
}
