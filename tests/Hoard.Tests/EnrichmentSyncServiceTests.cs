using Hoard.App.Services;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Hoard.Data.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Igdb.Model;
using Hoard.Enrich.Steam;
using Hoard.Enrich.Steam.Model;
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

    // ── Fixture ──────────────────────────────────────────────────────────────

    private sealed record Seeded(long WorkId, long ReleaseId);

    private sealed class EnrichmentFixture : IDisposable
    {
        private readonly TempDatabase _db = new();

        public EnrichmentFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);

            Service = new EnrichmentSyncService(
                Works, Releases, Igdb, Steam, _db.Factory,
                NullLogger<EnrichmentSyncService>.Instance);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public FakeIgdbClient Igdb { get; } = new();

        public FakeSteamStoreClient Steam { get; } = new();

        public EnrichmentSyncService Service { get; }

        public Task<Seeded> AddProvisionalAsync(string appId)
            => AddAsync(appId, "App " + appId, provisional: true);

        public Task<Seeded> AddNamedAsync(string appId, string name)
            => AddAsync(appId, name, provisional: false);

        public async Task<string?> WorkNameAsync(long workId)
            => (await Works.GetAsync(workId))?.Name;

        public async Task<string?> ReleaseNameAsync(long releaseId)
            => (await Releases.GetAsync(releaseId))?.Name;

        public async Task<bool> IsProvisionalAsync(long workId)
            => (await Works.GetAsync(workId))?.NameIsProvisional ?? false;

        private async Task<Seeded> AddAsync(string appId, string name, bool provisional)
        {
            var workId = await Works.InsertAsync(new Work { Name = name, NameIsProvisional = provisional });
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

        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

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
                if (Names.TryGetValue(appId, out var name))
                {
                    matched[appId] = new IgdbSteamMatch(appId, 1, name, null, null, null);
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, IgdbSteamMatch>>(matched);
        }

        public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
            IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IgdbGame>>([]);
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
    }
}
