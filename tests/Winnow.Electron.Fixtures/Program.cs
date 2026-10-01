using Winnow.App.Services;
using Winnow.Backend;
using Winnow.Electron.Fixtures;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.Igdb;

var directoryIndex = Array.IndexOf(args, "--data-dir");
if (directoryIndex < 0 || directoryIndex + 1 >= args.Length)
    throw new ArgumentException("Pass --data-dir <throwaway directory> to this test fixture.");
var directory = Path.GetFullPath(args[directoryIndex + 1]);
var visibility = Path.GetFileName(directory).StartsWith("winnow-electron-visibility-", StringComparison.Ordinal);
var pluginActions = Path.GetFileName(directory).StartsWith("winnow-electron-plugin-actions-", StringComparison.Ordinal);
var editions = Path.GetFileName(directory).StartsWith("winnow-electron-editions-", StringComparison.Ordinal);
var merges = Path.GetFileName(directory).StartsWith("winnow-electron-merges-", StringComparison.Ordinal);
var matching = Path.GetFileName(directory).StartsWith("winnow-electron-matching-", StringComparison.Ordinal);
var metadataEditing = Path.GetFileName(directory).StartsWith("winnow-electron-metadata-editing-", StringComparison.Ordinal);
var detailsReading = Path.GetFileName(directory).StartsWith("winnow-electron-details-reading-", StringComparison.Ordinal);
var activityRemaining = Path.GetFileName(directory).StartsWith("winnow-electron-activity-remaining-", StringComparison.Ordinal);
var journalActivity = Path.GetFileName(directory).StartsWith("winnow-electron-journal-activity-", StringComparison.Ordinal);
var gameplayStats = Path.GetFileName(directory).StartsWith("winnow-electron-gameplay-stats-", StringComparison.Ordinal);
var recommendationState = Path.GetFileName(directory).StartsWith("winnow-electron-recommendation-state-", StringComparison.Ordinal);
var marker = Path.Combine(directory, ".visibility-fixture");
if (visibility)
{
    if (File.Exists(Path.Combine(directory, "winnow.db")) && !File.Exists(marker))
        throw new ArgumentException("Only a previously created visibility fixture may be reopened.");
    Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(marker, "Winnow Electron visibility test fixture");
}
else if (!(pluginActions || editions || merges || matching || metadataEditing || detailsReading || activityRemaining || journalActivity || gameplayStats || recommendationState || Path.GetFileName(directory).StartsWith("winnow-electron-ownership-", StringComparison.Ordinal))
    || File.Exists(Path.Combine(directory, "winnow.db")))
    throw new ArgumentException("Composition fixtures require a new test-owned directory.");

