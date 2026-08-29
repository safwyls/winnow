using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Winnow.Monitor;

/// <summary>
/// What one <see cref="SessionWatcher.TickAsync"/> did. Returned for tests and
/// for the one log line a busy tick is allowed to emit.
/// </summary>
/// <param name="Running">Processes being tracked when the tick finished.</param>
/// <param name="Started">Game processes newly attached this tick.</param>
/// <param name="Recorded">Sessions written to the database this tick.</param>
/// <param name="Debounced">Sessions dropped for falling under the debounce floor.</param>
/// <param name="Queued">
/// Sessions finalised but not yet accepted by the database, still waiting to be
/// retried. Non-zero means writes are failing, not that work is pending.
/// </param>
public readonly record struct SessionWatcherTick(
    int Running, int Started, int Recorded, int Debounced, int Queued);

/// <summary>
/// Process-watching session detector. Sessions belong to ownerships, not processes --
/// all processes under one install directory are grouped into a single session.
/// Discovery is polled (Tier 1), exit is event-driven (Tier 2). Exit callbacks
/// record timestamps under a lock; all DB writes happen on the tick caller's thread.
/// </summary>
public sealed class SessionWatcher : IDisposable
{
    private readonly IProcessSource _source;
    private readonly GameExecutableIndexBuilder _indexBuilder;
    private readonly ISessionRepository _sessions;
    private readonly SessionWatcherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SessionWatcher> _logger;

    /// <summary>Upper bound on <see cref="_pending"/>. See <see cref="Enqueue"/>.</summary>
    private const int MaxPendingSessions = 4096;

    private readonly Lock _gate = new();

    /// <summary>Live game processes by pid. A pid here cannot be reused — the handle is held.</summary>
    private readonly Dictionary<int, TrackedGame> _tracked = [];

    /// <summary>Open sessions by ownership id. At most one per game at a time.</summary>
    private readonly Dictionary<long, LiveSession> _live = [];

    /// <summary>Pids that passed the name filter but resolved outside all install directories (negative cache).</summary>
    private readonly Dictionary<int, string> _rejected = [];

    /// <summary>Exited processes whose handles are released on the next tick (not inside the callback).</summary>
    private readonly List<ITrackedProcess> _closing = [];

    // Sessions stay queued until the DB accepts them, so a failed insert does
    // not lose the session -- the next tick or FlushAsync retries.
    private readonly List<Session> _pending = [];

    /// <summary>M3b attribution: launches Winnow fired itself. Consulted only after inference fails.</summary>
    private readonly LaunchIntents _intents;

    private GameExecutableIndex _index = GameExecutableIndex.Empty;
    private DateTime _indexBuiltAtUtc = DateTime.MinValue;
    private long _discoveryPass;
    private bool _disposed;

    public SessionWatcher(
        IProcessSource source,
        GameExecutableIndexBuilder indexBuilder,
        ISessionRepository sessions,
        IOptions<SessionWatcherOptions> options,
        TimeProvider? timeProvider = null,
        ILogger<SessionWatcher>? logger = null,
        LaunchIntents? intents = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _source = source;
        _indexBuilder = indexBuilder;
        _sessions = sessions;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SessionWatcher>.Instance;

        // Optional, and a private empty registry when absent, so a host that
        // never wired the UI still watches exactly as M3a did. Nothing declares
        // against a registry nobody can reach, so every attribution stays
        // inferred and no code path below has to test for null.
        _intents = intents ?? new LaunchIntents(options);
    }

    /// <summary>Raised after a session is written. Handlers are invoked defensively (a throw is logged and skipped).</summary>
    public event EventHandler<Session>? SessionRecorded;

    /// <summary>The executable index as of the last rebuild. Exposed for diagnostics and tests.</summary>
    public GameExecutableIndex Index => _index;

    /// <summary>One pass: refresh index, discover new game processes, write completed sessions. Not thread-safe for concurrent callers.</summary>
    public async Task<SessionWatcherTick> TickAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await EnsureIndexAsync(now, ct).ConfigureAwait(false);
        await DescribeIntentsAsync(now, ct).ConfigureAwait(false);

