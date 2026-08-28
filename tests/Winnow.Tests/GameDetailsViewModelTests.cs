using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The detail view's state: the gap rail, the outbound links, and the rule that
/// an unvalidated string never becomes a clickable affordance.
///
/// <para>These are pure view-model tests — no database, no Avalonia
/// application. The panel is built the way the library builds it, from a tile
/// plus the two lists the library reads on open.</para>
/// </summary>
public sealed class GameDetailsViewModelTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    // ══ Link validation — the security boundary ═════════════════════════════
    //
    // update_events.url is captured from a network response (§4.5), and an
    // install path is read off disk. Neither is trusted input, and the only way
    // either becomes something the shell opens is through GameLink.Create.

    [Theory]
    [InlineData("https://store.steampowered.com/app/80/")]
    [InlineData("http://example.com/patch-notes")]
    [InlineData("steam://run/80")]
    [InlineData("steam://install/620")]
    public void Http_and_steam_targets_are_openable(string uri)
    {
        var link = GameLink.Create("Go", uri);

        Assert.NotNull(link);
        Assert.Equal("Go", link.Label);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://example.com/payload")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("vbscript:msgbox")]
    [InlineData("ms-settings:")]
    [InlineData("/relative/path")]
    [InlineData("store.steampowered.com/app/80")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Everything_else_is_refused_outright(string? uri)
        => Assert.Null(GameLink.Create("Go", uri));

    /// <summary>
    /// A control character is how a stored string smuggles a second line past a
    /// consumer that only looked at the scheme.
    /// </summary>
    [Fact]
    public void A_target_carrying_control_characters_is_refused()
    {
        Assert.Null(GameLink.Create("Go", "https://example.com/\nsteam://run/80"));
        Assert.Null(GameLink.Create("Go", "https://example.com/\u0000"));
    }

    [Fact]
    public void A_link_with_no_label_is_not_a_link()
        => Assert.Null(GameLink.Create(" ", "https://example.com"));

    [Theory]
    [InlineData("80", true)]
    [InlineData("1203620", true)]
    [InlineData("", false)]
    [InlineData("80a", false)]
    [InlineData("../80", false)]
    [InlineData("8 0", false)]
    [InlineData("12345678901", false)]
    [InlineData(null, false)]
    public void Only_a_plain_number_is_treated_as_an_appid(string? appId, bool expected)
        => Assert.Equal(expected, GameLink.IsSteamAppId(appId));

    /// <summary>
    /// The end of that chain: a tile handed a bad appid holds none, so the panel
    /// offers no Steam affordance rather than one built from junk.
    /// </summary>
    [Fact]
    public void A_malformed_appid_never_reaches_a_url()
    {
        var details = Details(Tile(steamAppId: "80/../../evil"));

        Assert.Null(details.SteamAppId);
        Assert.False(details.HasSteamAppId);
        Assert.Null(details.PrimaryAction);
        Assert.False(details.HasPrimaryAction);
        Assert.Empty(details.Links);
        Assert.False(details.HasLinks);
    }

    /// <summary>
    /// An announcement whose stored URL is not http(s) is listed — it happened —
    /// but it carries no link, so no button is ever rendered for it.
    /// </summary>
    [Fact]
    public void A_hostile_patch_notes_url_lists_the_update_and_offers_no_button()
    {
        var updates = new[]
        {
            Update("Real notes", Now.AddDays(-2), url: "https://store.steampowered.com/news/app/80/view/1"),
            Update("Poisoned", Now.AddDays(-3), url: "javascript:alert(1)"),
            Update("Local", Now.AddDays(-4), url: "file:///C:/Windows/System32/cmd.exe"),
        };

        var details = Details(Tile(lastPlayed: Now.AddDays(-10)), updates);

        Assert.Equal(3, details.Updates.Count);
        Assert.True(details.Updates[0].HasLink);
        Assert.False(details.Updates[1].HasLink);
        Assert.False(details.Updates[2].HasLink);
        Assert.Null(details.Updates[1].Link);
    }

    // ══ The way in ══════════════════════════════════════════════════════════

    /// <summary>
    /// §7: a control says exactly what happens when it is used. "Play" on an
    /// uninstalled 60GB game promises something the next hour will not deliver,
    /// so the button changes its name and its target with the install state.
    /// </summary>
    [Fact]
    public void An_installed_game_offers_a_real_steam_launch()
    {
        var details = Details(Tile(steamAppId: "80", installed: true));

        Assert.Equal("Play", details.PrimaryAction!.Label);
        Assert.Equal("steam://run/80", details.PrimaryAction.Uri);
        Assert.True(details.PrimaryAction.IsSteamProtocol);
    }

    [Fact]
    public void An_uninstalled_game_offers_the_download_instead()
    {
        var details = Details(Tile(steamAppId: "80", installed: false));

        Assert.Equal("Install", details.PrimaryAction!.Label);
        Assert.Equal("steam://install/80", details.PrimaryAction.Uri);
    }

    [Fact]
    public void The_store_page_and_the_patch_notes_hub_are_both_offered()
    {
        var details = Details(Tile(steamAppId: "620"));

        Assert.Equal(["Store page", "All patch notes"], details.Links.Select(l => l.Label));
        Assert.Equal("https://store.steampowered.com/app/620/", details.Links[0].Uri);
        Assert.Equal("https://store.steampowered.com/news/app/620", details.Links[1].Uri);
    }

    /// <summary>
    /// The folder is reached as a path through the launcher's directory entry
    /// point, never as a file: URI — which GameLink refuses precisely so that no
    /// stored string can become a shell open.
    /// </summary>
    [Fact]
    public void The_install_folder_is_offered_only_when_the_game_is_on_disk()
    {
        var installed = Details(Tile(installed: true, installPath: @"D:\SteamLibrary\common\Factorio"));
        Assert.True(installed.HasOpenableFolder);
        Assert.Equal(@"D:\SteamLibrary\common\Factorio", installed.OpenableFolder);
        Assert.DoesNotContain(installed.Links, l => l.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase));

        var absent = Details(Tile(installed: false, installPath: @"D:\SteamLibrary\common\Factorio"));
        Assert.False(absent.HasOpenableFolder);
        Assert.Null(absent.OpenableFolder);
    }

    // ══ The gap rail — the view's signature ═════════════════════════════════

    /// <summary>
    /// Marks sit where the update landed between "you stopped" and "now". Half
    /// way through a two-year gap is 0.5, and nothing else in the app can draw
    /// that because nothing else holds both tables.
    /// </summary>
    [Fact]
    public void Rail_marks_sit_where_the_update_landed_in_the_gap()
    {
        var lastPlayed = Now.AddDays(-100);
        var updates = new[]
        {
            Update("Late", Now.AddDays(-25)),
            Update("Halfway", Now.AddDays(-50)),
            Update("Early", Now.AddDays(-75)),
        };

        var details = Details(Tile(lastPlayed: lastPlayed), updates);

        Assert.True(details.HasGap);
        Assert.Equal(3, details.RailMarks.Count);

        // Ordered along the rail, oldest first, regardless of list order.
        Assert.Equal(0.25, details.RailMarks[0], 3);
        Assert.Equal(0.50, details.RailMarks[1], 3);
        Assert.Equal(0.75, details.RailMarks[2], 3);
    }

    /// <summary>
    /// An update from before the last session is not in the gap. It stays in
    /// the list — it happened — but it is not a thing the user missed, and the
    /// section stops claiming otherwise.
    /// </summary>
    [Fact]
    public void Updates_from_before_the_last_session_are_history_not_marks()
    {
        var updates = new[]
        {
            Update("Older", Now.AddYears(-3)),
            Update("Older still", Now.AddYears(-4)),
        };

        var details = Details(Tile(lastPlayed: Now.AddDays(-30)), updates);

        Assert.Empty(details.RailMarks);
        Assert.False(details.HasRailMarks);
        Assert.Equal(2, details.Updates.Count);
        Assert.DoesNotContain(details.Updates, u => u.IsSinceYouPlayed);
        Assert.Equal("UPDATE HISTORY", details.UpdatesLabel);

        // "recorded", not "nothing shipped": polling is staggered across days,
        // so an empty rail can mean a quiet decade or a turn that has not come
        // round yet, and only one of those is a claim we can support.
        Assert.Equal("No updates recorded in that stretch.", details.GapCaption);
    }

    [Fact]
    public void A_missed_update_renames_the_section_and_marks_its_row()
    {
        var updates = new[]
        {
            Update("New", Now.AddDays(-5)),
            Update("Old", Now.AddYears(-3)),
        };

        var details = Details(Tile(lastPlayed: Now.AddDays(-30)), updates);

        Assert.Equal("SINCE YOU PLAYED", details.UpdatesLabel);
        Assert.Equal("1 update landed while you were away.", details.GapCaption);
        Assert.True(details.Updates[0].IsSinceYouPlayed);
        Assert.False(details.Updates[1].IsSinceYouPlayed);
        Assert.Single(details.RailMarks);
    }

    [Fact]
    public void Several_missed_updates_are_counted_in_the_plural()
    {
        var updates = new[]
        {
            Update("A", Now.AddDays(-2)),
            Update("B", Now.AddDays(-4)),
            Update("C", Now.AddDays(-6)),
        };

        var details = Details(Tile(lastPlayed: Now.AddDays(-30)), updates);

        Assert.Equal("3 updates landed while you were away.", details.GapCaption);
    }

    /// <summary>
    /// §5.2: a game you have never opened has nothing to be behind on, so it
    /// gets no rail at all rather than a rail anchored at an invented date.
    /// </summary>
    [Fact]
    public void A_game_that_was_never_opened_gets_a_sentence_instead_of_a_rail()
    {
        var details = Details(
            Tile(playtimeMinutes: 0, lastPlayed: null),
            [Update("Patch", Now.AddDays(-5))]);

        Assert.False(details.HasGap);
        Assert.True(details.LacksGap);
        Assert.Empty(details.RailMarks);
        Assert.Equal("You've never opened this.", details.NoGapText);
        Assert.DoesNotContain(details.Updates, u => u.IsSinceYouPlayed);
    }

    /// <summary>
    /// Minutes on the clock and no date is a different fact from never having
    /// opened it, and §7 will not let the two collapse into one sentence.
    /// </summary>
    [Fact]
    public void Playtime_without_a_date_says_so_rather_than_saying_never()
    {
        var details = Details(Tile(playtimeMinutes: 28, lastPlayed: null));

        Assert.False(details.HasGap);
        Assert.Equal("Steam has no date for your last session.", details.NoGapText);
    }

    /// <summary>
    /// Past a point the rail stops being a picture of a gap and becomes a
    /// smear. The list below stays exhaustive — it is the record.
    /// </summary>
    [Fact]
    public void The_rail_caps_its_marks_and_the_list_does_not()
    {
        var updates = Enumerable.Range(1, 40)
            .Select(i => Update($"Patch {i}", Now.AddDays(-i)))
            .ToArray();

        var details = Details(Tile(lastPlayed: Now.AddDays(-100)), updates);

        Assert.Equal(14, details.RailMarks.Count);
        Assert.Equal(40, details.Updates.Count);
        Assert.Equal("40 updates landed while you were away.", details.GapCaption);
    }

    // ══ The longitudinal record ═════════════════════════════════════════════
    //
    // §1 names playtime history as the thing storefronts discard. On a real
    // library that table holds one reading per game, so this is a sentence
    // rather than a chart — and the sentence has to be true at n = 0, 1 and n.

    [Fact]
    public void With_no_readings_the_record_line_is_absent_rather_than_empty_prose()
    {
        var details = Details(Tile());

        Assert.False(details.HasRecordLine);
        Assert.Equal(string.Empty, details.RecordLine);
    }

    [Fact]
    public void One_reading_says_it_is_one_reading()
    {
        var details = Details(Tile(), snapshots: [Snapshot(600, Now.AddDays(-1))]);

        Assert.True(details.HasRecordLine);
        Assert.StartsWith("Checked once, on ", details.RecordLine, StringComparison.Ordinal);
        Assert.EndsWith("Winnow keeps every reading from here.", details.RecordLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// The delta is between the first and last reading Winnow holds — the part it
    /// actually watched happen, not the total Steam already knew.
    /// </summary>
    [Fact]
    public void Several_readings_report_what_moved_between_them()
    {
        var details = Details(Tile(), snapshots:
        [
            Snapshot(176, Now.AddDays(-3)),
            Snapshot(200, Now.AddDays(-2)),
            Snapshot(243, Now.AddDays(-1)),
        ]);

        Assert.Contains("Checked 3 times since ", details.RecordLine, StringComparison.Ordinal);
        Assert.EndsWith("— up 1h 7m.", details.RecordLine, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flat_series_says_no_change_rather_than_up_zero()
    {
        var details = Details(Tile(), snapshots:
        [
            Snapshot(600, Now.AddDays(-2)),
            Snapshot(600, Now.AddDays(-1)),
        ]);

        Assert.EndsWith("— no change.", details.RecordLine, StringComparison.Ordinal);
    }

    /// <summary>Out-of-order rows must not invert the delta or the start date.</summary>
    [Fact]
    public void The_series_is_read_in_time_order_whatever_order_it_arrives_in()
    {
        var details = Details(Tile(), snapshots:
        [
            Snapshot(243, Now.AddDays(-1)),
            Snapshot(176, Now.AddDays(-3)),
        ]);

        Assert.EndsWith("— up 1h 7m.", details.RecordLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Durations_read_as_the_app_writes_them()
    {
        Assert.EndsWith("— up 45m.", Line(0, 45), StringComparison.Ordinal);
        Assert.EndsWith("— up 3h.", Line(0, 180), StringComparison.Ordinal);
        Assert.EndsWith("— up 3h 20m.", Line(0, 200), StringComparison.Ordinal);

        static string Line(long from, long to) => Details(Tile(), snapshots:
        [
            Snapshot(from, Now.AddDays(-2)),
            Snapshot(to, Now.AddDays(-1)),
        ]).RecordLine;
    }

    // ══ Identity ════════════════════════════════════════════════════════════

    /// <summary>
    /// Year and publisher share a line but not a typeface (§3), so they are two
    /// runs — and the separator travels with the year, so it cannot survive the
    /// year's absence and leave a line starting with a middot.
    /// </summary>
    [Fact]
    public void Year_and_publisher_share_one_line_and_neither_invents_the_other()
    {
        var both = Details(Tile(year: 2004, publisher: "Sierra Entertainment"));
        Assert.True(both.HasIdentityLine);
        Assert.Equal("2004 · ", both.IdentityYearText);
        Assert.Equal("Sierra Entertainment", both.Publisher);

        var yearOnly = Details(Tile(year: 2004));
        Assert.True(yearOnly.HasIdentityLine);
        Assert.Equal("2004", yearOnly.IdentityYearText);
        Assert.False(yearOnly.HasPublisher);

        var publisherOnly = Details(Tile(publisher: "Sierra Entertainment"));
        Assert.True(publisherOnly.HasIdentityLine);
        Assert.Equal(string.Empty, publisherOnly.IdentityYearText);
        Assert.True(publisherOnly.HasPublisher);

        var bare = Details(Tile());
        Assert.False(bare.HasIdentityLine);
        Assert.Equal(string.Empty, bare.IdentityYearText);
    }

    /// <summary>
    /// "App 8510" is an appid wearing a title's clothes. The panel says so; a
    /// placeholder that looks like a title is how a user concludes the whole
    /// screen is wrong.
    /// </summary>
    [Fact]
    public void A_provisional_title_is_named_as_one()
    {
        var provisional = Details(Tile(title: "App 8510", nameIsProvisional: true));
        Assert.True(provisional.TitleIsProvisional);

        Assert.False(Details(Tile(title: "Counter-Strike")).TitleIsProvisional);
    }

    /// <summary>
    /// The metadata-poor case has to be a panel that is honestly waiting, not a
    /// panel that failed. Every absence has copy behind it.
    /// </summary>
    [Fact]
    public void The_sparse_case_still_says_something_in_every_band()
    {
        var details = Details(Tile(title: "App 8510", steamAppId: "8510", playtimeMinutes: 667, lastPlayed: null, nameIsProvisional: true));

        Assert.False(details.HasSummary);
        Assert.True(details.ShowEmptyBody);
        Assert.NotEmpty(details.EmptyBodyText);
        Assert.False(details.HasUpdates);
        Assert.False(details.HasGap);
        Assert.NotEmpty(details.NoGapText);
        Assert.Equal("Not installed", details.InstallText);
        Assert.False(details.HasInstallPath);

        // And it is still a game you can start, which is the point of the panel.
        Assert.Equal("steam://install/8510", details.PrimaryAction!.Uri);
        Assert.Equal("8510", details.SteamAppId);
    }

    /// <summary>§8: the reduced-motion preference reaches this panel too.</summary>
    [Fact]
    public void Reduced_motion_reaches_the_panel()
    {
        Assert.True(Details(Tile(ramp: new DormancyRamp { ReducedMotion = true })).ReducedMotion);
        Assert.False(Details(Tile(ramp: new DormancyRamp { ReducedMotion = false })).ReducedMotion);
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private static GameTileViewModel Tile(
        string title = "Counter-Strike: Condition Zero",
        long playtimeMinutes = 600,
        DateTime? lastPlayed = null,
        string? steamAppId = "80",
        bool installed = false,
        string? installPath = null,
        int? year = null,
        string? publisher = null,
        string? summary = null,
        bool nameIsProvisional = false,
        DormancyRamp? ramp = null)
        => new(
            ownershipId: 1,
            releaseId: 1,
            title: title,
            store: "steam",
            bucket: "bounced",
            playtimeMinutes: playtimeMinutes,
            lastPlayedUtc: lastPlayed,
            nowUtc: Now,
            work: new Work
            {
                Name = title,
                FirstReleaseYear = year,
                Publisher = publisher,
                Summary = summary,
                NameIsProvisional = nameIsProvisional,
            },
            ownership: new Ownership
            {
                ReleaseId = 1,
                Store = "steam",
                Installed = installed,
                InstallPath = installPath,
            },
            ramp: ramp,
            steamAppId: steamAppId);

    private static GameDetailsViewModel Details(
        GameTileViewModel tile,
        IReadOnlyList<UpdateEvent>? updates = null,
        IReadOnlyList<PlaytimeSnapshot>? snapshots = null)
    {
        var rows = (updates ?? [])
            .OrderByDescending(u => u.OccurredAt)
            .Select(u => UpdateEventViewModel.Create(u, tile.LastPlayedUtc))
            .ToList();

        return new GameDetailsViewModel(tile, "Bounced off", rows, Now, snapshots);
    }

    private static UpdateEvent Update(string title, DateTime occurredAt, string? url = null)
        => new()
        {
            ReleaseId = 1,
            Kind = UpdateEventKinds.Announcement,
            OccurredAt = occurredAt,
            Title = title,
            Url = url,
        };

    private static PlaytimeSnapshot Snapshot(long minutes, DateTime observedAt)
        => new() { OwnershipId = 1, PlaytimeMinutes = minutes, ObservedAt = observedAt };
}
