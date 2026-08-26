using Hoard.App.Services;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Hoard.Data.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Igdb.Model;
using Hoard.Enrich.Steam;
using Hoard.Enrich.Steam.Model;
using Hoard.Enrich.Updates;
using Hoard.Enrich.Updates.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The pass that turns <c>App 1203620</c> into <c>Portal 2</c>.
///
/// <para>Three properties are worth pinning, and all three are about what
/// happens when something goes wrong rather than when everything works.
/// <b>Isolation:</b> IGDB is the backbone (§4.4) but it needs credentials this
/// machine does not have and a Twitch endpoint that can be down, and neither
/// may take the credential-free Steam fallback down with it. <b>Idempotency:</b>
/// this runs on every launch and must cost one indexed query once the backlog
/// is drained. <b>One-way promotion:</b> a real title is never overwritten by a
/// placeholder — the failure that would rename a user's library back to appids.
/// </para>
///
/// <para>Both clients are fakes. Nothing here touches the network, and no IGDB
/// credentials are needed or used: the fakes stand in for exactly the
/// behaviours a live run would exhibit, including the ones that only occur when
/// IGDB is unreachable.</para>
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
        fixture.Igdb.Matches["620"] = new IgdbSteamMatch(
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
        fixture.Igdb.Matches["620"] = new IgdbSteamMatch("620", 7346, "Portal 2", null, 2011, null);
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
        fixture.Igdb.Matches["620"] = new IgdbSteamMatch("620", 7346, "Skyrim", null, 2011, null);
        fixture.Igdb.Matches["621"] = new IgdbSteamMatch("621", 7347, "Skyrim", null, 2011, null);
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
        fixture.Igdb.Matches["620"] = new IgdbSteamMatch("620", 7346, "Portal 2", null, null, null);
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
        fixture.Igdb.Matches["620"] = new IgdbSteamMatch("620", 7346, "Portal 2", "   ", 2011, "  ");
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
        fixture.Igdb.Matches["620"] = new IgdbSteamMatch(
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
        fixture.Igdb.Matches["620"] = new IgdbSteamMatch(
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
        fixture.Igdb.Matches["63500"] = new IgdbSteamMatch("63500", 4123, "Riven", null, 1997, "Myst II.");
        fixture.Igdb.Matches["63501"] = new IgdbSteamMatch("63501", 4123, "Riven", null, 1997, "Myst II.");
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

    // ── Fixture ──────────────────────────────────────────────────────────────

    private static IgdbGame Game(long id, string name, IReadOnlyList<string> publishers)
        => new(id, name, null, null, null, IgdbGame.NoStrings, IgdbGame.NoStrings, publishers);

    private sealed record Seeded(long WorkId, long ReleaseId);

    private sealed class EnrichmentFixture : IDisposable
    {
        private readonly TempDatabase _db = new();

        public EnrichmentFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);

            Service = new EnrichmentSyncService(
                Works, Releases, Igdb, Steam, SteamCmd, _db.Factory,
                NullLogger<EnrichmentSyncService>.Instance);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public FakeIgdbClient Igdb { get; } = new();

        public FakeSteamStoreClient Steam { get; } = new();

        public FakeBuildInfoClient SteamCmd { get; } = new();

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

        public async Task<Seeded> AddAsync(string appId, Work work)
        {
            var name = work.Name;
            var workId = await Works.InsertAsync(work);
            var releaseId = await Releases.InsertAsync(new Release { WorkId = workId, Name = name });
            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = appId,
            });

            return new Seeded(workId, releaseId);
        }

        public void Dispose() => _db.Dispose();
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
        public Dictionary<string, IgdbSteamMatch> Matches { get; } = new(StringComparer.Ordinal);

        /// <summary>The second call: <c>/games</c>, the only source of the publisher.</summary>
        public Dictionary<long, IgdbGame> Games { get; } = [];

        public List<string> Asked { get; } = [];

        public List<long> GameIdsAsked { get; } = [];

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Configured);

        public Task<IReadOnlyDictionary<string, IgdbSteamMatch>> ResolveBySteamAppIdsAsync(
            IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            var requested = appIds.ToArray();
            Asked.AddRange(requested);

            if (Throw is not null)
            {
                throw Throw;
            }

            var matched = new Dictionary<string, IgdbSteamMatch>(StringComparer.Ordinal);
            foreach (var appId in requested)
            {
                if (Matches.TryGetValue(appId, out var match))
                {
                    matched[appId] = match;
                }
                else if (Names.TryGetValue(appId, out var name))
                {
                    matched[appId] = new IgdbSteamMatch(appId, 1, name, null, null, null);
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, IgdbSteamMatch>>(matched);
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
}