        var started = Discover(now);
        var debounced = Collect(now);
        _intents.Sweep(now);
        var recorded = await DrainPendingAsync(ct).ConfigureAwait(false);

        int running, queued;
        lock (_gate)
        {
            running = _tracked.Count;
            queued = _pending.Count;
        }

        return new SessionWatcherTick(running, started, recorded, debounced, queued);
    }

    /// <summary>Drains the pending session queue to the database, oldest first. Cancellation stops the drain without throwing.</summary>
    private async Task<int> DrainPendingAsync(CancellationToken ct)
    {
        var recorded = 0;

        while (true)
        {
            Session session;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    break;
                }

                session = _pending[0];
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            long id;
            try
            {
                id = await _sessions.InsertAsync(session, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Left queued deliberately. Logged at Warning because a session
                // that cannot be written is the one failure in this module a
                // user would actually care about.
                _logger.LogWarning(
                    ex,
                    "Could not write a session for ownership {OwnershipId}; {Queued} session(s) "
                    + "are queued and will be retried.",
                    session.OwnershipId, PendingCount);
                break;
            }

            lock (_gate)
            {
                // Index 0 rather than Remove(session): Session is a record, so
                // Remove matches by value and two identical sessions (a game run
                // twice for the same duration) would collapse into one removal.
                if (_pending.Count > 0 && ReferenceEquals(_pending[0], session))
                {
                    _pending.RemoveAt(0);
                }
            }

            recorded++;
            _logger.LogInformation(
                "Recorded a {Duration:n0}s {Attribution} session for ownership {OwnershipId} "
                + "({Start:u} → {End:u}).",
                session.DurationSeconds ?? 0,
                session.AttributedBy ?? "unattributed",
                session.OwnershipId,
                session.StartedAt,
                session.EndedAt);

            Announce(session with { Id = id });
        }

        return recorded;
    }

    /// <summary>Raises <see cref="SessionRecorded"/>; a throwing handler is logged and skipped.</summary>
    private void Announce(Session session)
    {
        try
        {
            SessionRecorded?.Invoke(this, session);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "A session-recorded handler threw for session {SessionId}; the session is written "
                + "and the drain continues.",
                session.Id);
        }
    }

    /// <summary>Describes newly declared launches with install root and executable names from a deep scan.</summary>
    private async Task DescribeIntentsAsync(DateTime now, CancellationToken ct)
    {
        var awaiting = _intents.AwaitingDescription(now);
        if (awaiting.Count == 0)
        {
            return;
        }

        var index = _index;
        foreach (var ownershipId in awaiting)
        {
            var root = index.RootFor(ownershipId);
            IReadOnlySet<string> names;
            try
            {
                names = await _indexBuilder.ScanLaunchNamesAsync(root, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Could not scan {Path} for the launch of ownership {OwnershipId}.",
                    root ?? "<no install path>", ownershipId);
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            _intents.Describe(ownershipId, root, names);

            _logger.LogDebug(
                "Launch of ownership {OwnershipId} is watching {Count} executable name(s) under {Path}.",
                ownershipId, names.Count, root ?? "<no install path>");
        }

        // A process resolved and rejected minutes ago now has a reason to be
        // reconsidered: the game the user just asked for may be one of them,
        // running from outside the install root that made it look like a
        // stranger. One extra path resolution per suspect, once per launch.
        lock (_gate)
        {
            _rejected.Clear();
        }
    }

    /// <summary>
    /// Shutdown flush. Already-exited games get real end times; still-running games
    /// are written with null end time. Also drains any previously queued sessions.
    /// </summary>
    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        lock (_gate)
        {
            foreach (var live in _live.Values)
            {
                // A game that has already exited gets its real end time even
                // here; only the still-running ones become open rows.
                var session = live.LiveProcesses > 0
                    ? BuildOpenSession(live, now)
                    : BuildSession(live, live.LastExitUtc ?? now);

                if (session is not null)
                {
                    Enqueue(session);
                }
            }

            _live.Clear();
            ReleaseAllLocked();
        }

        var written = await DrainPendingAsync(ct).ConfigureAwait(false);

        if (written > 0)
        {
            _logger.LogInformation("Shutdown: wrote {Count} outstanding session(s).", written);
        }

        var stranded = PendingCount;
        if (stranded > 0)
        {
            // Said out loud rather than swallowed: these are real sittings that
            // this process observed and could not persist, and the queue dies
            // with the app.
            _logger.LogWarning(
                "Shutdown: {Count} session(s) could not be written and are lost.", stranded);
        }

        return written;
    }

    /// <summary>Queued sessions awaiting a successful insert.</summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Adds a finished session to the write queue. Caller holds the lock. Bounded to <see cref="MaxPendingSessions"/>.</summary>
    private void Enqueue(Session session)
    {
        if (_pending.Count >= MaxPendingSessions)
        {
            _logger.LogWarning(
                "The session write queue is full at {Cap}; dropping the oldest queued session "
                + "for ownership {OwnershipId}. The database has been rejecting writes.",
                MaxPendingSessions, _pending[0].OwnershipId);
            _pending.RemoveAt(0);
        }

        _pending.Add(session);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _live.Clear();
            ReleaseAllLocked();
        }
    }

    private async Task EnsureIndexAsync(DateTime now, CancellationToken ct)
    {
        // A clock that jumps backwards — an NTP correction, a VM resuming from a
        // snapshot, a user fixing their timezone — makes `now - builtAt`
        // negative, which is trivially less than any interval. Left as a plain
        // comparison the index would then never rebuild again until the clock
        // caught back up to where it had been, silently and for as long as the
        // jump was large. Treating "the stamp is in the future" as due costs one
        // extra rebuild in a case that should never happen.
        var elapsed = now - _indexBuiltAtUtc;
        if (elapsed >= TimeSpan.Zero && elapsed < _options.IndexRefreshInterval)
        {
            return;
        }

        try
        {
            _index = await _indexBuilder.BuildAsync(ct).ConfigureAwait(false);
            _indexBuiltAtUtc = now;

            // The negative cache holds verdicts reached against the old index.
            // A game whose install path was wrong, or missing, when the previous
            // index was built would otherwise stay ignored for as long as its
            // process kept running — which for a long session is the whole
            // session. Re-deciding costs one path resolution per still-running
            // suspect, once per rebuild.
            lock (_gate)
            {
                _rejected.Clear();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed rebuild keeps the previous index, which is better than
            // both alternatives: an empty one would silently stop watching every
            // game, and rethrowing would take the hosted service down. The
            // stamp is not advanced, so the next tick retries.
            _logger.LogWarning(ex, "Executable index rebuild failed; keeping the previous index.");
        }
    }

    /// <summary>Tier 1 discovery: filters by name first, then resolves paths only for candidates.</summary>
    private int Discover(DateTime now)
    {
        var index = _index;

        // Snapshotted once per pass, outside the lock below, and empty whenever
        // nobody has pressed Play. See LaunchIntents.ExpectedNames for why this
        // does not breach §5.2's cost rule.
        var expected = _intents.ExpectedNames(now);
        var listings = _source.List();
        List<ProcessListing> candidates;
        long pass;

        lock (_gate)
        {
            // Every process attached during this pass shares a pass number. See
            // LiveSession.Join for what that buys.
            pass = ++_discoveryPass;

            // Anything that has gone away since the last poll leaves the
            // negative cache, so the dictionary tracks live pids rather than
            // growing for the life of the process.
            if (_rejected.Count > 0)
            {
                var alive = new HashSet<int>(listings.Count);
                foreach (var listing in listings)
                {
                    alive.Add(listing.Pid);
                }

                foreach (var pid in _rejected.Keys.Where(pid => !alive.Contains(pid)).ToList())
                {
                    _rejected.Remove(pid);
                }
            }

            candidates = [];
            foreach (var listing in listings)
            {
                // Already ours. No pid-identity re-check is needed here: the
                // retained handle makes it impossible for this pid to be a
                // different process than the one we attached (see
                // ITrackedProcess).
                if (_tracked.ContainsKey(listing.Pid))
                {
                    continue;
                }

                if (_rejected.TryGetValue(listing.Pid, out var rejectedName)
                    && string.Equals(rejectedName, listing.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // ***** The Tier 1 filter. Nothing above this line opened a
                // handle, and nothing below it runs for a non-candidate. *****
                if (!index.ProcessNames.Contains(listing.ProcessName)
                    && !expected.Contains(listing.ProcessName))
                {
                    continue;
                }

                candidates.Add(listing);
            }
        }

        var started = 0;
        foreach (var candidate in candidates)
        {
            // Tier 2 work — opening the process, resolving its path — happens
            // outside the lock so a slow or blocking OpenProcess cannot stall an
            // exit callback for another game.
            var process = _source.Track(candidate.Pid, candidate.ProcessName);
            if (process is null)
            {
                continue;
            }

            // Inference first, ALWAYS. A path that resolves inside some game's
            // install directory is that game, whatever the user last clicked —
            // filesystem evidence outranks a declaration, and inverting this
            // would let a pending launch relabel a second game the user started
            // from Steam while waiting. The intent is consulted only where M3a
            // would have shrugged: see LaunchIntents.Attribute for the two rules
            // and for what it deliberately refuses to do.
            var ownershipId = index.Match(process.ExecutablePath, process.ProcessName)
                ?? _intents.Attribute(process.ExecutablePath, process.ProcessName, now);
            if (ownershipId is null)
            {
                // A name collision with something that is not a game: the user's
                // own build of an executable that shares a name with one in a
                // game folder, or a game folder's name matching an unrelated
                // tool. Correct outcome, cached so it costs one resolution.
                _logger.LogDebug(
                    "Process {Name} (pid {Pid}) at {Path} is not inside any install directory; ignoring.",
                    process.ProcessName, process.Pid, process.ExecutablePath ?? "<unknown>");
                process.Dispose();

                lock (_gate)
                {
                    _rejected[candidate.Pid] = candidate.ProcessName;
                }

                continue;
            }

            if (Attach(process, ownershipId.Value, pass, now))
            {
                started++;

                // Outside Attach, and therefore outside its lock: this raises an
                // event the UI is listening to. Idempotent per declared launch,
                // so a game whose second executable joins the same session does
                // not announce itself twice.
                _intents.Fulfil(ownershipId.Value, now);
            }
        }

        return started;
    }

    /// <summary>Adds a tracked process to its game's session, opening one if needed.</summary>
    private bool Attach(ITrackedProcess process, long ownershipId, long discoveryPass, DateTime now)
    {
        // Read before taking the lock: LaunchIntents has a lock of its own, and
        // nesting two of them in one order here and the other order anywhere
        // else is how a deadlock gets written.
        var attribution = _intents.IsLive(ownershipId, now)
            ? SessionAttributions.Launch
            : SessionAttributions.Inferred;

        lock (_gate)
        {
            if (_disposed || !_tracked.TryAdd(process.Pid, new TrackedGame(process, ownershipId)))
            {
                process.Dispose();
                return false;
            }

            if (!_live.TryGetValue(ownershipId, out var live))
            {
                live = new LiveSession(ownershipId, process.StartedAtUtc, discoveryPass, attribution);
                _live[ownershipId] = live;
                _logger.LogDebug(
                    "Session opened for ownership {OwnershipId} at {Start:u} (first process {Name}, pid {Pid}).",
                    ownershipId, process.StartedAtUtc, process.ProcessName, process.Pid);
            }
            else if (live.LiveProcesses == 0)
            {
                // The relaunch case: this game's previous process had exited and
                // the session was waiting out its grace. It is the same sitting.
                _logger.LogDebug(
                    "Ownership {OwnershipId} relaunched through {Name} (pid {Pid}) inside the grace window; "
                    + "extending the open session rather than starting a second one.",
                    ownershipId, process.ProcessName, process.Pid);
            }

            live.Join(process.StartedAtUtc, discoveryPass, _options.RelaunchGrace);
        }

        // Subscribe only after the process is in the tracking table, so a
        // callback that fires immediately finds something to update.
        process.Exited += OnProcessExited;

        // Covers the window between Track() opening the process and this
        // subscription. .NET's Process raises Exited for a handler attached
        // after the fact, but the interface does not promise that of every
        // implementation and the check costs nothing.
        if (process.HasExited)
        {
            OnProcessExited(process, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>Exit callback (thread pool). Records timestamp under lock; DB writes happen on the next tick.</summary>
    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not ITrackedProcess process)
        {
            return;
        }

        // Belt as well as braces. This runs on a thread pool thread from an OS
        // callback, where an escaping exception is unhandled and terminates the
        // process — it does not merely fail the session. Every member touched
        // below is written to be safe after disposal (ITrackedProcess.Pid is
        // cached for exactly this reason), so this catch should be unreachable;
        // "should be unreachable" is not a good enough reason to let a crash out
        // of a path that runs on every game exit forever.
        try
        {
            HandleExit(process);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record the exit of pid {Pid}.", TryReadPid(process));
        }
    }

    private static int TryReadPid(ITrackedProcess process)
    {
        try
        {
            return process.Pid;
        }
        catch
        {
            return -1;
        }
    }

    private void HandleExit(ITrackedProcess process)
    {
        lock (_gate)
        {
            if (!_tracked.TryGetValue(process.Pid, out var game)
                || !ReferenceEquals(game.Process, process))
            {
                return;
            }

            _tracked.Remove(process.Pid);
            _closing.Add(process);

            // The OS's record of the exit, not the clock: a callback delayed by
            // thread pool starvation or a laptop waking from sleep still yields
            // the right end time.
            var exitedAt = process.ExitedAtUtc ?? _timeProvider.GetUtcNow().UtcDateTime;
            if (_live.TryGetValue(game.OwnershipId, out var live))
            {
                live.Leave(exitedAt);
            }
        }
    }

    /// <summary>Finalises sessions past the relaunch grace and releases handles of exited processes.</summary>
    private int Collect(DateTime now)
    {
        var debounced = 0;

        lock (_gate)
        {
            foreach (var process in _closing)
            {
                // Unsubscribe before disposing, matching the _tracked path in
                // ReleaseAllLocked. The object is about to be dropped either
                // way, but leaving a live subscription on a disposed process is
                // how a callback ends up running against torn-down state.
                process.Exited -= OnProcessExited;
                process.Dispose();
            }

            _closing.Clear();

            foreach (var live in _live.Values.ToList())
            {
                if (live.LiveProcesses > 0 || live.LastExitUtc is not { } endedAt)
                {
                    continue;
                }

                if (now - endedAt < _options.RelaunchGrace)
                {
                    // Still inside the handoff window. A second executable may
                    // yet appear and rejoin this session — see
                    // SessionWatcherOptions.RelaunchGrace.
                    continue;
                }

                _live.Remove(live.OwnershipId);

                var session = BuildSession(live, endedAt);
                if (session is null)
                {
                    debounced++;
                }
                else
                {
                    // Queued, not written. Finalising and persisting are
                    // separate steps on purpose — see _pending.
                    Enqueue(session);
                }
            }
        }

        return debounced;
    }

    /// <summary>Builds a session row, or null if under the debounce floor.</summary>
    private Session? BuildSession(LiveSession live, DateTime endedAt)
    {
        var duration = endedAt - live.StartedAtUtc;
        if (duration < TimeSpan.Zero)
        {
            // Only reachable if the wall clock moved backwards between the OS
            // recording the start and recording the exit — a DST-unaware clock
            // correction, a VM resuming from a snapshot. A negative duration
            // would violate the schema's CHECK anyway, and there is no honest
            // repair, so the record is dropped and said out loud.
            _logger.LogWarning(
                "Discarding a session for ownership {OwnershipId}: it ended at {End:u}, before it "
                + "started at {Start:u}. The system clock moved backwards.",
                live.OwnershipId, endedAt, live.StartedAtUtc);
            return null;
        }

        if (duration < _options.MinimumSessionDuration)
        {
            _logger.LogDebug(
                "Debounced a {Duration:n0}s run of ownership {OwnershipId} (floor is {Floor:n0}s).",
                duration.TotalSeconds, live.OwnershipId, _options.MinimumSessionDuration.TotalSeconds);
            return null;
        }

        return new Session
        {
            OwnershipId = live.OwnershipId,
            StartedAt = live.StartedAtUtc,
            EndedAt = endedAt,
            DurationSeconds = (long)duration.TotalSeconds,
            DetectionMethod = DetectionMethods.ProcessWatch,
            AttributedBy = live.Attribution,
        };
    }

    /// <summary>Builds a session with null end time (shutdown while game still running).</summary>
    private Session? BuildOpenSession(LiveSession live, DateTime now)
    {
        var elapsed = now - live.StartedAtUtc;
        if (elapsed < _options.MinimumSessionDuration)
        {
            return null;
        }

        return new Session
        {
            OwnershipId = live.OwnershipId,
            StartedAt = live.StartedAtUtc,
            EndedAt = null,
            DurationSeconds = null,
            DetectionMethod = DetectionMethods.ProcessWatch,
            AttributedBy = live.Attribution,
        };
    }

    private void ReleaseAllLocked()
    {
        foreach (var game in _tracked.Values)
        {
            game.Process.Exited -= OnProcessExited;
            game.Process.Dispose();
        }

        _tracked.Clear();

        foreach (var process in _closing)
        {
            process.Exited -= OnProcessExited;
            process.Dispose();
        }

        _closing.Clear();
        _rejected.Clear();
    }

    private sealed record TrackedGame(ITrackedProcess Process, long OwnershipId);

    /// <summary>One game's in-progress session, spanning all processes under its install directory.</summary>
    private sealed class LiveSession(
        long ownershipId, DateTime startedAtUtc, long openedInPass, string attribution)
    {
        public long OwnershipId { get; } = ownershipId;

        /// <summary>Fixed when the session opens; later processes joining cannot change it.</summary>
        public string Attribution { get; } = attribution;

        /// <summary>Session start time, narrowed by <see cref="Join"/>.</summary>
        public DateTime StartedAtUtc { get; private set; } = startedAtUtc;

        /// <summary>Discovery pass in which this session was opened. See <see cref="Join"/>.</summary>
        private long OpenedInPass { get; } = openedInPass;

        /// <summary>Latest exit seen; the session's end once nothing is left running.</summary>
        public DateTime? LastExitUtc { get; private set; }

        /// <summary>Processes in this group still running. Zero starts the grace clock.</summary>
        public int LiveProcesses { get; private set; }

        /// <summary>
        /// Adds a process to the group. Within the opening pass, the earliest start
        /// wins freely. In later passes, the start can only pull back by at most
        /// <paramref name="maxPullBack"/> to prevent long-running tools from
        /// backdating the session.
        /// </summary>
        public void Join(DateTime startedAtUtc, long discoveryPass, TimeSpan maxPullBack)
        {
            LiveProcesses++;

            if (startedAtUtc >= StartedAtUtc)
            {
                return;
            }

            StartedAtUtc = discoveryPass == OpenedInPass
                ? startedAtUtc
                : Max(startedAtUtc, StartedAtUtc - maxPullBack);
        }

        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

        public void Leave(DateTime exitedAtUtc)
        {
            if (LiveProcesses > 0)
            {
                LiveProcesses--;
            }

            if (LastExitUtc is null || exitedAtUtc > LastExitUtc)
            {
                LastExitUtc = exitedAtUtc;
            }
        }
    }
}
