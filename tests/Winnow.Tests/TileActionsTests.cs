using System.Globalization;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The cover tile's two new jobs: the primary action it offers, and the card
/// flip that reveals it.
///
/// <para><b>Why the URIs are asserted literally.</b> These are the strings this
/// app hands to another program's protocol handler, and a typo in one is a
/// button that appears to work and does nothing — which the design doc's §10.3
/// forbids more strongly than it forbids a missing button. The Epic case in
/// particular is asserted byte for byte against a shortcut the Epic Games
/// Launcher wrote itself, so this test fails if anything about the composite key
/// or its escaping drifts.</para>
/// </summary>
public sealed class TileActionsTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    // ══ Play / Install, per store ═══════════════════════════════════════════

    [Fact]
    public void Steam_on_disk_plays_and_off_disk_installs()
    {
        Assert.Equal("Play", Tile(ExternalIdProviders.Steam, installed: true).PrimaryActionLabel);
        Assert.Equal(
            "steam://run/620",
            Tile(ExternalIdProviders.Steam, installed: true).PrimaryAction!.Uri);

        Assert.Equal("Install", Tile(ExternalIdProviders.Steam, installed: false).PrimaryActionLabel);
        Assert.Equal(
            "steam://install/620",
            Tile(ExternalIdProviders.Steam, installed: false).PrimaryAction!.Uri);
    }

    /// <summary>
    /// <c>goggalaxy://launchGame/&lt;GRK&gt;</c>, where a GRK for a GOG-native
    /// product is <c>gog_&lt;productId&gt;</c> — the same key Galaxy's own
    /// database uses and the GOG ingest splits the provider id out of.
    ///
    /// <para>The authority arrives lowercased because <see cref="Uri"/>
    /// lowercases it on the round trip <see cref="GameLink"/> insists on, and
    /// Galaxy's dispatcher is case-insensitive — confirmed by firing the
    /// lowercased form of a command at a running client. Asserting the
    /// lowercased string is asserting what actually leaves the app.</para>
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
    /// The Epic launcher exposes no install route — its binary carries
    /// <c>launch</c>, <c>installer</c>, <c>updatecheck</c> and <c>verify</c> and
    /// no <c>install</c>, and no store route at all. An unverifiable action is
    /// no action, never a button that silently does nothing.
    /// </summary>
    [Fact]
    public void Epic_off_disk_offers_nothing_rather_than_a_button_that_does_nothing()
    {
        var tile = Tile(ExternalIdProviders.Epic, installed: false);

        Assert.Null(tile.PrimaryAction);
        Assert.False(tile.HasPrimaryAction);
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
    /// Epic gets no store link: a store URL needs a product slug and nothing in
    /// this database holds one. Absent, not invented.
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
        var details = new GameDetailsViewModel(tile, "Bounced off", [], Now);

        Assert.Same(tile.PrimaryAction, details.PrimaryAction);
        Assert.True(details.HasLinks);
    }

    // ══ Launching (M3b) ════════════════════════════════════════════════

    /// <summary>
    /// Every tile launches through the LIBRARY, not through its own view.
    ///
    /// <para>That is not tidiness. A launch has to name the ownership it is
    /// launching so the session watcher does not have to infer it, and a Click
    /// handler holding a <see cref="GameLink"/> knows a URI and nothing else. If
    /// this assertion ever fails, the Play button still works and every session
    /// it produces silently drops back to inference — the exact failure mode this
    /// codebase keeps meeting: build green, tests green, feature absent.</para>
    /// </summary>
    [Fact]
    public async Task Every_tile_launches_through_the_librarys_own_command()
    {
        using var fixture = new FlipFixture();
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

        var details = new GameDetailsViewModel(tile, "Bounced off", [], Now);

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

        // A store page is neither, and nothing about it should ever be waited on.
        var storePage = StoreActions.LinksFor(ExternalIdProviders.Steam, "620", null)[0];
        Assert.False(storePage.StartsGame);
        Assert.Equal(GameLinkKind.Link, storePage.Kind);
    }

    // ══ The card flip ═══════════════════════════════════════════════════════

    [Fact]
    public async Task A_click_turns_one_card_over_and_selects_it()
    {
        using var fixture = new FlipFixture();
        await fixture.SeedAsync("Anvil");
        var library = await fixture.LoadAsync();

        var anvil = library.VisibleTiles.Single(t => t.Title == "Anvil");
        library.FlipTileCommand.Execute(anvil);

        Assert.True(anvil.IsFlipped);
        Assert.Same(anvil, library.FlippedTile);
        Assert.Same(anvil, library.SelectedTile);
        Assert.Equal([anvil], library.SelectedTiles);
    }

    [Fact]
    public async Task Clicking_the_turned_card_again_turns_it_back()
    {
        using var fixture = new FlipFixture();
        await fixture.SeedAsync("Anvil");
        var library = await fixture.LoadAsync();

        var anvil = library.VisibleTiles.Single(t => t.Title == "Anvil");
        library.FlipTileCommand.Execute(anvil);
        library.FlipTileCommand.Execute(anvil);

        Assert.False(anvil.IsFlipped);
        Assert.Null(library.FlippedTile);
    }

    /// <summary>
    /// Exactly one card is ever face-down. §1 says the art is the interface, and
    /// a grid of backs is a grid with no art in it.
    /// </summary>
    [Fact]
    public async Task Turning_a_second_card_turns_the_first_one_back()
    {
        using var fixture = new FlipFixture();
        await fixture.SeedAsync("Anvil");
        await fixture.SeedAsync("Banjo");
        var library = await fixture.LoadAsync();

        var anvil = library.VisibleTiles.Single(t => t.Title == "Anvil");
        var banjo = library.VisibleTiles.Single(t => t.Title == "Banjo");

        library.FlipTileCommand.Execute(anvil);
        library.FlipTileCommand.Execute(banjo);

        Assert.False(anvil.IsFlipped);
        Assert.True(banjo.IsFlipped);
        Assert.Same(banjo, library.FlippedTile);
    }

    /// <summary>
    /// Arrowing off a turned card turns it back: selection and the flip move
    /// together, which is what makes the keyboard route out of the back face a
    /// key the user already knows (§8).
    /// </summary>
    [Fact]
    public async Task Moving_the_selection_turns_the_card_back()
    {
        using var fixture = new FlipFixture();
        await fixture.SeedAsync("Anvil");
        await fixture.SeedAsync("Banjo");
        var library = await fixture.LoadAsync();

        var first = library.VisibleTiles[0];
        library.FlipTileCommand.Execute(first);
        library.MoveSelection(1);

        Assert.False(first.IsFlipped);
        Assert.Null(library.FlippedTile);
    }

    /// <summary>
    /// The wall is rebuilt under whatever was turned over, and the turned card
    /// may not even be in the new set.
    /// </summary>
    [Fact]
    public async Task Cutting_the_library_turns_every_card_back()
    {
        using var fixture = new FlipFixture();
        await fixture.SeedAsync("Anvil");
        await fixture.SeedAsync("Banjo");
        var library = await fixture.LoadAsync();

        var anvil = library.VisibleTiles.Single(t => t.Title == "Anvil");
        library.FlipTileCommand.Execute(anvil);
        library.SearchText = "banjo";

        Assert.False(anvil.IsFlipped);
        Assert.Null(library.FlippedTile);
    }

    [Fact]
    public async Task Leaving_the_grid_turns_every_card_back()
    {
        using var fixture = new FlipFixture();
        await fixture.SeedAsync("Anvil");
        var library = await fixture.LoadAsync();

        library.FlipTileCommand.Execute(library.VisibleTiles[0]);
        library.ShowListViewCommand.Execute(null);

        Assert.Null(library.FlippedTile);
    }

    /// <summary>
    /// The modal is the richer version of the back face, so the card goes
    /// face-up on the way in — Escape should return the user to the wall they
    /// were reading, not to a step they have already taken.
    /// </summary>
    [Fact]
    public async Task Opening_the_details_modal_turns_the_card_back()
    {
        using var fixture = new FlipFixture();
        await fixture.SeedAsync("Anvil");
        var library = await fixture.LoadAsync();

        var anvil = library.VisibleTiles[0];
        library.FlipTileCommand.Execute(anvil);
        await library.OpenDetailsCommand.ExecuteAsync(anvil);

        Assert.False(anvil.IsFlipped);
        Assert.Null(library.FlippedTile);
        Assert.True(library.IsDetailsOpen);
    }

    /// <summary>
    /// The back face raises the library's own commands rather than reaching for
    /// a repository (§5.1) — and "Add to list" is the SAME command the command
    /// bar runs, so the single-game route and the bulk route can never drift.
    /// </summary>
    [Fact]
    public async Task The_back_face_carries_the_librarys_own_commands()
    {
        using var fixture = new FlipFixture();
        await fixture.SeedAsync("Anvil");
        var library = await fixture.LoadAsync();

        var anvil = library.VisibleTiles[0];

        Assert.Same(library.BeginAddToListCommand, anvil.AddToListCommand);
        Assert.Same(library.OpenDetailsCommand, anvil.OpenDetailsCommand);
    }

    /// <summary>The §7 name, not the query's key — the rail's own vocabulary.</summary>
    [Fact]
    public async Task The_back_face_names_the_bucket_the_way_the_rail_does()
    {
        using var fixture = new FlipFixture();
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
        => new(
            ownershipId: 1,
            releaseId: 1,
            title: "Fez",
            store: store,
            bucket: "never_played",
            playtimeMinutes: 0,
            lastPlayedUtc: null,
            nowUtc: Now,
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
    /// The smallest real library the flip needs: a migrated SQLite file and the
    /// real repositories, with no cover cache and no Avalonia application. The
    /// flip is view-model state, so nothing here has to render.
    /// </summary>
    private sealed class FlipFixture : IDisposable
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
