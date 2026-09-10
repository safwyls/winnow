using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Model;
using Winnow.Enrich.Igdb;
using Xunit;

namespace Winnow.Tests;

public sealed class GamesDbIdentitySyncTests
{
    [Fact]
    public async Task Repository_refuses_snapshot_when_user_moves_evidence_work_to_another_parent()
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Epic");
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Steam");
        var parent = await f.AddAsync(ExternalIdProviders.Gog, "123", "First parent");
        var newParent = await f.AddAsync(ExternalIdProviders.Gog, "456", "New parent");
        await f.Links.LinkAsync(new IdentityLinkRequest { ParentWorkId = parent.WorkId, ChildWorkIds = [epic.WorkId] });
        var staleRequest = new IdentityLinkRequest
        {
            ParentWorkId = parent.WorkId, ChildWorkIds = [steam.WorkId], Source = IdentityLinkSources.HardId,
            ExpectedSameGameRoots = new Dictionary<long, long> { [epic.WorkId] = parent.WorkId, [steam.WorkId] = steam.WorkId },
        };
        await f.Links.LinkAsync(new IdentityLinkRequest { ParentWorkId = newParent.WorkId, ChildWorkIds = [epic.WorkId] });
        var history = await f.Links.GetHistoryAsync();
        var acts = await f.Links.GetActsAsync();

        await Assert.ThrowsAsync<IdentityLinkRefusedException>(() => f.Links.LinkAsync(staleRequest));