await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
{
    if (recommendationState)
    {
        RecommendationStateFixture.Register(services, Path.GetFileName(directory).Contains("-nofeedback-", StringComparison.Ordinal));
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, MatchingOfflineHttp>();
        return;
    }
    if (gameplayStats)
    {
        GameplayStatsFixture.Register(services);
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, MatchingOfflineHttp>();
        return;
    }
    if (journalActivity)
    {
        JournalActivityFixture.Register(services);
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, MatchingOfflineHttp>();
        return;
    }
    if (activityRemaining)
    {
        services.AddSingleton(new ActivityReadTrackingFactory(new Winnow.Data.SqliteConnectionFactory(
            Path.Combine(directory, "winnow.db"), pooling: false)));
        services.AddSingleton<Winnow.Data.ISqliteConnectionFactory>(provider => provider.GetRequiredService<ActivityReadTrackingFactory>());
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<ActivityRemainingFixture>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, MatchingOfflineHttp>();
        return;
    }
    if (detailsReading)
    {
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<DetailsReadingFixture>();
        services.AddSingleton<DetailsReadingIgdbClient>();
        services.AddSingleton<IIgdbClient>(provider => provider.GetRequiredService<DetailsReadingIgdbClient>());
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, MatchingOfflineHttp>();
        return;
    }
    if (metadataEditing)
    {
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<MetadataEditingFixture>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, MatchingOfflineHttp>();
        if (Path.GetFileName(directory).Contains("-noservice-", StringComparison.Ordinal))
            services.RemoveAll<IWorkMetadataEditService>();
        return;
    }
    if (matching)
    {
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<MatchingIgdbClient>();
        services.AddSingleton<IgdbMatchingFixture>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, MatchingOfflineHttp>();
        if (Path.GetFileName(directory).Contains("-pipeline-", StringComparison.Ordinal))
        {
            services.RemoveAll<IStoreArtifactAliasSource>();
            services.AddSingleton<IStoreArtifactAliasSource, MatchingAliases>();
            services.AddSingleton<IGameIdentityGraph, MatchingGraph>();
        }
        else services.AddSingleton<IIgdbClient>(provider => provider.GetRequiredService<MatchingIgdbClient>());
        if (Path.GetFileName(directory).Contains("-noservice-", StringComparison.Ordinal))
            services.RemoveAll<IIgdbAssignmentService>();
        if (Path.GetFileName(directory).Contains("-refused-", StringComparison.Ordinal))
            services.AddSingleton<IIdentityLinkRepository, MatchingRefusingLinks>();
        return;
    }
    if (merges)
    {
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<MergeReviewFixture>();
        return;
    }
    if (editions)
    {
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<EditionIdentityFixture>();
        return;
    }
    if (pluginActions)
    {
        services.AddSingleton<PluginActionShellGuard>();
        services.AddSingleton<IUriDispatcher>(provider => provider.GetRequiredService<PluginActionShellGuard>());
        services.AddSingleton<PluginActionsFixture>();
        return;
    }
    if (visibility)
    {
        services.AddSingleton<TimeProvider>(new VisibilityFixtureClock());
        services.AddSingleton<LibraryVisibilityFixture>();
        return;
    }
    services.AddSingleton<ScheduledOwnershipRefresh>();
    services.AddSingleton(provider => new LibraryRefreshPipeline(
    [
        new("metadata", provider.GetRequiredService<ScheduledOwnershipRefresh>().EnrichAsync),
        new("optional outage", provider.GetRequiredService<ScheduledOwnershipRefresh>().OptionalAsync),
    ], ct => provider.GetRequiredService<ScheduledOwnershipRefresh>().PublishAsync(
        provider.GetRequiredService<LibraryChangePublisher>(), ct),
        provider.GetRequiredService<ILogger<LibraryRefreshPipeline>>()));
    services.AddSingleton(provider => new OwnershipRefreshCoordinator(
        new FixtureRemoteOwnership(provider.GetRequiredService<ScheduledOwnershipRefresh>()),
        provider.GetRequiredService<LibraryRefreshPipeline>(),
        provider.GetRequiredService<ILogger<OwnershipRefreshCoordinator>>()));
});
await app.Services.GetRequiredService<FirstRunSetupService>().SaveAsync(null);
if (pluginActions)
    await app.Services.GetRequiredService<PluginActionsFixture>().InitializeAsync(directory);

// BackendApplication's normal loopback, authority and bearer-token middleware covers these routes.
// Only this separately built test executable exposes fixture control; the production backend does not.
if (recommendationState)
    RecommendationStateFixture.Map(app);
