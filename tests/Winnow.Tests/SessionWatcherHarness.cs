using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Monitor;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Winnow.Tests;

/// <summary>
/// A whole §5.2 watcher on a temp-file database, a fake clock and a scripted
/// process source. Install directories are real directories with real (empty)
/// <c>.exe</c> files in them, so the executable index is genuinely built by
/// walking a filesystem rather than being handed a list — which is what keeps
/// the deny-list, the depth limit and the subtree pruning under test.
///
/// <para>Nothing here sleeps and nothing enumerates a real process.</para>
/// </summary>
public sealed class SessionWatcherHarness : IDisposable
{
    public static readonly DateTime Origin = new(2026, 8, 26, 18, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();
    private readonly string _root;
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;

    public SessionWatcherHarness(Action<SessionWatcherOptions>? configure = null)
    {
        _root = Path.Combine(Path.GetTempPath(), $"winnow-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        Sessions = new SessionRepository(_db.Factory);
        Settings = new SettingsRepository(_db.Factory);
        SessionWrites = new FlakySessionRepository(Sessions);

        Clock = new FakeTimeProvider(Origin);
        Processes = new ScriptedProcessSource();

        var options = new SessionWatcherOptions();
        configure?.Invoke(options);
        var wrapped = Options.Create(options);
        IndexBuilder = new GameExecutableIndexBuilder(_ownerships, wrapped, Clock);

        // M3b: the registry the UI declares launches on. Real rather than
        // faked — it is a few fields and a lock, and the thing worth testing is
        // the watcher and the registry agreeing, not either alone.
        Intents = new LaunchIntents(wrapped);
        Watcher = new SessionWatcher(
            Processes, IndexBuilder, SessionWrites, wrapped, Clock, logger: null, intents: Intents);
    }

    public FakeTimeProvider Clock { get; }

    public ScriptedProcessSource Processes { get; }

    public GameExecutableIndexBuilder IndexBuilder { get; }

    /// <summary>The launch-intent registry the watcher consults (M3b).</summary>
    public LaunchIntents Intents { get; }

    /// <summary>Declares a Winnow launch at the harness clock's current time.</summary>
    public bool Declare(long ownershipId) => Intents.Declare(ownershipId, Clock.GetUtcNow().UtcDateTime);

    public SessionWatcher Watcher { get; }

    public SessionRepository Sessions { get; }

    /// <summary>
    /// The same settings table the app reads its preferences from, so the
    /// journal prompt's opt-in can be exercised against a real row rather than a
    /// stub that always answers.
    /// </summary>
    public SettingsRepository Settings { get; }

    /// <summary>
    /// The repository the watcher writes through. Pass-through by default; a
    /// test sets <see cref="FlakySessionRepository.FailNextInserts"/> to reach
    /// the write-failure path.
    /// </summary>
    public FlakySessionRepository SessionWrites { get; }

    /// <summary>
    /// Creates an installed game: a work, a release, an ownership, an install
    /// directory, and the given executables inside it (paths relative to the
    /// install directory, using <c>/</c> as the separator).
    /// </summary>
    public async Task<InstalledGame> AddGameAsync(
        string name, params string[] relativeExecutables)
        => await AddGameAsync(name, installed: true, relativeExecutables);

    public async Task<InstalledGame> AddGameAsync(
        string name, bool installed, params string[] relativeExecutables)
    {
        var installPath = Path.Combine(_root, name);
        Directory.CreateDirectory(installPath);

        var executables = new List<string>();
        foreach (var relative in relativeExecutables)
        {
            var full = Path.Combine(installPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, []);
            executables.Add(full);
        }

        var workId = await _works.InsertAsync(new Work
        {
            Name = name,
            SortName = name.ToLowerInvariant(),
        });

        var releaseId = await _releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = name,
            Platform = "windows",
        });

        var ownershipId = await _ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = ExternalIdProviders.Steam,
            InstallPath = installPath,
            Installed = installed,
        });

        return new InstalledGame(ownershipId, installPath, executables);
    }

    /// <summary>A path under <see cref="_root"/> that belongs to no game.</summary>
    public string ElsewherePath(string relative)
    {
        var full = Path.Combine(_root, "elsewhere", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    /// <summary>Moves the clock and runs one watcher pass.</summary>
    public async Task<SessionWatcherTick> TickAtAsync(DateTime at)
    {
        Clock.SetUtcNow(at);
        return await Watcher.TickAsync();
    }

    public Task<IReadOnlyList<Session>> SessionsForAsync(long ownershipId)
        => Sessions.GetByOwnershipAsync(ownershipId);

    public void Dispose()
    {
        Watcher.Dispose();
        _db.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp tree is not a test failure.
        }
    }

    public sealed record InstalledGame(long OwnershipId, string InstallPath, IReadOnlyList<string> Executables)
    {
        /// <summary>Full path of the executable whose file name (with extension) matches.</summary>
        public string Exe(string fileName)
            => Executables.Single(e => string.Equals(
                Path.GetFileName(e), fileName, StringComparison.OrdinalIgnoreCase));
    }
}
