using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Model;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Model;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Epic.Web.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The pass that turns <c>App 1203620</c> into <c>Portal 2</c>.
/// </summary>
public sealed class EnrichmentSyncServiceTests
{
    // ── IGDB failure isolation ───────────────────────────────────────────────

    /// <summary>
    /// <c>IsConfiguredAsync</c> proves credentials EXIST — it reads the
    /// credential store, not the network. Minting can still fail, and when it
    /// does the Steam fallback must still run: the whole point of step 2 is that
    /// it needs nothing from IGDB.
    /// </summary>
    [Fact]
    public async Task A_configured_igdb_that_throws_falls_through_to_steam()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddProvisionalAsync("620");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Throw = new HttpRequestException("Twitch is down");
        fixture.Steam.Names["620"] = "Portal 2";

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Promoted);
        Assert.Equal(0, report.FromIgdb);
        Assert.Equal("Portal 2", await fixture.WorkNameAsync(work.WorkId));
    }

    [Fact]
    public async Task An_unconfigured_igdb_is_not_an_error_and_steam_still_names_the_work()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddProvisionalAsync("620");

        fixture.Igdb.Configured = false;
        fixture.Steam.Names["620"] = "Portal 2";

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Promoted);
        Assert.Equal("Portal 2", await fixture.WorkNameAsync(work.WorkId));

        // Not even asked: an unconfigured backbone costs no call at all.
        Assert.Empty(fixture.Igdb.Asked);
    }

    /// <summary>
    /// Both sources failing is a degraded run, not a crashed one. The names stay
    /// provisional and the next launch tries again.
    /// </summary>
    [Fact]
    public async Task Both_sources_failing_leaves_the_name_provisional_and_does_not_throw()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddProvisionalAsync("620");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Throw = new HttpRequestException("Twitch is down");

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Outstanding);
        Assert.Equal(0, report.Promoted);
        Assert.Equal("App 620", await fixture.WorkNameAsync(work.WorkId));
        Assert.True(await fixture.IsProvisionalAsync(work.WorkId));
    }

    /// <summary>
    /// IGDB is the backbone and wins the disagreement (§4.4); Steam is only
    /// asked about what IGDB did not answer for.
    /// </summary>
    [Fact]
    public async Task Igdb_wins_and_steam_is_only_asked_about_the_remainder()
    {
        using var fixture = new EnrichmentFixture();
        var portal = await fixture.AddProvisionalAsync("620");
        var dota = await fixture.AddProvisionalAsync("570");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Names["620"] = "Portal 2 (IGDB)";
        fixture.Steam.Names["620"] = "Portal 2 (Steam)";
        fixture.Steam.Names["570"] = "Dota 2";

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(2, report.Promoted);
        Assert.Equal(1, report.FromIgdb);
        Assert.Equal("Portal 2 (IGDB)", await fixture.WorkNameAsync(portal.WorkId));
        Assert.Equal("Dota 2", await fixture.WorkNameAsync(dota.WorkId));
        Assert.Equal(["570"], fixture.Steam.Asked);
    }

    // ── Idempotency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_second_run_has_nothing_to_do()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddProvisionalAsync("620");
        fixture.Steam.Names["620"] = "Portal 2";

        var first = await fixture.Service.EnrichAsync();
        var second = await fixture.Service.EnrichAsync();

        Assert.Equal(1, first.Promoted);
        Assert.Equal(0, second.Outstanding);
        Assert.Equal(0, second.Promoted);

        // A promoted work drops out of the provisional set, so the second pass
        // does not even ask the store about it.
        Assert.Equal(["620"], fixture.Steam.Asked);
    }

    [Fact]
    public async Task A_run_with_no_provisional_names_asks_nothing()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddNamedAsync("730", "Counter-Strike 2");

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(0, report.Outstanding);
        Assert.Empty(fixture.Steam.Asked);
        Assert.Empty(fixture.Igdb.Asked);
    }

    // ── A real title is never reverted to a placeholder ──────────────────────

    /// <summary>
    /// The failure that would rename a user's library back to appids. A work
    /// already holding a real title is not in the provisional set at all, so a
    /// source offering a different name — or no name — cannot touch it.
    /// </summary>
    [Fact]
    public async Task A_real_title_is_never_reverted_to_a_placeholder()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddNamedAsync("620", "Portal 2");

        // Both sources are offering placeholder-shaped nonsense for this appid.
        fixture.Igdb.Configured = true;
        fixture.Igdb.Names["620"] = "App 620";
        fixture.Steam.Names["620"] = "App 620";

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(0, report.Promoted);
        Assert.Equal("Portal 2", await fixture.WorkNameAsync(work.WorkId));
        Assert.Equal("Portal 2", await fixture.ReleaseNameAsync(work.ReleaseId));
        Assert.False(await fixture.IsProvisionalAsync(work.WorkId));
    }

    /// <summary>
    /// A source answering with blank or whitespace is "no data", not a title.
    /// Promoting it would clear the provisional flag and strand a nameless tile
    /// that no later run would revisit.
    /// </summary>
    [Fact]
    public async Task A_blank_name_from_a_source_is_not_a_promotion()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddProvisionalAsync("620");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Names["620"] = "   ";
        fixture.Steam.Names["620"] = string.Empty;

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(0, report.Promoted);
        Assert.Equal("App 620", await fixture.WorkNameAsync(work.WorkId));
        Assert.True(await fixture.IsProvisionalAsync(work.WorkId));
    }

    /// <summary>
    /// Work and release move together. Clearing name_is_provisional is what
    /// removes the work from the query, so a release left holding "App 620"
    /// would never be revisited by any future run.
    /// </summary>
    [Fact]
    public async Task Promotion_moves_the_work_and_its_release_together()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddProvisionalAsync("620");
        fixture.Steam.Names["620"] = "Portal 2";

        await fixture.Service.EnrichAsync();

        Assert.Equal("Portal 2", await fixture.WorkNameAsync(work.WorkId));
        Assert.Equal("Portal 2", await fixture.ReleaseNameAsync(work.ReleaseId));
        Assert.False(await fixture.IsProvisionalAsync(work.WorkId));
    }

    // ── Metadata, not just the name ──────────────────────────────────────────

    /// <summary>
    /// The bug this pass used to have: IGDB answers with an id, a year, a
    /// summary and a cover, and the service read <c>Name</c> and threw the rest
    /// away — leaving four §6 columns empty and two of §5.3's four soft-match
    /// signals permanently unable to fire.
    /// </summary>
    [Fact]
    public async Task Igdb_metadata_is_stored_alongside_the_promoted_name()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddProvisionalAsync("620");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["620"] = new IgdbExternalMatch(
            "620", 7346, "Portal 2", "https://images.igdb.com/cover.jpg", 2011, "Still alive.");
        fixture.Igdb.Games[7346] = Game(7346, "Portal 2", publishers: ["Valve"]);

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Promoted);
        Assert.Equal(1, report.MetadataFilled);

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal("Portal 2", work.Name);
        Assert.Equal(7346, work.IgdbId);
        Assert.Equal(2011, work.FirstReleaseYear);
        Assert.Equal("Still alive.", work.Summary);
        Assert.Equal("https://images.igdb.com/cover.jpg", work.CoverUrl);
        Assert.Equal("Valve", work.Publisher);
    }

    /// <summary>
    /// The publisher is the one field <c>external_games</c> cannot carry —
    /// it hangs off <c>involved_companies</c> and needs the second, batched
    /// <c>/games</c> call. Without that call the publisher signal stays exactly
    /// as silent as it was before the column existed.
    /// </summary>
    [Fact]
    public async Task The_publisher_comes_from_the_second_games_call()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddProvisionalAsync("620");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["620"] = new IgdbExternalMatch("620", 7346, "Portal 2", null, 2011, null);
        fixture.Igdb.Games[7346] = Game(7346, "Portal 2", publishers: ["Valve"]);

        await fixture.Service.EnrichAsync();

        Assert.Equal([7346L], fixture.Igdb.GameIdsAsked);
        Assert.Equal("Valve", (await fixture.WorkAsync(seeded.WorkId)).Publisher);
    }

    /// <summary>
    /// IGDB returns publishers as a list and the column stores one name, so the
    /// pick has to be order-independent: two library rows for the same game must
    /// agree, or a corroborating signal turns into a mismatch penalty. Ordinal
    /// order, not IGDB's row order.
    /// </summary>
    [Fact]
    public async Task Multiple_publishers_reduce_to_the_same_name_whatever_order_igdb_lists_them_in()
    {
        using var fixture = new EnrichmentFixture();
        var first = await fixture.AddProvisionalAsync("620");
        var second = await fixture.AddProvisionalAsync("621");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["620"] = new IgdbExternalMatch("620", 7346, "Skyrim", null, 2011, null);
        fixture.Igdb.Matches["621"] = new IgdbExternalMatch("621", 7347, "Skyrim", null, 2011, null);
        fixture.Igdb.Games[7346] = Game(7346, "Skyrim", publishers: ["ZeniMax Media", "Bethesda Softworks"]);
        fixture.Igdb.Games[7347] = Game(7347, "Skyrim", publishers: ["Bethesda Softworks", "ZeniMax Media"]);

        await fixture.Service.EnrichAsync();

        Assert.Equal("Bethesda Softworks", (await fixture.WorkAsync(first.WorkId)).Publisher);
        Assert.Equal("Bethesda Softworks", (await fixture.WorkAsync(second.WorkId)).Publisher);
    }

    /// <summary>
    /// A source that says nothing must not be able to erase what a source that
    /// said something already wrote. This is the failure mode that makes an
    /// "update the row" method unusable for enrichment: every field the partial
    /// answer did not carry arrives as null.
    /// </summary>
    [Fact]
    public async Task A_null_from_igdb_never_overwrites_a_stored_value()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddAsync("620", new Work
        {
            Name = "Portal 2",
            FirstReleaseYear = 2011,
            Summary = "Still alive.",
            CoverUrl = "https://example.invalid/kept.jpg",
        });

        // IGDB knows this appid but has no date, no summary and no cover for it.
        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["620"] = new IgdbExternalMatch("620", 7346, "Portal 2", null, null, null);
        fixture.Igdb.Games[7346] = Game(7346, "Portal 2", publishers: ["Valve"]);

        await fixture.Service.EnrichAsync();

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal(2011, work.FirstReleaseYear);
        Assert.Equal("Still alive.", work.Summary);
        Assert.Equal("https://example.invalid/kept.jpg", work.CoverUrl);

        // And the columns that WERE empty are filled — one-way, not read-only.
        Assert.Equal(7346, work.IgdbId);
        Assert.Equal("Valve", work.Publisher);
    }

    /// <summary>A blank string is "I do not know", not a value to store.</summary>
    [Fact]
    public async Task A_blank_summary_is_not_stored_as_a_value()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddProvisionalAsync("620");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["620"] = new IgdbExternalMatch("620", 7346, "Portal 2", "   ", 2011, "  ");
        fixture.Igdb.Games[7346] = Game(7346, "Portal 2", publishers: ["   "]);

        await fixture.Service.EnrichAsync();

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Null(work.Summary);
        Assert.Null(work.CoverUrl);
        Assert.Null(work.Publisher);
        Assert.Equal(2011, work.FirstReleaseYear);
    }

    // ── Backfill: the 616 works that already have names ──────────────────────

    /// <summary>
    /// The real starting condition. A library named by an earlier build has no
    /// provisional works left, so a pass keyed on <c>name_is_provisional</c>
    /// alone would look at nothing and back-fill nothing — forever.
    /// </summary>
    [Fact]
    public async Task An_already_named_work_with_no_metadata_is_backfilled()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddNamedAsync("620", "Portal 2");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["620"] = new IgdbExternalMatch(
            "620", 7346, "Portal 2 (IGDB spelling)", "https://images.igdb.com/cover.jpg", 2011, "Still alive.");
        fixture.Igdb.Games[7346] = Game(7346, "Portal 2", publishers: ["Valve"]);

        var report = await fixture.Service.EnrichAsync();

        // No name was outstanding, so nothing was "promoted" — but the work was
        // still enriched, which is the whole point.
        Assert.Equal(0, report.Outstanding);
        Assert.Equal(0, report.Promoted);
        Assert.Equal(1, report.MetadataFilled);

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal("Portal 2", work.Name);
        Assert.Equal(2011, work.FirstReleaseYear);
        Assert.Equal("Valve", work.Publisher);
    }

    /// <summary>
    /// The other half of backfill: a work that already has everything is not a
    /// target, so a warm library costs one query that returns no rows and no
    /// source is asked anything at all.
    /// </summary>
    [Fact]
    public async Task A_fully_enriched_work_is_never_asked_about_again()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddAsync("620", new Work
        {
            Name = "Portal 2",
            IgdbId = 7346,
            FirstReleaseYear = 2011,
            Summary = "Still alive.",
            CoverUrl = "https://example.invalid/cover.jpg",
            Publisher = "Valve",
        });

        fixture.Igdb.Configured = true;

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(0, report.MetadataFilled);
        Assert.Empty(fixture.Igdb.Asked);
        Assert.Empty(fixture.Igdb.GameIdsAsked);
        Assert.Empty(fixture.Steam.Asked);
    }

    /// <summary>
    /// A second run over a library the first run enriched writes nothing: every
    /// target either dropped out of the query or produces an empty patch, so no
    /// transaction is opened.
    /// </summary>
    [Fact]
    public async Task A_second_run_over_an_enriched_library_writes_nothing()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddProvisionalAsync("620");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["620"] = new IgdbExternalMatch(
            "620", 7346, "Portal 2", "https://images.igdb.com/cover.jpg", 2011, "Still alive.");
        fixture.Igdb.Games[7346] = Game(7346, "Portal 2", publishers: ["Valve"]);

        var first = await fixture.Service.EnrichAsync();
        var second = await fixture.Service.EnrichAsync();

        Assert.Equal(1, first.MetadataFilled);
        Assert.Equal(0, second.MetadataFilled);
    }

    /// <summary>
    /// The Steam store endpoint is undocumented and exists to supply TITLES.
    /// A work that has a title and only wants a year must never reach it —
    /// otherwise a credential-free machine hammers it once per game per launch
    /// to re-learn names it already has.
    /// </summary>
    [Fact]
    public async Task The_steam_fallback_is_not_asked_about_a_work_that_only_needs_metadata()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddNamedAsync("620", "Portal 2");
        await fixture.AddProvisionalAsync("570");

        fixture.Igdb.Configured = false;
        fixture.Steam.Names["570"] = "Dota 2";

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(["570"], fixture.Steam.Asked);
        Assert.Equal(1, report.Promoted);
    }

    /// <summary>
    /// Two Steam appids resolving to one IGDB game IS a duplicate in the user's
    /// library, and <c>works.igdb_id</c> is UNIQUE. The second work keeps the
    /// metadata — which is what lets the soft matcher see two rows with the same
    /// year and the same publisher and queue the pair — while the id itself
    /// stays with the first, because re-pointing identity is a merge and merges
    /// need a human (§5.3).
    /// </summary>
    [Fact]
    public async Task A_second_appid_for_one_igdb_game_keeps_its_metadata_without_stealing_the_id()
    {
        using var fixture = new EnrichmentFixture();
        var first = await fixture.AddProvisionalAsync("63500");
        var second = await fixture.AddProvisionalAsync("63501");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["63500"] = new IgdbExternalMatch("63500", 4123, "Riven", null, 1997, "Myst II.");
        fixture.Igdb.Matches["63501"] = new IgdbExternalMatch("63501", 4123, "Riven", null, 1997, "Myst II.");
        fixture.Igdb.Games[4123] = Game(4123, "Riven", publishers: ["Brøderbund"]);

        await fixture.Service.EnrichAsync();

        var left = await fixture.WorkAsync(first.WorkId);
        var right = await fixture.WorkAsync(second.WorkId);

        Assert.Equal(4123, left.IgdbId);
        Assert.Null(right.IgdbId);

        // Both sides carry the evidence the matcher needs.
        Assert.Equal(1997, left.FirstReleaseYear);
        Assert.Equal(1997, right.FirstReleaseYear);
        Assert.Equal("Brøderbund", left.Publisher);
        Assert.Equal("Brøderbund", right.Publisher);
    }

    // ── The third name source: api.steamcmd.net ──────────────────────────────

    /// <summary>
    /// The 18-appid case. IGDB has no entry for 4028270 and
    /// <c>IStoreBrowseService/GetItems</c> returns nothing, so this work sat as
    /// "App 4028270" through every earlier run. steamcmd.net names it — and
    /// classifies it in the same response.
    /// </summary>
    [Fact]
    public async Task Steamcmd_names_an_app_igdb_and_the_store_both_missed()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddProvisionalAsync("4028270");

        fixture.Igdb.Configured = true;
        fixture.SteamCmd.Add("4028270", "Everwind Demo", "Demo", parent: "2253100");

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Promoted);
        Assert.Equal(1, report.FromSteamCmd);
        Assert.Equal(0, report.FromIgdb);

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal("Everwind Demo", work.Name);
        Assert.Equal("Demo", work.SteamAppType);
        Assert.Equal("Everwind Demo", await fixture.ReleaseNameAsync(seeded.ReleaseId));
        Assert.False(work.NameIsProvisional);
    }

    /// <summary>
    /// Ordering, all three sources at once. §4.4 keeps IGDB the backbone and the
    /// no-SLA volunteer mirror last, so it is only ever asked about what the
    /// other two could not answer.
    /// </summary>
    [Fact]
    public async Task Steamcmd_is_last_and_only_sees_what_igdb_and_the_store_missed()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddProvisionalAsync("620");
        await fixture.AddProvisionalAsync("570");
        await fixture.AddProvisionalAsync("4028270");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Names["620"] = "Portal 2";
        fixture.Steam.Names["570"] = "Dota 2";
        fixture.SteamCmd.Add("620", "Portal 2 (steamcmd)", "Game");
        fixture.SteamCmd.Add("570", "Dota 2 (steamcmd)", "Game");
        fixture.SteamCmd.Add("4028270", "Everwind Demo", "Demo");

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(3, report.Promoted);
        Assert.Equal(1, report.FromIgdb);
        Assert.Equal(1, report.FromSteamCmd);

        // The two appids the earlier sources answered for were never requested.
        Assert.Equal(["4028270"], fixture.SteamCmd.Asked);
    }

    /// <summary>
    /// The volunteer service is not asked to re-name a library it already named.
    /// Without this, 616 works would cost 616 requests on every launch — the
    /// exact failure the Steam-store fallback already guards against, and the
    /// stakes are higher here because the host has no SLA.
    /// </summary>
    [Fact]
    public async Task Steamcmd_is_not_asked_about_a_work_that_only_needs_metadata()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddNamedAsync("620", "Portal 2");

        fixture.Igdb.Configured = false;
        fixture.SteamCmd.Add("620", "Portal 2", "Game");

        await fixture.Service.EnrichAsync();

        // Not requested. It IS offered the free cache read — a body some other
        // pass already paid for costs nothing — but no call is made.
        Assert.Empty(fixture.SteamCmd.Asked);
        Assert.Equal(["620"], fixture.SteamCmd.Peeked);
    }

    /// <summary>
    /// …and a body the update poller already fetched is harvested for free, so
    /// a library that polls for update signals gradually learns its own types
    /// without a single extra request.
    /// </summary>
    [Fact]
    public async Task A_type_already_in_the_cache_is_read_at_no_cost()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddNamedAsync("2246340", "Monster Hunter Wilds");

        fixture.SteamCmd.Add("2246340", "Monster Hunter Wilds", "Game");
        fixture.SteamCmd.Cached.Add("2246340");

        await fixture.Service.EnrichAsync();

        Assert.Empty(fixture.SteamCmd.Asked);
        Assert.Equal("Game", (await fixture.WorkAsync(seeded.WorkId)).SteamAppType);
    }

    /// <summary>
    /// The one class of already-named work that IS worth a request: a title that
    /// reads like a handout, where Valve's type decides whether a tile gets
    /// hidden. Narrow by construction — the query only returns these while the
    /// type is still unknown.
    /// </summary>
    [Fact]
    public async Task A_variant_titled_work_is_asked_about_so_its_type_can_be_stored()
    {
        using var fixture = new EnrichmentFixture();
        var demo = await fixture.AddNamedAsync("107110", "Bastion Demo");
        await fixture.AddNamedAsync("107100", "Bastion");

        fixture.SteamCmd.Add("107110", "Bastion - Demo", "Demo", parent: "107100");
        fixture.SteamCmd.Add("107100", "Bastion", "game");

        await fixture.Service.EnrichAsync();

        // Only the handout-shaped title cost a request.
        Assert.Equal(["107110"], fixture.SteamCmd.Asked);

        var work = await fixture.WorkAsync(demo.WorkId);
        Assert.Equal("Demo", work.SteamAppType);

        // The name is NOT touched: this work already had a real title, and
        // "Bastion - Demo" must not overwrite "Bastion Demo".
        Assert.Equal("Bastion Demo", work.Name);
    }

    /// <summary>
    /// A second run asks nothing. The name promotion drops the work out of the
    /// provisional set and the stored type drops it out of the variant-title
    /// predicate, so the volunteer service sees one request per appid, ever.
    /// </summary>
    [Fact]
    public async Task A_second_run_asks_steamcmd_nothing()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddProvisionalAsync("4028270");
        fixture.SteamCmd.Add("4028270", "Everwind Demo", "Demo");

        await fixture.Service.EnrichAsync();
        fixture.SteamCmd.Asked.Clear();
        await fixture.Service.EnrichAsync();

        Assert.Empty(fixture.SteamCmd.Asked);
    }

    /// <summary>
    /// The restricted appids — 8510, 813000, 1883690, 236600 — answer HTTP 200
    /// with no <c>common</c> block, and no anonymous request will ever get more.
    /// That is a degraded run, not a failed one: the name stays provisional, the
    /// type stays NULL (never a guess), and nothing throws.
    /// </summary>
    [Fact]
    public async Task An_unreadable_appid_leaves_the_name_provisional_and_the_type_null()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddProvisionalAsync("8510");

        // The fake answers NoData for anything it was not given.
        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(0, report.Promoted);

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal("App 8510", work.Name);
        Assert.True(work.NameIsProvisional);
        Assert.Null(work.SteamAppType);
    }

    /// <summary>
    /// A dead volunteer service must not take the pass down with it. §5.1:
    /// enrichment never blocks, and it is the LAST source precisely so its
    /// failure costs nothing the first two already delivered.
    /// </summary>
    [Fact]
    public async Task A_throwing_steamcmd_does_not_fail_the_run()
    {
        using var fixture = new EnrichmentFixture();
        var portal = await fixture.AddProvisionalAsync("620");
        var everwind = await fixture.AddProvisionalAsync("4028270");

        fixture.Steam.Names["620"] = "Portal 2";
        fixture.SteamCmd.Throw = new HttpRequestException("steamcmd.net is down");

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Promoted);
        Assert.Equal(0, report.FromSteamCmd);
        Assert.Equal("Portal 2", await fixture.WorkNameAsync(portal.WorkId));
        Assert.True(await fixture.IsProvisionalAsync(everwind.WorkId));
    }

    /// <summary>
    /// A name from the mirror is still a name, and the one-way promotion rule
    /// applies to it exactly as it does to the other two sources.
    /// </summary>
    [Fact]
    public async Task Steamcmd_never_overwrites_a_real_title()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddNamedAsync("4028270", "Everwind Demo (renamed by hand)");

        fixture.SteamCmd.Add("4028270", "Everwind Demo", "Demo");

        await fixture.Service.EnrichAsync();

        Assert.Equal(
            "Everwind Demo (renamed by hand)", (await fixture.WorkAsync(seeded.WorkId)).Name);
    }

    /// <summary>
    /// A blank name is "I do not know", not a title — the same rule the other
    /// two sources are held to. The type from the same response is still stored:
    /// the response answered one question and not the other.
    /// </summary>
    [Fact]
    public async Task A_blank_name_from_steamcmd_is_not_a_promotion()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddProvisionalAsync("4028270");

        fixture.SteamCmd.Add("4028270", "   ", "Demo");

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(0, report.Promoted);

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.True(work.NameIsProvisional);
        Assert.Equal("Demo", work.SteamAppType);
    }

    // -- Every store, not just Steam ------------------------------------------

    /// <summary>
    /// The bug, stated as a test. Enrichment asked the repository for
    /// <c>steam</c> targets and asked IGDB with Steam's source id, both
    /// hardcoded, so a GOG or Epic release was never in a result set at all --
    /// measured on the author's real library as 67 Epic and 14 GOG works with
    /// zero igdb_id, zero covers, zero years and zero summaries between them.
    /// </summary>
    [Fact]
    public async Task A_gog_release_is_asked_about_at_all()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddAsync(
            ExternalIdProviders.Gog, "1207658695", new Work { Name = "Beneath a Steel Sky" });

        fixture.Igdb.Configured = true;

        await fixture.Service.EnrichAsync();

        // Source 5, with the bare GOG product id. IGDB stores it verbatim, so
        // there is nothing to transform -- the id we hold IS the id it indexes.
        Assert.Contains((5, "1207658695"), fixture.Igdb.AskedExternal);
    }

    /// <summary>
    /// The GOG route end to end: source 5 answers and the metadata columns the
    /// tile and 5.3's soft matcher both read are filled.
    /// </summary>
    [Fact]
    public async Task A_gog_release_gets_its_metadata_from_source_5()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddAsync(
            ExternalIdProviders.Gog, "1207658695", new Work { Name = "Beneath a Steel Sky" });

        fixture.Igdb.Configured = true;
        fixture.Igdb.External[(5, "1207658695")] = new IgdbExternalMatch(
            "1207658695", 612, "Beneath a Steel Sky", "https://img/co1.jpg", 1994, "Cyberpunk point-and-click.");
        fixture.Igdb.Games[612] = Game(612, "Beneath a Steel Sky", ["Revolution Software"]);

        await fixture.Service.EnrichAsync();

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal(612, work.IgdbId);
        Assert.Equal(1994, work.FirstReleaseYear);
        Assert.Equal("https://img/co1.jpg", work.CoverUrl);
        Assert.Equal("Revolution Software", work.Publisher);
        Assert.NotNull(work.Summary);
    }

    /// <summary>
    /// The Epic route: catalog item id to AppName (from the launcher's own
    /// files) to gamesdb to a Steam appid to IGDB source 1. Every hop an exact
    /// identifier; no title is normalised anywhere, which is what keeps this
    /// inside 5.3 layer 1 rather than in the fuzzy matcher.
    /// </summary>
    [Fact]
    public async Task An_epic_release_is_bridged_to_a_steam_appid_and_enriched()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddAsync(
            ExternalIdProviders.Epic, "7a70b499513441c792b541d53505e0b2", new Work { Name = "Fez" });

        fixture.Aliases.Epic["7a70b499513441c792b541d53505e0b2"] = "Bluebird";
        fixture.Identity.Add("epic", "Bluebird", "51152861476431582", ("steam", "224760"));

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["224760"] = new IgdbExternalMatch(
            "224760", 1991, "Fez", "https://img/cofez.jpg", 2012, "A 2D creature in a 3D world.");

        await fixture.Service.EnrichAsync();

        Assert.Equal(("epic", "Bluebird"), fixture.Identity.Asked.Single());

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal(1991, work.IgdbId);
        Assert.Equal(2012, work.FirstReleaseYear);
        Assert.Equal("https://img/cofez.jpg", work.CoverUrl);
    }

    /// <summary>
    /// gamesdb is crowd-shaped and carries junk -- Fez lists both
    /// <c>steam/224760</c> and <c>steam/steam_224760</c>. Taking the graph's
    /// first answer would send a malformed id to IGDB, miss, and leave the title
    /// blank for no visible reason.
    /// </summary>
    [Fact]
    public async Task A_malformed_id_in_the_graph_is_skipped_for_the_well_formed_one()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddAsync(ExternalIdProviders.Epic, "cat-1", new Work { Name = "Fez" });

        fixture.Aliases.Epic["cat-1"] = "Bluebird";
        fixture.Identity.Add(
            "epic", "Bluebird", "51152861476431582", ("steam", "steam_224760"), ("steam", "224760"));

        fixture.Igdb.Configured = true;

        await fixture.Service.EnrichAsync();

        Assert.Contains("224760", fixture.Igdb.Asked);
        Assert.DoesNotContain("steam_224760", fixture.Igdb.Asked);
    }

    /// <summary>
    /// An Epic title with no cross-store twin -- Fortnite, Genshin Impact and
    /// Dauntless on the author's library. The route ends, and the row is left
    /// exactly as it was found.
    /// </summary>
    [Fact]
    public async Task An_epic_exclusive_with_no_twin_is_left_untouched()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddAsync(ExternalIdProviders.Epic, "cat-2", new Work { Name = "Fortnite" });

        fixture.Aliases.Epic["cat-2"] = "Fortnite";
        fixture.Identity.Add("epic", "Fortnite", "51152861476431999");
        fixture.Igdb.Configured = true;

        await fixture.Service.EnrichAsync();

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal("Fortnite", work.Name);
        Assert.Null(work.IgdbId);
        Assert.Null(work.CoverUrl);
        Assert.Null(work.Summary);
    }

    /// <summary>
    /// <b>The rule this codebase has already paid for twice.</b> No Epic
    /// launcher on this machine means no alias map, which says nothing whatever
    /// about the library -- and must not be written down as though it did.
    /// </summary>
    [Fact]
    public async Task No_alias_source_leaves_epic_rows_exactly_as_they_were()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "cat-3",
            new Work
            {
                Name = "ABZU",
                FirstReleaseYear = 2016,
                CoverUrl = "https://img/existing.jpg",
                Summary = "Already known.",
            });

        // Aliases empty: the launcher is not installed.
        fixture.Igdb.Configured = true;

        await fixture.Service.EnrichAsync();

        Assert.Empty(fixture.Identity.Asked);

        var work = await fixture.WorkAsync(seeded.WorkId);
        Assert.Equal("ABZU", work.Name);
        Assert.Equal(2016, work.FirstReleaseYear);
        Assert.Equal("https://img/existing.jpg", work.CoverUrl);
        Assert.Equal("Already known.", work.Summary);
    }

    /// <summary>
    /// Same rule one hop further out: the alias exists, gamesdb is unreachable,
    /// and silence is still not an answer.
    /// </summary>
    [Fact]
    public async Task An_unreachable_identity_graph_writes_nothing()
    {
        using var fixture = new EnrichmentFixture();
        var seeded = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "cat-4",
            new Work { Name = "ABZU", CoverUrl = "https://img/existing.jpg" });

        fixture.Aliases.Epic["cat-4"] = "Buccaneer";
        fixture.Identity.Throw = new HttpRequestException("gamesdb is down");
        fixture.Igdb.Configured = true;

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(0, report.Promoted);
        Assert.Equal("https://img/existing.jpg", (await fixture.WorkAsync(seeded.WorkId)).CoverUrl);
    }

    /// <summary>
    /// An alias source that throws -- an unreadable %PROGRAMDATA%, a launcher
    /// mid-upgrade -- degrades the run, it does not fail it (5.1).
    /// </summary>
    [Fact]
    public async Task An_alias_source_that_throws_does_not_break_the_pass()
    {
        using var fixture = new EnrichmentFixture();
        var steam = await fixture.AddProvisionalAsync("620");
        await fixture.AddAsync(ExternalIdProviders.Epic, "cat-5", new Work { Name = "ABZU" });

        fixture.Aliases.Throw = new UnauthorizedAccessException("ProgramData");
        fixture.Igdb.Configured = true;
        fixture.Igdb.Names["620"] = "Portal 2";

        await fixture.Service.EnrichAsync();

        // The Steam half of the library finished normally.
        Assert.Equal("Portal 2", await fixture.WorkNameAsync(steam.WorkId));
    }

    /// <summary>
    /// The two keyless Steam endpoints take APPIDS. A GOG product id is numeric
    /// and would be accepted as one, answering confidently about an unrelated
    /// game -- the failure mode worse than answering about none.
    /// </summary>
    [Fact]
    public async Task Steam_only_endpoints_are_never_asked_about_a_gog_id()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddAsync(
            ExternalIdProviders.Gog, "1", new Work { Name = "App 1", NameIsProvisional = true });

        fixture.Igdb.Configured = false;
        fixture.Steam.Names["1"] = "Half-Life 2: Lost Coast";
        fixture.SteamCmd.Add("1", "Something Else Entirely", "Game");

        var report = await fixture.Service.EnrichAsync();

        Assert.Empty(fixture.Steam.Asked);
        Assert.Empty(fixture.SteamCmd.Asked);
        Assert.Equal(0, report.Promoted);
    }

    /// <summary>
    /// Valve's <c>common.type</c> is Steam's own classification, keyed by appid.
    /// A GOG work must not inherit it just because its product id happens to
    /// look like one.
    /// </summary>
    [Fact]
    public async Task A_gog_work_never_inherits_a_steam_app_type()
    {
        using var fixture = new EnrichmentFixture();
        var steam = await fixture.AddNamedAsync("2000", "Portal 2 Demo");
        var gog = await fixture.AddAsync(
            ExternalIdProviders.Gog, "2000", new Work { Name = "Beta Colony" });

        fixture.SteamCmd.Add("2000", "Portal 2 Demo", "Demo");
        fixture.Igdb.Configured = false;

        await fixture.Service.EnrichAsync();

        Assert.Equal("Demo", (await fixture.WorkAsync(steam.WorkId)).SteamAppType);
        Assert.Null((await fixture.WorkAsync(gog.WorkId)).SteamAppType);
    }

    /// <summary>
    /// Epic ids are never sent to IGDB's source 26. Measured twice -- once in
    /// the spike, once while fixing this -- at 0 matches out of 67 owned titles,
    /// because IGDB indexes Epic store <i>offer</i> ids and the launcher writes
    /// <i>catalog item</i> ids. Asking anyway would spend the rate limit to
    /// cache 67 misses for a month.
    /// </summary>
    [Fact]
    public async Task An_epic_catalog_item_id_is_never_sent_to_igdb_directly()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddAsync(ExternalIdProviders.Epic, "1e4e7275844844", new Work { Name = "ABZU" });

        fixture.Aliases.Epic["1e4e7275844844"] = "Buccaneer";
        fixture.Identity.Add("epic", "Buccaneer", "51152861476431777");
        fixture.Igdb.Configured = true;

        await fixture.Service.EnrichAsync();

        Assert.DoesNotContain(fixture.Igdb.AskedExternal, a => a.Uid == "1e4e7275844844");
        Assert.DoesNotContain(fixture.Igdb.AskedExternal, a => a.Source == 26);
    }

    /// <summary>
    /// Two rows for one game -- the cross-store duplicate the merge queue is
    /// full of. <c>works.igdb_id</c> is UNIQUE, so only one of them may hold it;
    /// the other must still get its year, summary and cover, or the Epic half of
    /// every pair stays blank forever.
    /// </summary>
    [Fact]
    public async Task A_duplicate_that_cannot_claim_the_igdb_id_still_gets_its_metadata()
    {
        using var fixture = new EnrichmentFixture();
        var steam = await fixture.AddNamedAsync("224760", "Fez");
        var epic = await fixture.AddAsync(ExternalIdProviders.Epic, "cat-6", new Work { Name = "Fez" });

        fixture.Aliases.Epic["cat-6"] = "Bluebird";
        fixture.Identity.Add("epic", "Bluebird", "51152861476431582", ("steam", "224760"));

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["224760"] = new IgdbExternalMatch(
            "224760", 1991, "Fez", "https://img/cofez.jpg", 2012, "A 2D creature in a 3D world.");

        await fixture.Service.EnrichAsync();

        var steamWork = await fixture.WorkAsync(steam.WorkId);
        var epicWork = await fixture.WorkAsync(epic.WorkId);

        Assert.Equal(1991, steamWork.IgdbId);

        // Identity refused -- re-pointing it is a merge, and merges need a human
        // (5.3). Metadata written all the same: this is what puts art on the
        // Epic tile of a pair whose Steam side already claimed the id.
        Assert.Null(epicWork.IgdbId);
        Assert.Equal("https://img/cofez.jpg", epicWork.CoverUrl);
        Assert.Equal(2012, epicWork.FirstReleaseYear);
    }

    // ── Starvation: what a run that does not finish leaves behind ────────────

    /// <summary>
    /// A run cut short must still reach every store, not just the one with the
    /// lowest work ids.
    /// </summary>
    [Fact]
    public async Task A_run_cut_short_still_reaches_the_provider_holding_the_highest_ids()
    {
        // Slice of 6, so cancelling on the second slice boundary leaves exactly
        // one committed slice to inspect — a genuinely bounded pass, not a run
        // that quietly finished everything before the token was signalled.
        using var fixture = new EnrichmentFixture(sliceSize: 6);

        // Insertion order is the point: steam first and in bulk, then epic, then
        // gog last and smallest. This is the author's library in miniature.
        var steam = new List<long>();
        for (var i = 0; i < 12; i++)
        {
            var appId = (600 + i).ToString();
            steam.Add((await fixture.AddNamedAsync(appId, "Steam Game " + i)).WorkId);
            fixture.Igdb.Matches[appId] = Match(appId, 1000 + i, "Steam Game " + i);
        }

        var epic = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var catalogId = "epic-cat-" + i;
            var appName = "EpicName" + i;
            var bridgedAppId = (700 + i).ToString();

            epic.Add((await fixture.AddAsync(
                ExternalIdProviders.Epic, catalogId, new Work { Name = "Epic Game " + i })).WorkId);

            fixture.Aliases.Epic[catalogId] = appName;
            fixture.Identity.Add("epic", appName, "game-" + i, ("steam", bridgedAppId));
            fixture.Igdb.Matches[bridgedAppId] = Match(bridgedAppId, 2000 + i, "Epic Game " + i);
        }

        var gog = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            var productId = "120765" + i;
            gog.Add((await fixture.AddAsync(
                ExternalIdProviders.Gog, productId, new Work { Name = "GOG Game " + i })).WorkId);

            // Source 5 — GOG's own external_game_source, the lookup that on the
            // real library had never run at all.
            fixture.Igdb.External[(5, productId)] = Match(productId, 3000 + i, "GOG Game " + i);
        }

        fixture.Igdb.Configured = true;

        // Cancel as the SECOND slice begins. IsConfiguredAsync is asked once per
        // slice, so this ends the run with slice one committed and nothing of
        // slice two written — the shape of a window closed mid-pass.
        using var cts = new CancellationTokenSource();
        fixture.Igdb.OnConfigurationCheck = slice =>
        {
            if (slice >= 2)
            {
                cts.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.EnrichAsync(cts.Token));

        // The property, stated three ways because all three failed in the field.
        var steamServed = await fixture.CountWithCoverAsync(steam);
        var epicServed = await fixture.CountWithCoverAsync(epic);
        var gogServed = await fixture.CountWithCoverAsync(gog);

        Assert.True(gogServed > 0, $"GOG was starved again: 0 of {gog.Count} enriched.");
        Assert.True(epicServed > 0, $"Epic was starved again: 0 of {epic.Count} enriched.");
        Assert.True(steamServed > 0, "Steam should still be served; this is round-robin, not a reversal.");

        // And it really was cut short — otherwise the assertions above would be
        // pinning nothing but "a completed run enriches everything".
        Assert.True(
            steamServed + epicServed + gogServed < steam.Count + epic.Count + gog.Count,
            "The run finished; this test asserts nothing unless the pass was genuinely bounded.");
    }

    /// <summary>
    /// The other half of the ordering rule: within a store, a work with nothing
    /// at all outranks a work missing one field. A user staring at placeholder
    /// art cares about the empty tile; the row that only wants a publisher can
    /// wait for the next launch.
    ///
    /// <para>Both works here are Steam, so the round-robin cannot be what
    /// separates them — only emptiness can.</para>
    /// </summary>
    [Fact]
    public async Task A_work_with_nothing_is_served_before_one_missing_a_single_field()
    {
        using var fixture = new EnrichmentFixture(sliceSize: 1);

        // Inserted FIRST, and missing only its publisher. Under the old
        // insertion-order sweep this row went first and consumed the one slice.
        var nearlyDone = await fixture.AddAsync("600", new Work
        {
            Name = "Nearly Done",
            IgdbId = 4242,
            FirstReleaseYear = 2011,
            Summary = "Has almost everything.",
            CoverUrl = "https://img/have.jpg",
        });

        // Inserted SECOND, and has nothing at all.
        var empty = await fixture.AddNamedAsync("601", "Empty");

        fixture.Igdb.Configured = true;
        fixture.Igdb.Matches["600"] = Match("600", 4242, "Nearly Done");
        fixture.Igdb.Matches["601"] = Match("601", 5151, "Empty");

        // BOTH have a publisher waiting. Without this the near-complete row
        // would have had nothing to write either way, and the test would pass
        // for the wrong reason — the run would have skipped it on an empty
        // patch rather than on priority.
        fixture.Igdb.Games[4242] = Game(4242, "Nearly Done", ["Late Publisher"]);
        fixture.Igdb.Games[5151] = Game(5151, "Empty", ["Publisher"]);

        using var cts = new CancellationTokenSource();
        fixture.Igdb.OnConfigurationCheck = slice =>
        {
            if (slice >= 2)
            {
                cts.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.EnrichAsync(cts.Token));

        Assert.Equal("https://img/co5151.jpg", (await fixture.WorkAsync(empty.WorkId)).CoverUrl);
        Assert.Null((await fixture.WorkAsync(nearlyDone.WorkId)).Publisher);
    }

    /// <summary>
    /// Ordering is only half the fix. The pass used to run every source step
    /// over the whole library before opening its first transaction, so a run
    /// cancelled during step 0 — the per-Epic-work gamesdb hop, one rate-limited
    /// request each — committed nothing whatsoever, in whatever order the rows
    /// had arrived. This pins that a slice which finished its sources has
    /// already been written by the time the next one starts.
    /// </summary>
    [Fact]
    public async Task Work_completed_before_the_cancellation_is_already_committed()
    {
        using var fixture = new EnrichmentFixture(sliceSize: 2);

        var first = await fixture.AddNamedAsync("600", "First");
        var second = await fixture.AddNamedAsync("601", "Second");
        var third = await fixture.AddNamedAsync("602", "Third");
        var fourth = await fixture.AddNamedAsync("603", "Fourth");

        fixture.Igdb.Configured = true;
        foreach (var (appId, igdbId) in new[] { ("600", 1L), ("601", 2L), ("602", 3L), ("603", 4L) })
        {
            fixture.Igdb.Matches[appId] = Match(appId, igdbId, "Game " + appId);
        }

        using var cts = new CancellationTokenSource();
        fixture.Igdb.OnConfigurationCheck = slice =>
        {
            if (slice >= 2)
            {
                cts.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.EnrichAsync(cts.Token));

        Assert.Equal(1, (await fixture.WorkAsync(first.WorkId)).IgdbId);
        Assert.Equal(2, (await fixture.WorkAsync(second.WorkId)).IgdbId);
        Assert.Null((await fixture.WorkAsync(third.WorkId)).IgdbId);
        Assert.Null((await fixture.WorkAsync(fourth.WorkId)).IgdbId);
    }

    /// <summary>
    /// Slicing must not change what a run that is allowed to finish produces.
    /// A pass split into six commits and a pass done in one have to agree, or
    /// the durability fix has quietly become a correctness bug.
    /// </summary>
    [Fact]
    public async Task Slicing_does_not_change_the_result_of_a_run_that_finishes()
    {
        using var fixture = new EnrichmentFixture(sliceSize: 2);

        var works = new List<long>();
        for (var i = 0; i < 11; i++)
        {
            var appId = (600 + i).ToString();
            works.Add((await fixture.AddNamedAsync(appId, "Game " + i)).WorkId);
            fixture.Igdb.Matches[appId] = Match(appId, 1000 + i, "Game " + i);
            fixture.Igdb.Games[1000 + i] = Game(1000 + i, "Game " + i, ["Publisher " + i]);
        }

        fixture.Igdb.Configured = true;

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(works.Count, report.MetadataFilled);
        Assert.Equal(works.Count, await fixture.CountWithCoverAsync(works));
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private static IgdbGame Game(long id, string name, IReadOnlyList<string> publishers)
        => new(id, name, null, null, null, IgdbGame.NoStrings, IgdbGame.NoStrings, publishers);

    /// <summary>
    /// A full <c>external_games</c> answer — everything the five metadata
    /// columns want, so a work this matched is unambiguously "enriched" and one
    /// the run never reached is unambiguously not.
    /// </summary>
    private static IgdbExternalMatch Match(string uid, long igdbId, string name)
        => new(uid, igdbId, name, $"https://img/co{igdbId}.jpg", 2012, "A summary.");

    private sealed record Seeded(long WorkId, long ReleaseId);

    // ── Epic: naming and classifying what the library endpoint returns bare ──

    /// <summary>
    /// The symptom, end to end: a tile titled
    /// <c>App 16a66a9f5630407d923429470bd5c967</c> becomes a tile titled
    /// <c>LEGO® Fortnite: Odyssey</c>.
    /// </summary>
    [Fact]
    public async Task An_Epic_work_with_no_local_title_is_named_from_the_catalog_service()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "8f33cce63b3f4a46aca59ff8c85ff1cd",
            new Work { Name = "App 8f33cce63b3f4a46aca59ff8c85ff1cd", NameIsProvisional = true });

        fixture.EpicCatalog.AddGame("8f33cce63b3f4a46aca59ff8c85ff1cd", "LEGO® Fortnite: Odyssey");

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Promoted);
        Assert.Equal("LEGO® Fortnite: Odyssey", await fixture.WorkNameAsync(work.WorkId));

        // And credited to the source that actually answered. This used to fall
        // through to the Steam store's counter, which reported "29 from the
        // Steam store" on the author's first real run for an endpoint that was
        // never asked about a single Epic id.
        Assert.Equal(0, report.FromIgdb);

        // The release moves with the work, or the title is only half promoted.
        Assert.Equal("LEGO® Fortnite: Odyssey", await fixture.ReleaseNameAsync(work.ReleaseId));
        Assert.False(await fixture.IsProvisionalAsync(work.WorkId));
    }

    [Fact]
    public async Task The_categories_are_stored_so_the_non_game_filter_can_read_them()
    {
        using var fixture = new EnrichmentFixture();
        var engine = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "3ddb1bad6e004b99a7192c1a29f2318a",
            new Work { Name = "App 3ddb1bad6e004b99a7192c1a29f2318a", NameIsProvisional = true });

        fixture.EpicCatalog.AddEngine("3ddb1bad6e004b99a7192c1a29f2318a", "Unreal Engine");

        await fixture.Service.EnrichAsync();

        var stored = await fixture.WorkAsync(engine.WorkId);

        // Named — the user owns this and has 320 minutes in it — and classified,
        // so the grid hides it while the toggle brings it back.
        Assert.Equal("Unreal Engine", stored.Name);
        Assert.Equal("engines,engines/ue4", stored.EpicCategories);
        Assert.True(NonGameEntries.IsNonGameEpicCategories(stored.EpicCategories));
    }

    /// <summary>
    /// The rule this codebase has already been bitten by twice, at the layer
    /// that would do the damage: a title that came from <c>catcache.bin</c> is
    /// never replaced, and an unanswered catalog never blanks a classification.
    /// </summary>
    [Fact]
    public async Task A_real_local_title_is_never_replaced_by_the_catalog_service()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "7a70b499513441c792b541d53505e0b2",
            new Work { Name = "Fez" });

        // Epic's own catalog spells some titles differently. The local reader is
        // authoritative for what it knows, so this must not land.
        fixture.EpicCatalog.AddGame("7a70b499513441c792b541d53505e0b2", "FEZ (Epic Edition)");

        await fixture.Service.EnrichAsync();

        Assert.Equal("Fez", await fixture.WorkNameAsync(work.WorkId));

        // The classification, which the local reader had no column for, IS
        // written — filling an empty column is not overwriting a full one.
        Assert.Equal("public,games,applications", (await fixture.WorkAsync(work.WorkId)).EpicCategories);
    }

    [Fact]
    public async Task A_catalog_that_cannot_answer_leaves_the_row_exactly_as_it_was()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "d2cc1433b55a4ba7b0a76e9485efa1d6",
            new Work { Name = "App d2cc1433b55a4ba7b0a76e9485efa1d6", NameIsProvisional = true });

        // Nothing recorded for this id: unreachable, unrecognised and not signed
        // in are all the same absence, deliberately.
        await fixture.Service.EnrichAsync();

        var stored = await fixture.WorkAsync(work.WorkId);
        Assert.Equal("App d2cc1433b55a4ba7b0a76e9485efa1d6", stored.Name);
        Assert.True(stored.NameIsProvisional);
        Assert.Null(stored.EpicCategories);
    }

    [Fact]
    public async Task An_already_classified_work_is_never_asked_again()
    {
        using var fixture = new EnrichmentFixture();
        await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "7a70b499513441c792b541d53505e0b2",
            new Work { Name = "Fez", EpicCategories = "public,games,applications" });

        await fixture.Service.EnrichAsync();

        // What a catalog item is called and what kind of thing it is do not
        // change. Asking again would spend an authenticated request per Epic
        // work per launch to relearn the same string.
        Assert.Empty(fixture.EpicCatalog.Asked);
    }

    [Fact]
    public async Task A_Steam_work_is_never_asked_about_and_never_takes_an_Epic_classification()
    {
        using var fixture = new EnrichmentFixture();
        var steam = await fixture.AddProvisionalAsync("620");

        // A catalog answer keyed by the same string. The provider guard is what
        // stops a Steam appid inheriting it.
        fixture.EpicCatalog.AddEngine("620", "Not Portal 2");

        await fixture.Service.EnrichAsync();

        Assert.Empty(fixture.EpicCatalog.Asked);
        var stored = await fixture.WorkAsync(steam.WorkId);
        Assert.Null(stored.EpicCategories);
        Assert.Equal("App 620", stored.Name);
    }

    [Fact]
    public async Task An_entry_the_catalog_classified_but_could_not_name_keeps_its_placeholder()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "0b41f0192f7f4f2691684581aedc0778",
            new Work { Name = "App 0b41f0192f7f4f2691684581aedc0778", NameIsProvisional = true });

        fixture.EpicCatalog.AddUntitled("0b41f0192f7f4f2691684581aedc0778", "hidden");

        await fixture.Service.EnrichAsync();

        var stored = await fixture.WorkAsync(work.WorkId);
        Assert.True(stored.NameIsProvisional);

        // Half an answer is still an answer for the half it covers: hidden from
        // the grid, and still asked about next run for a name.
        Assert.Equal("hidden", stored.EpicCategories);
    }

    [Fact]
    public async Task An_entry_with_no_categories_is_named_and_left_unclassified()
    {
        using var fixture = new EnrichmentFixture();
        var work = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "cd9e44a9d1b14b8d84923bb985bc1636",
            new Work { Name = "App cd9e44a9d1b14b8d84923bb985bc1636", NameIsProvisional = true });

        fixture.EpicCatalog.AddUncategorised("cd9e44a9d1b14b8d84923bb985bc1636", "Something Epic Sells");

        await fixture.Service.EnrichAsync();

        var stored = await fixture.WorkAsync(work.WorkId);
        Assert.Equal("Something Epic Sells", stored.Name);

        // NULL, not an empty string: "not known" must stay distinguishable, and
        // an empty string would satisfy "column is filled" forever.
        Assert.Null(stored.EpicCategories);
    }

    [Fact]
    public async Task A_catalog_client_that_throws_does_not_abort_the_pass()
    {
        using var fixture = new EnrichmentFixture();
        var epic = await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "7a70b499513441c792b541d53505e0b2",
            new Work { Name = "App 7a70b499513441c792b541d53505e0b2", NameIsProvisional = true });
        var steam = await fixture.AddProvisionalAsync("620");

        fixture.EpicCatalog.Throw = new InvalidOperationException("something unforeseen");
        fixture.Steam.Names["620"] = "Portal 2";

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Promoted);
        Assert.Equal("Portal 2", await fixture.WorkNameAsync(steam.WorkId));
        Assert.True(await fixture.IsProvisionalAsync(epic.WorkId));
    }

    [Fact]
    public async Task A_host_with_no_Epic_module_enriches_exactly_as_before()
    {
        // The module is opt-in and most installs will never register it. Its
        // absence must be a no-op, not a null reference.
        using var fixture = new EnrichmentFixture(epicCatalog: false);
        var steam = await fixture.AddProvisionalAsync("620");
        await fixture.AddAsync(
            ExternalIdProviders.Epic,
            "7a70b499513441c792b541d53505e0b2",
            new Work { Name = "App 7a70b499513441c792b541d53505e0b2", NameIsProvisional = true });

        fixture.Steam.Names["620"] = "Portal 2";

        var report = await fixture.Service.EnrichAsync();

        Assert.Equal(1, report.Promoted);
        Assert.Equal("Portal 2", await fixture.WorkNameAsync(steam.WorkId));
    }

    private sealed class EnrichmentFixture : IDisposable
    {
        private readonly TempDatabase _db = new();

        /// <param name="sliceSize">
        /// How many targets the pass commits at a time. Left at the production
        /// default for every test that is not about truncation; the starvation
        /// tests shrink it so a bounded run is a handful of works rather than a
        /// wall-clock wait.
        /// </param>
        /// <param name="sliceSize">See above.</param>
        /// <param name="epicCatalog">
        /// Whether the opt-in Epic catalog client is registered at all. False
        /// models the overwhelmingly common install: no Epic API module, no
        /// session, and every Epic work named by the launcher's own files.
        /// </param>
        public EnrichmentFixture(
            int sliceSize = EnrichmentSyncService.DefaultSliceSize, bool epicCatalog = true)
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);

            Planner = new EnrichmentLookupPlanner(
                IgdbOptions,
                [Aliases],
                Identity,
                NullLogger<EnrichmentLookupPlanner>.Instance);

            Service = new EnrichmentSyncService(
                Works, Releases, Igdb, Steam, SteamCmd, Planner, _db.Factory,
                NullLogger<EnrichmentSyncService>.Instance,
                epicCatalog ? EpicCatalog : null)
            {
                SliceSize = sliceSize,
            };
        }

        /// <summary>The source-id table. Defaults are the live IGDB values: Steam 1, GOG 5, Epic 26.</summary>
        public IgdbOptions IgdbOptions { get; } = new();

        /// <summary>Epic's catalogItemId → AppName map, as the launcher's local files would supply it.</summary>
        public FakeAliasSource Aliases { get; } = new();

        /// <summary>gamesdb's cross-store graph.</summary>
        public FakeIdentityGraph Identity { get; } = new();

        public EnrichmentLookupPlanner Planner { get; }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public FakeIgdbClient Igdb { get; } = new();

        public FakeSteamStoreClient Steam { get; } = new();

        public FakeBuildInfoClient SteamCmd { get; } = new();

        /// <summary>Epic's catalog service — the only source of an Epic title and its categories.</summary>
        public FakeEpicCatalogClient EpicCatalog { get; } = new();

        public EnrichmentSyncService Service { get; }

        public Task<Seeded> AddProvisionalAsync(string appId)
            => AddAsync(appId, new Work { Name = "App " + appId, NameIsProvisional = true });

        public Task<Seeded> AddNamedAsync(string appId, string name)
            => AddAsync(appId, new Work { Name = name });

        public async Task<string?> WorkNameAsync(long workId)
            => (await Works.GetAsync(workId))?.Name;

        public async Task<string?> ReleaseNameAsync(long releaseId)
            => (await Releases.GetAsync(releaseId))?.Name;

        public async Task<bool> IsProvisionalAsync(long workId)
            => (await Works.GetAsync(workId))?.NameIsProvisional ?? false;

        public async Task<Work> WorkAsync(long workId)
        {
            var work = await Works.GetAsync(workId);
            Assert.NotNull(work);
            return work;
        }

        /// <summary>
        /// How many of these works came out of the run with cover art. The
        /// cover is the right probe for starvation: it is the field the user
        /// literally sees missing, and unlike <c>igdb_id</c> it is written even
        /// for the losing half of a cross-store duplicate.
        /// </summary>
        public async Task<int> CountWithCoverAsync(IEnumerable<long> workIds)
        {
            var served = 0;
            foreach (var workId in workIds)
            {
                if ((await Works.GetAsync(workId))?.CoverUrl is not null)
                {
                    served++;
                }
            }

            return served;
        }

        public Task<Seeded> AddAsync(string appId, Work work)
            => AddAsync(ExternalIdProviders.Steam, appId, work);

        public async Task<Seeded> AddAsync(string provider, string providerId, Work work)
        {
            var name = work.Name;
            var workId = await Works.InsertAsync(work);
            var releaseId = await Releases.InsertAsync(new Release { WorkId = workId, Name = name });
            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = provider,
                ProviderId = providerId,
            });

            return new Seeded(workId, releaseId);
        }

        public void Dispose() => _db.Dispose();
    }

    /// <summary>
    /// Stands in for Epic's catalog service. Answers only for ids a test put in
    /// <see cref="Items"/>; everything else is absent, which is the contract's
    /// "learned nothing about this item".
    /// </summary>
    private sealed class FakeEpicCatalogClient : IEpicCatalogClient
    {
        public Dictionary<string, EpicCatalogItemInfo> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every id the pass actually asked about, in order.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>Thrown from the lookup, the way an unforeseen client bug would.</summary>
        public Exception? Throw { get; set; }

        /// <summary>Records a game the way the live service returns one.</summary>
        public void AddGame(string catalogItemId, string title, string appName = "Codename")
            => Items[catalogItemId] = new EpicCatalogItemInfo(
                catalogItemId, "ns", title, ["public", "games", "applications"], appName, null);

        /// <summary>Records an Unreal Engine build — owned, and not a game.</summary>
        public void AddEngine(string catalogItemId, string title, string appName = "UE_4.0")
            => Items[catalogItemId] = new EpicCatalogItemInfo(
                catalogItemId, "ns", title, ["engines", "engines/ue4"], appName, null);

        /// <summary>Records an entry the service classified but could not name.</summary>
        public void AddUntitled(string catalogItemId, params string[] categories)
            => Items[catalogItemId] = new EpicCatalogItemInfo(
                catalogItemId, "ns", null, categories, null, null);

        /// <summary>Records an entry with a name and no categories to judge it by.</summary>
        public void AddUncategorised(string catalogItemId, string title)
            => Items[catalogItemId] = new EpicCatalogItemInfo(catalogItemId, "ns", title, [], null, null);

        public Task<IReadOnlyDictionary<string, EpicCatalogItemInfo>> GetItemsAsync(
            IReadOnlyCollection<string> catalogItemIds, CancellationToken ct = default)
        {
            Asked.AddRange(catalogItemIds);

            if (Throw is not null)
            {
                throw Throw;
            }

            var answers = new Dictionary<string, EpicCatalogItemInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in catalogItemIds)
            {
                if (Items.TryGetValue(id, out var item))
                {
                    answers[id] = item;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, EpicCatalogItemInfo>>(answers);
        }
    }

    /// <summary>
    /// Stands in for IGDB, including the states this machine cannot reach: no
    /// credentials at all, and credentials that exist but whose token mint
    /// fails.
    /// </summary>
    private sealed class FakeIgdbClient : IIgdbClient
    {
        public bool Configured { get; set; }

        /// <summary>Thrown from the lookup, the way a dead Twitch endpoint would.</summary>
        public Exception? Throw { get; set; }

        /// <summary>Name-only answers: the <c>external_games</c> shape with no metadata.</summary>
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);

        /// <summary>Full <c>external_games</c> answers, when a test cares about the metadata.</summary>
        public Dictionary<string, IgdbExternalMatch> Matches { get; } = new(StringComparer.Ordinal);

        /// <summary>The second call: <c>/games</c>, the only source of the publisher.</summary>
        public Dictionary<long, IgdbGame> Games { get; } = [];

        public List<string> Asked { get; } = [];

        public List<long> GameIdsAsked { get; } = [];

        /// <summary>
        /// Called with the running count at the start of every
        /// <see cref="IsConfiguredAsync"/>, which the pass asks exactly once per
        /// slice. That makes it the one hook a test can use to end a run on a
        /// known slice boundary, rather than by counting lookups (whose number
        /// per slice depends on how many external_game_sources the slice
        /// happened to span) or by racing a wall clock.
        /// </summary>
        public Action<int>? OnConfigurationCheck { get; set; }

        private int _configurationChecks;

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        {
            OnConfigurationCheck?.Invoke(++_configurationChecks);
            return ValueTask.FromResult(Configured);
        }

        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(
            IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            var requested = appIds.ToArray();
            Asked.AddRange(requested);

            if (Throw is not null)
            {
                throw Throw;
            }

            var matched = new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal);
            foreach (var appId in requested)
            {
                if (Matches.TryGetValue(appId, out var match))
                {
                    matched[appId] = match;
                }
                else if (Names.TryGetValue(appId, out var name))
                {
                    matched[appId] = new IgdbExternalMatch(appId, 1, name, null, null, null);
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(matched);
        }

        /// <summary>
        /// Answers keyed by (external_game_source, uid) — GOG's source 5 and
        /// anything else that is not Steam. Kept apart from <see cref="Matches"/>
        /// on purpose: a uid is only unique within its source, and a fake that
        /// merged them would make a test pass that a real cache-key collision
        /// would fail.
        /// </summary>
        public Dictionary<(int Source, string Uid), IgdbExternalMatch> External { get; } = [];

        /// <summary>Every (source, uid) pair asked for, in order.</summary>
        public List<(int Source, string Uid)> AskedExternal { get; } = [];

        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(
            int externalGameSourceId,
            IEnumerable<string> uids,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
        {
            var requested = uids.ToArray();
            foreach (var uid in requested)
            {
                AskedExternal.Add((externalGameSourceId, uid));
            }

            if (externalGameSourceId == 1)
            {
                return ResolveBySteamAppIdsAsync(requested, cacheTtl, ct);
            }

            if (Throw is not null)
            {
                throw Throw;
            }

            var matched = new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal);
            foreach (var uid in requested)
            {
                if (External.TryGetValue((externalGameSourceId, uid), out var match))
                {
                    matched[uid] = match;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(matched);
        }

        public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
            IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            var requested = igdbIds.ToArray();
            GameIdsAsked.AddRange(requested);

            var found = new List<IgdbGame>();
            foreach (var id in requested)
            {
                if (Games.TryGetValue(id, out var game))
                {
                    found.Add(game);
                }
            }

            return Task.FromResult<IReadOnlyList<IgdbGame>>(found);
        }
    }

    private sealed class FakeSteamStoreClient : ISteamStoreClient
    {
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyDictionary<string, SteamStoreItem>> GetItemsAsync(
            IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            var requested = appIds.ToArray();
            Asked.AddRange(requested);

            var items = new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);
            foreach (var appId in requested)
            {
                if (Names.TryGetValue(appId, out var name))
                {
                    items[appId] = new SteamStoreItem(appId, name, SteamStoreItem.NoTags);
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, SteamStoreItem>>(items);
        }

        public Task<SteamTagVocabulary> GetTagListAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(SteamTagVocabulary.Empty);

        public Task<SteamStoreCategoryVocabulary> GetStoreCategoriesAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(SteamStoreCategoryVocabulary.Empty);
    }

    /// <summary>
    /// Stands in for api.steamcmd.net — the third and last name source.
    /// <see cref="Asked"/> records the appids a REQUEST would have been made
    /// for; a <c>cachedOnly</c> read is recorded separately, because "we spent a
    /// call at the volunteer service" and "we looked at what was already on
    /// disk" are the two things these tests most need to tell apart.
    /// </summary>
    private sealed class FakeBuildInfoClient : IBuildInfoClient
    {
        /// <summary>Appids the fake will answer about, whether asked live or from cache.</summary>
        public Dictionary<string, SteamAppInfo> Infos { get; } = new(StringComparer.Ordinal);

        /// <summary>Appids whose body is "already cached", so a cachedOnly read finds them.</summary>
        public HashSet<string> Cached { get; } = new(StringComparer.Ordinal);

        /// <summary>Appids a live request was made for.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>Appids read cache-only, at no cost.</summary>
        public List<string> Peeked { get; } = [];

        /// <summary>Thrown from every call, the way a dead host would.</summary>
        public Exception? Throw { get; set; }

        public void Add(string appId, string? name, string? type, string? parent = null)
            => Infos[appId] = new SteamAppInfo(appId, name, type, parent);

        public Task<BuildInfoFetch> GetPublicBranchAsync(
            string appId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(BuildInfoFetch.Unavailable);

        public Task<AppInfoFetch> GetAppInfoAsync(
            string appId,
            TimeSpan? cacheTtl = null,
            bool cachedOnly = false,
            CancellationToken ct = default)
        {
            if (cachedOnly)
            {
                Peeked.Add(appId);

                if (!Cached.Contains(appId))
                {
                    return Task.FromResult(AppInfoFetch.Unavailable);
                }
            }
            else
            {
                Asked.Add(appId);
            }

            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(Infos.TryGetValue(appId, out var info)
                ? AppInfoFetch.Ok(info)

                // The restricted shape: the service answered and was not allowed
                // to say. Not a failure, and not a name.
                : AppInfoFetch.NoData);
        }
    }

    /// <summary>
    /// Epic's <c>catalogItemId → AppName</c> map, as the launcher's local files
    /// would supply it. Empty is the normal state of a machine with no Epic
    /// launcher, and these tests pin that it means "cannot say" rather than
    /// "these titles have no alias".
    /// </summary>
    private sealed class FakeAliasSource : IStoreArtifactAliasSource
    {
        public Dictionary<string, string> Epic { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Thrown from every call, the way an unreadable ProgramData would.</summary>
        public Exception? Throw { get; set; }

        public ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(
            string provider, CancellationToken ct = default)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                provider == ExternalIdProviders.Epic
                    ? Epic
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// gamesdb's cross-store graph. Answers only about pairs a test taught it,
    /// and distinguishes the two outcomes that matter: null (no release under
    /// that id, or the service could not be reached) from a game with no
    /// release on the platform being asked for.
    /// </summary>
    private sealed class FakeIdentityGraph : IGameIdentityGraph
    {
        private readonly Dictionary<(string Platform, string Id), GamesDbGame> _games = [];

        /// <summary>Every lookup made, in order.</summary>
        public List<(string Platform, string Id)> Asked { get; } = [];

        /// <summary>Thrown from every call, the way an unhandled transport failure would.</summary>
        public Exception? Throw { get; set; }

        public void Add(string platform, string externalId, string gameId, params (string, string)[] releases)
            => _games[(platform, externalId)] = new GamesDbGame(
                platform,
                externalId,
                gameId,
                releases.Select(r => new GamesDbRelease(r.Item1, r.Item2)).ToArray());

        public Task<GamesDbGame?> ResolveAsync(
            string platform, string externalId, CancellationToken ct = default)
        {
            Asked.Add((platform, externalId));

            if (Throw is not null)
            {
                throw Throw;
            }

            return Task.FromResult(_games.GetValueOrDefault((platform, externalId)));
        }
    }
}