else if (gameplayStats)
{
    app.MapPost("/__fixture/gameplay-stats/seed", (GameplayFixtureSeed input, GameplayStatsFixture fixture) => fixture.SeedAsync(input.Kind, input.SecondStore));
    app.MapGet("/__fixture/gameplay-stats/state", (GameplayFixtureControls controls) => controls.Snapshot());
    app.MapPost("/__fixture/gameplay-stats/arm", (GameplayFixtureArm input, GameplayFixtureControls controls) => controls.Arm(input));
    app.MapPost("/__fixture/gameplay-stats/release", (GameplayFixtureRelease input, GameplayFixtureControls controls) =>
    {
        controls.Release(input.GateId, input.Seconds);
        return Results.NoContent();
    });
    app.MapPost("/__fixture/gameplay-stats/change", async (GameplayFixtureChange input, GameplayStatsFixture fixture) =>
    {
        await fixture.ChangeAsync(input.Change);
        return Results.NoContent();
    });
    app.MapPost("/__fixture/gameplay-stats/publish", async (GameplayStatsFixture fixture) =>
    {
        await fixture.PublishAsync();
        return Results.NoContent();
    });
}
else if (journalActivity)
{
    app.MapPost("/__fixture/journal-activity/seed", (JournalActivitySeed input, JournalActivityFixture fixture) => fixture.SeedAsync(input.Kind));
    app.MapGet("/__fixture/journal-activity/state", (JournalActivityFixture fixture) => fixture.Snapshot());
    app.MapPost("/__fixture/journal-activity/arm", (JournalActivityArm input, JournalActivityControls controls) => controls.Arm(input));
    app.MapPost("/__fixture/journal-activity/release", (JournalActivityRelease input, JournalActivityControls controls) =>
    {
        controls.Release(input.GateId);
        return Results.NoContent();
    });
    app.MapPost("/__fixture/journal-activity/recover-complete", async (JournalActivityFixture fixture) =>
    {
        await fixture.CompleteRecoveryAsync();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/journal-activity/reload-note", async (JournalActivityFixture fixture) =>
    {
        await fixture.ReloadNoteAsync();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/journal-activity/publish", async (JournalActivityFixture fixture) =>
    {
        await fixture.PublishAsync();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/journal-activity/publish-ended", (JournalActivityFixture fixture) =>
    {
        fixture.PublishEnded();
        return Results.NoContent();
    });
}
else if (activityRemaining)
{
    app.MapPost("/__fixture/activity-remaining/seed", (ActivityRemainingSeed input, ActivityRemainingFixture fixture)
        => fixture.SeedAsync(input.Kind));
    app.MapGet("/__fixture/activity-remaining/state", (ActivityRemainingFixture fixture) => fixture.Snapshot());
    app.MapPost("/__fixture/activity-remaining/measure-start", (ActivityMeasurementStart input, ActivityReadTrackingFactory tracking) =>
    {
        tracking.Start(input.Name);
        return Results.NoContent();
    });
    app.MapPost("/__fixture/activity-remaining/measure-end", (ActivityReadTrackingFactory tracking) => tracking.End());
}
else if (detailsReading)
{
    app.MapPost("/__fixture/details-reading/seed", (DetailsReadingSeed input, DetailsReadingFixture fixture)
        => fixture.SeedAsync(input.Kind, input.State));
    app.MapGet("/__fixture/details-reading/state", (DetailsReadingFixture fixture) => fixture.Snapshot());
}
else if (metadataEditing)
{
    app.MapPost("/__fixture/metadata-editing/seed", (MetadataEditingSeed input, MetadataEditingFixture fixture)
        => fixture.SeedAsync(input.Kind));
    app.MapPost("/__fixture/metadata-editing/background", async (MetadataEditingFixture fixture) =>
    {
        await fixture.BackgroundAsync();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/metadata-editing/patch", async (MetadataEditingFixture fixture) =>
    {
        await fixture.NewestPatchAsync();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/metadata-editing/publish", async (MetadataEditingFixture fixture) =>
    {
        await fixture.PublishAsync();
        return Results.NoContent();
    });
    app.MapGet("/__fixture/metadata-editing/state", (MetadataEditingFixture fixture) => fixture.Snapshot());
}
else if (matching)
{
    app.MapPost("/__fixture/matching/seed", (MatchingSeed input, IgdbMatchingFixture fixture) => fixture.SeedAsync(input.Kind));
    app.MapPost("/__fixture/matching/publish", async (IgdbMatchingFixture fixture) =>
    {
        await fixture.PublishAsync();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/matching/pipeline", async (LibraryRefreshPipeline pipeline, IRemoteOwnershipSync remote) =>
    {
        if (remote is not OwnershipRefreshCoordinator) throw new InvalidOperationException("Production ownership coordinator is required.");
        await pipeline.RunAsync();
        return Results.NoContent();
    });
    app.MapGet("/__fixture/matching/state", (IgdbMatchingFixture fixture) => fixture.Snapshot());
}
else if (merges)
{
    app.MapPost("/__fixture/merges/seed-pair", (MergePairSeed input, MergeReviewFixture fixture)
        => fixture.SeedPairAsync(input));
    app.MapPost("/__fixture/merges/publish", async (MergeReviewFixture fixture) =>
    {
        await fixture.PublishAsync();
        return Results.NoContent();
    });
}
else if (editions)
{
    app.MapPost("/__fixture/edition/seed", async (EditionIdentityFixture fixture) =>
    {
        await fixture.SeedAsync();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/edition/sync", (EditionIdentityFixture fixture) => fixture.SyncAsync());
    app.MapGet("/__fixture/edition/state", (EditionIdentityFixture fixture) => fixture.SnapshotAsync());
}
else if (pluginActions)
{
    app.MapPost("/__fixture/plugin-actions/sync", async (PluginObservation request, PluginActionsFixture fixture) =>
    {
        await fixture.SyncAsync(request.State);
        return Results.NoContent();
    });
    app.MapGet("/__fixture/plugin-actions/state", (PluginActionsFixture fixture) => fixture.SnapshotAsync());
    app.MapPost("/__fixture/plugin-actions/seed-steam", async (PluginActionsFixture fixture) =>
    {
        await fixture.SeedSteamAsync();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/plugin-actions/validate-captured", (CapturedActionCheck request, PluginActionsFixture fixture)
        => fixture.ValidateCapturedAsync(request.AfterRemoval));
    app.MapPost("/__fixture/plugin-actions/unload", async (PluginActionsFixture fixture) =>
    {
        await fixture.UnloadAsync();
        return Results.NoContent();
    });
}
else if (visibility)
{
    app.MapPost("/__fixture/visibility/seed", async (VisibilitySeed request, LibraryVisibilityFixture fixture) =>
    {
        await fixture.SeedAsync(request.Kind);
        return Results.NoContent();
    });
    app.MapPost("/__fixture/visibility/fail-exemptions", (LibraryVisibilityFixture fixture) =>
    {
        fixture.FailExemptions();
        return Results.NoContent();
    });
    app.MapPost("/__fixture/visibility/later-evidence", async (LibraryVisibilityFixture fixture) =>
    {
        await fixture.LaterEvidenceAsync();
        return Results.NoContent();
    });
    app.MapGet("/__fixture/visibility/state", (LibraryVisibilityFixture fixture) => fixture.Snapshot());
}
else
{
    app.MapPost("/__fixture/ownership/start", async (StartRefresh request, ScheduledOwnershipRefresh fixture,
        IRemoteOwnershipSync remote, ILogger<RemoteOwnershipSchedulerService> logger) =>
    {
        if (remote is not OwnershipRefreshCoordinator)
            throw new InvalidOperationException("The scheduler must use the production ownership coordinator.");
        await fixture.StartAsync(remote, request.Fail, logger);
        return Results.Accepted();
    });
    app.MapPost("/__fixture/ownership/release-metadata", (ScheduledOwnershipRefresh fixture) =>
    {
        fixture.ReleaseMetadata();
        return Results.Accepted();
    });
    app.MapGet("/__fixture/ownership/state", (ScheduledOwnershipRefresh fixture) => fixture.Snapshot());
}
await app.RunAsync();

internal sealed record StartRefresh(bool Fail);
internal sealed record VisibilitySeed(string Kind);
internal sealed record PluginObservation(string State);
internal sealed record CapturedActionCheck(bool AfterRemoval);
internal sealed record MatchingSeed(string Kind);
internal sealed record MetadataEditingSeed(string Kind);
internal sealed record DetailsReadingSeed(string Kind, int State = 0);
internal sealed record ActivityRemainingSeed(string Kind);
internal sealed record ActivityMeasurementStart(string Name);
internal sealed record JournalActivitySeed(string Kind);
internal sealed record JournalActivityRelease(string? GateId = null);
internal sealed record GameplayFixtureSeed(string Kind, string SecondStore = "gog");
internal sealed record GameplayFixtureRelease(string? GateId = null, double? Seconds = null);
internal sealed record GameplayFixtureChange(string Change);
