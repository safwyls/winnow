using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Identity;
using Winnow.Api.Contracts.Library;
using Winnow.Api.Contracts.Preferences;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Backend.Tests;

/// <summary>
/// The original group-header and user-name fixtures through Electron's authenticated API.
/// Canonical library metadata stays independent of the header preference; renderer tests
/// verify the displayed header, launch route, unavailable label, search and row labels.
/// </summary>
public sealed class NameAndHeaderParityTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Saved_header_choice_survives_reload_and_ingest_without_changing_canonical_metadata_or_identity_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var (steam, gog) = await fixture.SeedHeaderGroup();
        var before = await fixture.Review();
        Assert.True((await fixture.SetHeader(steam, "gog")).Changed);

        using var reopened = fixture.ReopenClient();
        var saved = await reopened.GetAsync<IdentityReviewResponse>("identity/review");
        Assert.Equal("gog", saved.Workspace.PreferredHeaderStores[steam.WorkId]);
        Assert.Equal(before.History, saved.History);
        Assert.Equal(RecordFacts(before.Workspace), RecordFacts(saved.Workspace));
        var game = Assert.Single((await reopened.GetLibraryAsync()).Games);
        Assert.Equal((steam.WorkId, "Steam title", 2011), (game.WorkId, game.Title, game.FirstReleaseYear));
        Assert.Equal(new[] { steam.WorkId, gog.WorkId }, game.Entries.Select(entry => entry.WorkId));
        Assert.Equal("GOG title", Assert.Single(saved.Workspace.Works, work => work.Id == gog.WorkId).Name);

        await fixture.RestoreOwnership(gog);
        Assert.Equal("gog", (await reopened.GetWorkspaceAsync()).PreferredHeaderStores[steam.WorkId]);
        Assert.True((await fixture.SetHeader(steam, null)).Changed);
        var automatic = await fixture.Review();
        Assert.Null(automatic.Workspace.PreferredHeaderStores[steam.WorkId]);
        Assert.Equal(before.History, automatic.History);
        Assert.Equal("Steam title", Assert.Single((await reopened.GetLibraryAsync()).Games).Title);
    }

    [Fact]
    public async Task Header_choice_follows_regrouping_and_undo_restores_the_original_anchor_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var (steam, gog) = await fixture.SeedHeaderGroup();
        Assert.True((await fixture.SetHeader(steam, "gog")).Changed);
        var epic = await fixture.SeedHeader("Epic title", "epic");
        var act = await fixture.Link(epic, steam);
        var joined = await fixture.Review();
        Assert.Equal("gog", joined.Workspace.PreferredHeaderStores[epic.WorkId]);
        Assert.False((await fixture.SetHeader(steam, "steam")).Changed);
        Assert.Equal(epic.WorkId, Assert.Single((await fixture.Api.GetLibraryAsync()).Games).WorkId);
        Assert.True((await fixture.SetHeader(epic, null)).Changed);
        var reset = await fixture.Review();
        Assert.Null(reset.Workspace.PreferredHeaderStores[epic.WorkId]);
        Assert.Equal(joined.History, reset.History);

        Assert.NotNull(act.ActId);
        await fixture.Api.SendAsync<IdentityReviewUndoRequest, IdentityReviewMutation>(HttpMethod.Post,
            "identity/review/undo", new(reset.Revision, [act.ActId.Value], [], []));
        var undone = await fixture.Review();
        Assert.Equal("gog", undone.Workspace.PreferredHeaderStores[steam.WorkId]);
        Assert.Equal(steam.WorkId, Assert.Single(undone.Workspace.Buckets, row => row.WorkId == gog.WorkId).ResolvedWorkId);
        Assert.Equal(epic.WorkId, Assert.Single(undone.Workspace.Buckets, row => row.WorkId == epic.WorkId).ResolvedWorkId);
        Assert.Equal(new[] { steam.WorkId, epic.WorkId }.Order(),
            (await fixture.Api.GetLibraryAsync()).Games.Select(game => game.WorkId).Order());
        Assert.Contains(undone.History, link => link.ParentWorkId == steam.WorkId && link.ChildWorkId == gog.WorkId);
    }

    [Fact]
    public async Task Unavailable_header_choice_is_retained_refused_and_restored_when_ownership_returns_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var (steam, gog) = await fixture.SeedHeaderGroup();
        await fixture.SetHeader(steam, "gog");
        using (var lease = fixture.Services.GetRequiredService<ISqliteConnectionFactory>().Lease())
            await lease.Connection.ExecuteAsync("DELETE FROM ownerships WHERE release_id = @release",
                new { release = gog.ReleaseId }, lease.Transaction);
        var unavailable = await fixture.Review();
        Assert.Equal("gog", unavailable.Workspace.PreferredHeaderStores[steam.WorkId]);
        Assert.DoesNotContain(unavailable.Workspace.Ownerships, ownership => ownership.ReleaseId == gog.ReleaseId);
        var game = Assert.Single((await fixture.Api.GetLibraryAsync()).Games);
        Assert.Equal("Steam title", game.Title);
        Assert.Equal(steam.WorkId, Assert.Single(game.Entries).WorkId);
        Assert.False((await fixture.SetHeader(steam, "gog")).Changed);
        Assert.Equal("gog", (await fixture.Review()).Workspace.PreferredHeaderStores[steam.WorkId]);

        await fixture.RestoreOwnership(gog);
        var restored = await fixture.Review();
        Assert.Equal("gog", restored.Workspace.PreferredHeaderStores[steam.WorkId]);
        Assert.Contains(restored.Workspace.Ownerships, ownership => ownership.ReleaseId == gog.ReleaseId);
        Assert.Equal(2, Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Entries.Count);
        Assert.Equal(unavailable.History, restored.History);
        Assert.True((await fixture.SetHeader(steam, null)).Changed);
        Assert.Null((await fixture.Review()).Workspace.PreferredHeaderStores[steam.WorkId]);
    }

    [Fact]
    public async Task Latest_group_header_wins_after_join_and_pending_platform_preference_does_not_replace_it_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var (steam, _) = await fixture.SeedHeaderGroup();
        var epic = await fixture.SeedHeader("Epic title", "epic");
        var manual = await fixture.SeedHeader("Manual title", "manual");
        await fixture.Link(epic, manual);
        await fixture.SetHeader(steam, "gog");
        await fixture.SetHeader(epic, "manual");
        await fixture.Link(steam, epic);
        var joined = await fixture.Review();
        Assert.Equal("manual", joined.Workspace.PreferredHeaderStores[steam.WorkId]);
        Assert.Equal(4, Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Entries.Count);

        await fixture.Api.SendAsync(HttpMethod.Put, "preferences/presentation/PreferredMergePlatform",
            new SetPresentationPreference("steam"));
        var preferences = await fixture.Api.GetAsync<PresentationPreferenceValue[]>("preferences/presentation");
        Assert.Equal("steam", Assert.Single(preferences,
            preference => preference.Preference == PresentationPreference.PreferredMergePlatform).Value);
        var after = await fixture.Review();
        Assert.Equal("manual", after.Workspace.PreferredHeaderStores[steam.WorkId]);
        Assert.Equal(joined.History, after.History);
        Assert.Equal(RecordFacts(joined.Workspace), RecordFacts(after.Workspace));
        Assert.Equal("Manual title", Assert.Single(after.Workspace.Works, work => work.Id == manual.WorkId).Name);
    }

    [Fact]
    public async Task Expansion_children_single_games_and_absent_stores_refuse_header_preferences_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var (steam, _) = await fixture.SeedHeaderGroup();
        var expansion = await fixture.SeedHeader("Expansion", "steam");
        await fixture.Link(steam, expansion, IdentityLinkKinds.ExpansionOf);
        var single = await fixture.SeedHeader("Single", "gog");
        var before = await fixture.Review();
        Assert.False((await fixture.SetHeader(expansion, "steam")).Changed);
        Assert.False((await fixture.SetHeader(single, "gog")).Changed);
        Assert.False((await fixture.SetHeader(steam, "missing")).Changed);
        var after = await fixture.Review();
        Assert.Empty(after.Workspace.PreferredHeaderStores);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.History, after.History);
        Assert.Equal(RecordFacts(before.Workspace), RecordFacts(after.Workspace));
    }

    [Fact]
    public async Task User_name_reaches_the_library_and_survives_automatic_metadata_refresh_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var entry = await fixture.SeedName("Automatic Name", "Storefront Title");
        await fixture.Rename(entry, "My Own Name");
        Assert.Equal("My Own Name", Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Title);
        Assert.False(await fixture.Services.GetRequiredService<IWorkRepository>()
            .ApplyEnrichmentAsync(new(entry.WorkId, Name: "Refetched Automatic Name")));
        using var reopened = fixture.ReopenClient();
        Assert.Equal("My Own Name", Assert.Single((await reopened.GetLibraryAsync()).Games).Title);
        Assert.Equal("Storefront Title", Assert.Single((await reopened.GetWorkspaceAsync()).Releases).Name);
        Assert.Equal(FieldSources.User, (await fixture.Sources(entry))[WorkFields.Name]);
    }

    [Fact]
    public async Task User_name_reaches_details_and_clears_the_provisional_flag_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var entry = await fixture.SeedName("Automatic Name", "Storefront Title");
        await fixture.Rename(entry, "My Own Name");
        Assert.Equal("My Own Name", (await fixture.Api.GetGameAsync(entry.WorkId)).Title);
        var metadata = await fixture.Metadata(entry);
        Assert.Equal("My Own Name", metadata.Title);
        Assert.Equal("My Own Name", Assert.Single(metadata.Fields, field => field.Field == WorkFields.Name).Value);
        Assert.False(Assert.Single((await fixture.Api.GetWorkspaceAsync()).Works).NameIsProvisional);
    }

    [Fact]
    public async Task User_name_orders_the_library_while_preserving_the_distinct_storefront_title_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var entry = await fixture.SeedName("Automatic Name", "Storefront Title");
        await fixture.SeedName("Almanac", "Almanac");
        await fixture.Rename(entry, "Zenith");
        Assert.Equal(new[] { "Almanac", "Zenith" }, (await fixture.Api.GetLibraryAsync()).Games.Select(game => game.Title));
        var workspace = await fixture.Api.GetWorkspaceAsync();
        Assert.Equal("Zenith", Assert.Single(workspace.Works, work => work.Id == entry.WorkId).Name);
        Assert.Equal("Storefront Title", Assert.Single(workspace.Releases, release => release.Id == entry.ReleaseId).Name);
    }

    [Fact]
    public async Task User_name_reaches_the_real_soft_match_review_without_rewriting_candidate_evidence_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var left = await fixture.SeedName("Prey", "Prey");
        var right = await fixture.SeedName("Prey", "Prey", "gog");
        await fixture.QueuePair(left, right);
        var before = await fixture.Review();
        Assert.Single(before.Candidates);
        await fixture.Rename(left, "Prey (2017)");
        var after = await fixture.Review();
        Assert.Equal("Prey (2017)", Assert.Single(after.Workspace.Works, work => work.Id == left.WorkId).Name);
        Assert.Equal("Prey", Assert.Single(after.Workspace.Works, work => work.Id == right.WorkId).Name);
        Assert.Equal(before.Candidates, after.Candidates);
        Assert.Empty(after.History);
        Assert.Equal(new[] { left.ReleaseId, right.ReleaseId }, after.Workspace.Releases.Select(release => release.Id));
    }

    [Fact]
    public async Task Unedited_name_keeps_the_automatic_title_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var entry = await fixture.SeedName("Automatic Name", "Storefront Title");
        Assert.Equal("Automatic Name", Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Title);
        Assert.Equal("Automatic Name", (await fixture.Metadata(entry)).Title);
        Assert.Equal("Storefront Title", Assert.Single((await fixture.Api.GetWorkspaceAsync()).Releases).Name);
        Assert.Empty(await fixture.Sources(entry));
    }

    [Fact]
    public async Task Reset_name_retains_text_as_provisional_until_automatic_enrichment_promotes_it_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        var entry = await fixture.SeedName("Automatic Name", "Storefront Title");
        await fixture.Rename(entry, "My Own Name");
        var metadata = await fixture.Metadata(entry);
        var outcome = await fixture.Api.SendAsync<ResetMetadataRequest, MutationOutcome>(HttpMethod.Post,
            $"games/{entry.WorkId}/metadata/reset", new(WorkFields.Name, metadata.Revision));
        Assert.Equal("Applied", outcome.Outcome);
        Assert.Equal("My Own Name", Assert.Single((await fixture.Api.GetLibraryAsync()).Games).Title);
        Assert.True(Assert.Single((await fixture.Api.GetWorkspaceAsync()).Works).NameIsProvisional);
        Assert.Empty(await fixture.Sources(entry));

        Assert.True(await fixture.Services.GetRequiredService<IWorkRepository>()
            .ApplyEnrichmentAsync(new(entry.WorkId, Name: "Promoted Automatic Name")));
        Assert.Equal("Promoted Automatic Name", (await fixture.Api.GetGameAsync(entry.WorkId)).Title);
        Assert.False(Assert.Single((await fixture.Api.GetWorkspaceAsync()).Works).NameIsProvisional);
    }

    private static string RecordFacts(LibraryWorkspaceResponse workspace) => JsonSerializer.Serialize(new
    {
        workspace.Works, workspace.Releases, workspace.Ownerships, workspace.ExternalIds,
    });

    private sealed record Entry(long WorkId, long ReleaseId, string Title, string Store);

    private sealed class Fixture(WebApplication app, string directory) : IAsyncDisposable
    {
        private int appId = 700_000;
        public IServiceProvider Services => app.Services;
        public WinnowApiClient Api { get; } = WinnowApiClient.Attach(directory);
        public WinnowApiClient ReopenClient() => WinnowApiClient.Attach(directory);

        public static async Task<Fixture> Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-name-header-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            try
            {
                await app.StartAsync();
                return new(app, directory);
            }
            catch
            {
                await app.DisposeAsync();
                Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        public async Task<(Entry Steam, Entry Gog)> SeedHeaderGroup()
        {
            var steam = await SeedHeader("Steam title", "steam");
            var gog = await SeedHeader("GOG title", "gog");
            await Link(steam, gog);
            return (steam, gog);
        }

        public async Task<Entry> SeedHeader(string title, string store)
        {
            var work = await Services.GetRequiredService<IWorkRepository>()
                .InsertAsync(new Work { Name = title, FirstReleaseYear = 2011 });
            var release = await Services.GetRequiredService<IReleaseRepository>()
                .InsertAsync(new Release { WorkId = work, Name = title });
            var entry = new Entry(work, release, title, store);
            await RestoreOwnership(entry);
            return entry;
        }

        public async Task RestoreOwnership(Entry entry)
            => await Services.GetRequiredService<IOwnershipRepository>().UpsertAsync(
                new(entry.ReleaseId, entry.Store, null, null, null, null));

        public async Task<Entry> SeedName(string workName, string releaseName, string store = "steam")
        {
            var work = await Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = workName });
            var releases = Services.GetRequiredService<IReleaseRepository>();
            var release = await releases.InsertAsync(new Release { WorkId = work, Name = releaseName, Platform = "windows" });
            await releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = release, Provider = store == "gog" ? ExternalIdProviders.Gog : ExternalIdProviders.Steam,
                ProviderId = (++appId).ToString(CultureInfo.InvariantCulture),
            });
            var ownership = await Services.GetRequiredService<IOwnershipRepository>()
                .InsertAsync(new Ownership { ReleaseId = release, Store = store });
            await Services.GetRequiredService<IPlayRecordRepository>().InsertAsync(new PlayRecord
            {
                OwnershipId = ownership, PlaytimeMinutes = 0, LastPlayedAt = null,
                Source = "steam_localconfig", ObservedAt = Now,
            });
            return new(work, release, releaseName, store);
        }

        public async Task QueuePair(Entry left, Entry right)
        {
            var score = new SoftMatcher().Score(
                new() { ReleaseId = left.ReleaseId, Title = left.Title },
                new() { ReleaseId = right.ReleaseId, Title = right.Title });
            Assert.True(score.ShouldQueue);
            await Services.GetRequiredService<IMergeCandidateRepository>().InsertAsync(new MergeCandidate
            {
                LeftReleaseId = left.ReleaseId, RightReleaseId = right.ReleaseId, Score = score.Score,
                SignalsJson = SoftMatchSignalsJson.Serialize(score), Status = MergeCandidateStatuses.Pending,
            });
        }

        public Task<IdentityReviewResponse> Review() => Api.GetAsync<IdentityReviewResponse>("identity/review");
        public Task<MetadataResponse> Metadata(Entry entry) => Api.GetAsync<MetadataResponse>($"games/{entry.WorkId}/metadata");
        public Task<IReadOnlyDictionary<string, string>> Sources(Entry entry)
            => Services.GetRequiredService<IWorkFieldSourceRepository>().GetSourcesAsync(entry.WorkId);

        public async Task Rename(Entry entry, string name)
        {
            var metadata = await Metadata(entry);
            var outcome = await Api.SendAsync<EditMetadataRequest, MutationOutcome>(HttpMethod.Put,
                $"games/{entry.WorkId}/metadata", new(WorkFields.Name, name, metadata.Revision));
            Assert.Equal("Applied", outcome.Outcome);
        }

        public async Task<IdentityReviewMutation> SetHeader(Entry entry, string? store)
            => await Api.SendAsync<IdentityReviewHeaderRequest, IdentityReviewMutation>(HttpMethod.Put,
                "identity/review/header", new((await Review()).Revision, entry.WorkId, store));

        public async Task<IdentityReviewMutation> Link(Entry parent, Entry child, string kind = IdentityLinkKinds.SameGame)
            => await Api.SendAsync<IdentityReviewLinkRequest, IdentityReviewMutation>(HttpMethod.Post,
                "identity/review/link", new((await Review()).Revision, parent.WorkId, [child.WorkId], kind, null, [], []));

        public async ValueTask DisposeAsync()
        {
            Api.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
