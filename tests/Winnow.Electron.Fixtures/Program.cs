using Winnow.App.Services;
using Winnow.Backend;
using Winnow.Electron.Fixtures;

var directoryIndex = Array.IndexOf(args, "--data-dir");
if (directoryIndex < 0 || directoryIndex + 1 >= args.Length)
    throw new ArgumentException("Pass --data-dir <throwaway directory> to this test fixture.");
var directory = Path.GetFullPath(args[directoryIndex + 1]);
if (!Path.GetFileName(directory).StartsWith("winnow-electron-ownership-", StringComparison.Ordinal)
    || File.Exists(Path.Combine(directory, "winnow.db")))
    throw new ArgumentException("Ownership composition fixtures require a new test-owned directory.");

await using var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
{
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
await app.RunAsync();

internal sealed record StartRefresh(bool Fail);