        Assert.Equal(history, await f.Links.GetHistoryAsync());
        Assert.Equal(acts, await f.Links.GetActsAsync());
        Assert.Equal(newParent.WorkId, (await f.Links.GetResolutionAsync()).SameGame.Resolve(epic.WorkId));
    }

    [Theory]
    [InlineData("rejection")]
    [InlineData("pin")]
    [InlineData("unlink")]
    [InlineData("existing-child")]
    public async Task Repository_rechecks_user_decisions_after_a_background_snapshot(string decision)
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Epic");
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Steam");
        var staleRequest = new IdentityLinkRequest
        {
            ParentWorkId = steam.WorkId, ChildWorkIds = [epic.WorkId], Source = IdentityLinkSources.HardId,
        };
        if (decision == "rejection")
        {
            var candidate = await f.QueueAsync(epic, steam);
            await f.Candidates.SetStatusAsync(candidate, MergeCandidateStatuses.Rejected);
        }
        else if (decision == "pin")
        {
            await f.Pins.PinAsync(new WorkIgdbPinAssignment { WorkId = epic.WorkId, IgdbId = 1234 });
        }
        else if (decision == "unlink")
        {
            await f.Links.LinkAsync(staleRequest with { Source = IdentityLinkSources.User });
            Assert.True(await f.Links.RetractLinkAsync(epic.WorkId));
        }
        else
        {
            var third = await f.AddAsync(ExternalIdProviders.Gog, "123", "User-chosen parent");
            await f.Links.LinkAsync(new IdentityLinkRequest { ParentWorkId = third.WorkId, ChildWorkIds = [epic.WorkId] });
        }
        var history = await f.Links.GetHistoryAsync();
        var acts = await f.Links.GetActsAsync();

        await Assert.ThrowsAsync<IdentityLinkRefusedException>(() => f.Links.LinkAsync(staleRequest));

        Assert.Equal(history, await f.Links.GetHistoryAsync());
        Assert.Equal(acts, await f.Links.GetActsAsync());
    }

    [Fact]
    public async Task Automatic_link_unifies_library_identity_and_preserves_each_ownership_row()
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Epic");
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Steam");
        await f.Ownerships.InsertAsync(new Ownership { ReleaseId = epic.ReleaseId, Store = "epic" });
        await f.Ownerships.InsertAsync(new Ownership { ReleaseId = steam.ReleaseId, Store = "steam" });
        var before = await f.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(2, before.Select(r => r.ResolvedWorkId).Distinct().Count());
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, "620")];

        Assert.Equal(1, await f.Service.SyncAsync());

        var after = await f.Queries.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(2, after.Count);
        Assert.Single(after.Select(r => r.ResolvedWorkId).Distinct());
        Assert.Equal(before.Select(r => (r.OwnershipId, r.ReleaseId, r.WorkId, r.Bucket, r.PlaytimeMinutes)),
            after.Select(r => (r.OwnershipId, r.ReleaseId, r.WorkId, r.Bucket, r.PlaytimeMinutes)));
    }

    [Fact]
    public async Task All_exact_counterparts_form_one_identity_without_duplicate_links_on_repeat()
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Epic");
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Steam");
        var secondSteam = await f.AddAsync(ExternalIdProviders.Steam, "621", "Steam edition");
        var gog = await f.AddAsync(ExternalIdProviders.Gog, "1207658924", "GOG");
        await f.QueueAsync(epic, steam);
        await f.QueueAsync(steam, gog);
        await f.QueueAsync(secondSteam, gog);
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, "steam_620"), (GamesDbPlatforms.Steam, "620"),
            (GamesDbPlatforms.Steam, "621"), (GamesDbPlatforms.Gog, "1207658924"), (GamesDbPlatforms.Steam, "620")];

        Assert.Equal(3, await f.Service.SyncAsync());
        var resolution = (await f.Links.GetResolutionAsync()).SameGame;
        Assert.Single(new[] { epic, steam, secondSteam, gog }.Select(s => resolution.Resolve(s.WorkId)).Distinct());
        Assert.Equal(0, await f.Candidates.CountPendingAsync());
        Assert.Equal(0, await f.Service.SyncAsync());
        Assert.Equal(3, (await f.Links.GetHistoryAsync()).Count);
    }

    [Fact]
    public async Task Cancellation_from_identity_graph_propagates_without_writing_links()
    {
        using var f = new Fixture();
        await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Epic");
        await f.AddAsync(ExternalIdProviders.Steam, "620", "Steam");
        f.Graph.Failure = new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.SyncAsync());
        Assert.Empty(await f.Links.GetHistoryAsync());
    }

    [Fact]
    public async Task Exact_ids_clear_queue_entries_and_preserve_both_releases_and_external_ids()
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Gold Edition", fullMetadata: true);
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Base Game", fullMetadata: true);
        var unrelated = await f.AddAsync(ExternalIdProviders.Steam, "570", "Gold Edition");
        var candidate = await f.QueueAsync(epic, steam);
        await f.QueueAsync(epic, unrelated);
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, "620")];

        Assert.Equal(2, await f.Candidates.CountPendingAsync());
        Assert.Equal(1, await f.Service.SyncAsync());
        Assert.Equal(1, await f.Candidates.CountPendingAsync());
        Assert.Null(await f.Candidates.GetAsync(candidate));
        var link = Assert.Single(await f.Links.GetHistoryAsync());
        Assert.Equal(IdentityLinkKinds.SameGame, link.Kind);
        Assert.Equal(IdentityLinkSources.HardId, link.Source);
        Assert.False(string.IsNullOrWhiteSpace(link.EvidenceJson));
        var resolution = (await f.Links.GetResolutionAsync()).SameGame;
        Assert.Equal(resolution.Resolve(epic.WorkId), resolution.Resolve(steam.WorkId));
        Assert.NotEqual(resolution.Resolve(epic.WorkId), resolution.Resolve(unrelated.WorkId));
        Assert.Equal(epic.WorkId, (await f.Releases.GetAsync(epic.ReleaseId))!.WorkId);
        Assert.Equal(steam.WorkId, (await f.Releases.GetAsync(steam.ReleaseId))!.WorkId);
        Assert.Equal("Gold Edition", (await f.Releases.GetAsync(epic.ReleaseId))!.Name);
        Assert.Equal("catalog", Assert.Single(await f.Releases.GetExternalIdsAsync(epic.ReleaseId)).ProviderId);
        Assert.Equal("620", Assert.Single(await f.Releases.GetExternalIdsAsync(steam.ReleaseId)).ProviderId);
        Assert.Equal(steam.ReleaseId, (await f.Releases.FindByExternalIdAsync(ExternalIdProviders.Steam, "620"))!.Id);
        Assert.Equal(0, await f.Service.SyncAsync());
        Assert.Single(await f.Links.GetHistoryAsync());
        Assert.Single(await f.Candidates.GetAllAsync());
    }

    [Theory]
    [InlineData(ExternalIdProviders.Steam, GamesDbPlatforms.Steam, "620")]
    [InlineData(ExternalIdProviders.Gog, GamesDbPlatforms.Gog, "1207658924")]
    public async Task Late_store_import_is_linked_even_when_epic_metadata_is_already_complete(
        string provider, string platform, string id)
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Already enriched", fullMetadata: true);
        f.Graph.ReleaseIds = [(platform, id)];
        Assert.Equal(0, await f.Service.SyncAsync());
        var other = await f.AddAsync(provider, id, "Different storefront title");

        Assert.Equal(1, await f.Service.SyncAsync());
        Assert.Empty(await f.Candidates.GetAllAsync());
        var resolution = (await f.Links.GetResolutionAsync()).SameGame;
        Assert.Equal(resolution.Resolve(epic.WorkId), resolution.Resolve(other.WorkId));
    }

    [Theory]
    [InlineData("steam_620")]
    [InlineData(" 620")]
    [InlineData("-620")]
    [InlineData("")]
    public async Task Malformed_graph_ids_do_not_create_identity(string graphId)
    {
        using var f = new Fixture();
        await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Same name");
        await f.AddAsync(ExternalIdProviders.Steam, "620", "Same name");
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, graphId)];

        Assert.Equal(0, await f.Service.SyncAsync());
        Assert.Empty(await f.Links.GetHistoryAsync());
        Assert.Empty(await f.Candidates.GetAllAsync());
    }

    [Theory]
    [InlineData("missing-alias")]
    [InlineData("graph-failure")]
    [InlineData("graph-miss")]
    public async Task Missing_or_failed_evidence_keeps_pending_pair(string failure)
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Same name");
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Same name");
        await f.QueueAsync(epic, steam);
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, "620")];
        if (failure == "missing-alias") f.Aliases.Items.Clear();
        if (failure == "graph-failure") f.Graph.Failure = new HttpRequestException("offline");
        if (failure == "graph-miss") f.Graph.Missing = true;

        Assert.Equal(0, await f.Service.SyncAsync());
        Assert.Equal(1, await f.Candidates.CountPendingAsync());
        Assert.Empty(await f.Links.GetHistoryAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_pair_is_not_automatically_linked(bool reversed)
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Epic");
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Steam");
        var id = await f.QueueAsync(reversed ? steam : epic, reversed ? epic : steam);
        await f.Candidates.SetStatusAsync(id, MergeCandidateStatuses.Rejected);
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, "620")];

        Assert.Equal(0, await f.Service.SyncAsync());
        Assert.Empty(await f.Links.GetHistoryAsync());
        Assert.Equal(MergeCandidateStatuses.Rejected, (await f.Candidates.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Separating_an_automatic_link_survives_the_next_sync()
    {
        using var f = new Fixture();
        await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Epic");
        await f.AddAsync(ExternalIdProviders.Steam, "620", "Steam");
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, "620")];
        Assert.Equal(1, await f.Service.SyncAsync());
        var link = Assert.Single(await f.Links.GetHistoryAsync());
        Assert.True(await f.Links.RetractLinkAsync(link.ChildWorkId));

        Assert.Equal(0, await f.Service.SyncAsync());
        Assert.DoesNotContain(await f.Links.GetHistoryAsync(), l => l.IsLive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task User_pin_on_either_work_prevents_automatic_identity(bool pinEpic)
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Epic");
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Steam");
        await f.Pins.PinAsync(new WorkIgdbPinAssignment { WorkId = pinEpic ? epic.WorkId : steam.WorkId, IgdbId = 1234 });
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, "620")];

        Assert.Equal(0, await f.Service.SyncAsync());
        Assert.Empty(await f.Links.GetHistoryAsync());
    }

    [Theory]
    [InlineData(IdentityLinkKinds.ExpansionOf)]
    [InlineData(IdentityLinkKinds.VariantOf)]
    public async Task Existing_non_identity_membership_is_preserved(string kind)
    {
        using var f = new Fixture();
        var epic = await f.AddAsync(ExternalIdProviders.Epic, "catalog", "Expansion");
        var steam = await f.AddAsync(ExternalIdProviders.Steam, "620", "Base");
        await f.Links.LinkAsync(new IdentityLinkRequest { ParentWorkId = steam.WorkId, ChildWorkIds = [epic.WorkId], Kind = kind });
        f.Graph.ReleaseIds = [(GamesDbPlatforms.Steam, "620")];

        Assert.Equal(0, await f.Service.SyncAsync());
        Assert.Equal(kind, Assert.Single(await f.Links.GetHistoryAsync(), l => l.IsLive).Kind);
        Assert.True((await f.Links.GetResolutionAsync()).SameGame.IsEmpty);
    }

    private sealed record Seeded(long WorkId, long ReleaseId);

    private sealed class Fixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        public WorkRepository Works { get; }
        public ReleaseRepository Releases { get; }
        public IdentityLinkRepository Links { get; }
        public MergeCandidateRepository Candidates { get; }
        public WorkIgdbPinRepository Pins { get; }
        public OwnershipRepository Ownerships { get; }
        public LibraryQueryRepository Queries { get; }
        public AliasSource Aliases { get; } = new();
        public IdentityGraph Graph { get; } = new();
        public GamesDbIdentitySyncService Service { get; }

        public Fixture()
        {
            Works = new(_db.Factory);
            Releases = new(_db.Factory);
            Links = new(_db.Factory);
            Candidates = new(_db.Factory);
            Pins = new(_db.Factory);
            Ownerships = new(_db.Factory);
            Queries = new(_db.Factory);
            Service = new(Releases, Links, Candidates, Pins,
                new EnrichmentLookupPlanner(new IgdbOptions(), [Aliases], Graph),
                NullLogger<GamesDbIdentitySyncService>.Instance);
        }

        public async Task<Seeded> AddAsync(string provider, string id, string name, bool fullMetadata = false)
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = name,
                CoverUrl = fullMetadata ? "https://example.test/cover.jpg" : null,
                Summary = fullMetadata ? "Already enriched" : null,
                FirstReleaseYear = fullMetadata ? 2020 : null,
                Publisher = fullMetadata ? "Publisher" : null,
            });
            var releaseId = await Releases.InsertAsync(new Release { WorkId = workId, Name = name });
            await Releases.AddExternalIdAsync(new ExternalId { ReleaseId = releaseId, Provider = provider, ProviderId = id });
            return new(workId, releaseId);
        }

        public Task<long> QueueAsync(Seeded left, Seeded right) => Candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = left.ReleaseId, RightReleaseId = right.ReleaseId, Score = .9,
        });

        public void Dispose() => _db.Dispose();
    }

    private sealed class AliasSource : IStoreArtifactAliasSource
    {
        public Dictionary<string, string> Items { get; } = new() { ["catalog"] = "ArtifactName" };
        public ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(string provider, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyDictionary<string, string>>(Items);
    }

    private sealed class IdentityGraph : IGameIdentityGraph
    {
        public (string Platform, string Id)[] ReleaseIds { get; set; } = [];
        public Exception? Failure { get; set; }
        public bool Missing { get; set; }
        public Task<GamesDbGame?> ResolveAsync(string platform, string externalId, CancellationToken ct = default)
        {
            if (Failure is not null) throw Failure;
            Assert.Equal(GamesDbPlatforms.Epic, platform);
            Assert.Equal("ArtifactName", externalId);
            return Task.FromResult<GamesDbGame?>(Missing ? null : new GamesDbGame(platform, externalId, "game-123",
                ReleaseIds.Select(r => new GamesDbRelease(r.Platform, r.Id)).ToArray()));
        }
    }
}
