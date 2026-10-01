using Winnow.App.Services;
using Winnow.Backend;
using Winnow.Electron.Fixtures;

var directoryIndex = Array.IndexOf(args, "--data-dir");
if (directoryIndex < 0 || directoryIndex + 1 >= args.Length)
    throw new ArgumentException("Pass --data-dir <throwaway directory> to this test fixture.");
var directory = Path.GetFullPath(args[directoryIndex + 1]);
var visibility = Path.GetFileName(directory).StartsWith("winnow-electron-visibility-", StringComparison.Ordinal);
var marker = Path.Combine(directory, ".visibility-fixture");
if (visibility)
{
    if (File.Exists(Path.Combine(directory, "winnow.db")) && !File.Exists(marker))
        throw new ArgumentException("Only a previously created visibility fixture may be reopened.");
    Directory.CreateDirectory(directory);
    await File.WriteAllTextAsync(marker, "Winnow Electron visibility test fixture");
}
else if (!Path.GetFileName(directory).StartsWith("winnow-electron-ownership-", StringComparison.Ordinal)
    || File.Exists(Path.Combine(directory, "winnow.db")))
    throw new ArgumentException("Ownership composition fixtures require a new test-owned directory.");

await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
{
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

// BackendApplication's normal loopback, authority and bearer-token middleware covers these routes.
// Only this separately built test executable exposes fixture control; the production backend does not.
if (visibility)
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
