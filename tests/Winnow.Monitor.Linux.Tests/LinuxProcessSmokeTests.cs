using System.Diagnostics;
using Microsoft.Extensions.Options;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Monitor;
using Xunit;

namespace Winnow.Monitor.Linux.Tests;

/// <summary>Marks a real /proc test as explicitly skipped outside Linux.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "This smoke test requires Linux /proc.";
        }
    }
}

/// <summary>Runs only in the Ubuntu CI job, against a real Linux /proc process.</summary>
public sealed class LinuxProcessSmokeTests
{
    [LinuxFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task A_native_linux_game_inside_its_install_root_is_discovered_and_recorded()
    {
        await RunAsync(startFromInstallRoot: true, compatibilityDataPath: null);
    }

    [LinuxFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task A_synthetic_Proton_environment_is_discovered_attributed_and_recorded()
    {
        await RunAsync(
            startFromInstallRoot: false,
            compatibilityDataPath: "/tmp/steam/steamapps/compatdata/480");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static async Task RunAsync(bool startFromInstallRoot, string? compatibilityDataPath)
    {

        const long ownershipId = 17;
        const long releaseId = 23;
        const string appId = "480";
        var root = Path.Combine(Path.GetTempPath(), "winnow-linux-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            // A copied native binary gives the executable builder real Unix mode
            // bits to inspect. The Proton case runs /bin/sleep outside this root,
            // so only STEAM_COMPAT_DATA_PATH can attribute it.
            var gameBinary = Path.Combine(root, "very-long-linux-game.x86_64");
            File.Copy("/bin/sleep", gameBinary);
            File.SetUnixFileMode(gameBinary, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var ownerships = new SmokeOwnerships(new Ownership
            {
                Id = ownershipId,
                ReleaseId = releaseId,
                Store = ExternalIdProviders.Steam,
                InstallPath = root,
                Installed = true,
            });
            var releases = new SmokeReleases(releaseId, appId);
            var sessions = new SmokeSessions();
            var options = Options.Create(new SessionWatcherOptions
            {
                MinimumSessionDuration = TimeSpan.Zero,
                RelaunchGrace = TimeSpan.Zero,
            });
            var builder = new GameExecutableIndexBuilder(ownerships, releases, options);
            var watcher = new SessionWatcher(new SystemProcessSource(), builder, sessions, options);

            try
            {
                var start = new ProcessStartInfo(startFromInstallRoot ? gameBinary : "/bin/sleep", "30")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                };
                if (compatibilityDataPath is not null)
                {
                    start.Environment["STEAM_COMPAT_DATA_PATH"] = compatibilityDataPath;
                }
                using var process = Process.Start(start)!;

                await WaitForDiscoveryAsync(watcher);
                process.Kill();
                await process.WaitForExitAsync();
                await watcher.TickAsync();

                var session = Assert.Single(sessions.Items);
                Assert.Equal(ownershipId, session.OwnershipId);
                Assert.Equal(SessionAttributions.Inferred, session.AttributedBy);
            }
            finally
            {
                watcher.Dispose();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WaitForDiscoveryAsync(SessionWatcher watcher)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if ((await watcher.TickAsync()).Started == 1)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("The real process was not discovered within two seconds.");
    }

    private sealed class SmokeOwnerships(Ownership ownership) : IOwnershipRepository
    {
        public Task<IReadOnlyList<Ownership>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Ownership>>([ownership]);
        public Task<long> InsertAsync(Ownership value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Ownership?> GetAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Ownership>> GetByReleaseAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> UpsertAsync(OwnershipUpsert value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> FillAcquisitionFactsAsync(OwnershipAcquisitionFill value, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class SmokeReleases(long releaseId, string appId) : IReleaseRepository
    {
        public Task<IReadOnlyList<ReleaseIdentity>> GetIdentitiesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ReleaseIdentity>>([new ReleaseIdentity
            {
                ReleaseId = releaseId, WorkId = 1, ReleaseName = "Smoke", WorkName = "Smoke", SteamAppId = appId,
            }]);
        public Task<long> InsertAsync(Release value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateNameAsync(long id, string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Release?> GetAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Release>> GetByWorkAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddExternalIdAsync(ExternalId value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExternalId>> GetExternalIdsAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Release?> FindByExternalIdAsync(string provider, string id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class SmokeSessions : ISessionRepository
    {
        public List<Session> Items { get; } = [];
        public Task<long> InsertAsync(Session session, CancellationToken ct = default)
        {
            Items.Add(session);
            return Task.FromResult((long)Items.Count);
        }
        public Task<Session?> GetAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Session>> GetByOwnershipAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetNoteAsync(SessionNote note, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionNote?> GetNoteAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionJournalEntry>> GetJournalEntriesByOwnershipAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteNoteAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
