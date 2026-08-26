using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hoard.Monitor;

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
/// §5.2 mechanism A: the process-watching session detector. Discovery is polled
/// (Tier 1), exit is event-driven (Tier 2), and neither decides what gets
/// written — the OS's own start and exit timestamps do.
///
/// <para><b>A session belongs to an ownership, not to a process.</b> That single
/// decision is what answers three of §5.2's four named noise sources at once,
/// and it is worth stating before the code makes it look incidental. A watcher
/// that opened a session per pid would record, for a real Unreal title on the
/// developer's own machine: three seconds for <c>Palworld.exe</c> (the shim),
/// then two hours for <c>Palworld-Win64-Shipping.exe</c> (the game). Two rows,
/// one of them a lie about the user having bounced off. Grouping by the
/// ownership whose install directory both executables live under collapses that
/// to the one thing that happened. The same grouping handles a launcher that
/// precedes or outlives the game, and a Proton/Wine process tree, without
/// needing to reconstruct parent/child relationships at all — every member of
/// the tree resolves to the same install directory, which is a stronger join
/// than a parent pid and one that survives re-parenting.</para>
///
/// <para>The session opens at the earliest <c>StartTime</c> of any process in
/// the group and closes when the last of them has been gone for
/// <see cref="SessionWatcherOptions.RelaunchGrace"/>.</para>
///
/// <para><b>The accepted cost of that grouping</b> is that a run which never
/// reaches the game still counts as one. A user who opens a launcher, sits in
/// its settings for two minutes and closes it without pressing Play gets a
/// two-minute session against the game, because from outside the process there
/// is no way to tell that apart from a game that started and was quickly
/// abandoned — and the alternative, attributing nothing until some specific
/// "real" executable appears, would silently drop every game whose only
/// executable <i>is</i> its launcher. The debounce floor absorbs the short
/// version of this, and §5.2 mechanism B is the exact answer for anyone who
/// cares about the rest.</para>
///
/// <para><b>Threading.</b> Exit callbacks arrive on thread pool threads; they
/// take the lock, record a timestamp, and return. Everything else — matching,
/// finalising, and every database write — happens on whichever single caller is
/// driving <see cref="TickAsync"/>. Nothing writes to SQLite from a callback,
/// which matters because <c>SqliteConnectionFactory.Begin</c> throws outright if
/// a unit of work is already open on the same flow and because SQLite has one
/// writer.</para>
///
/// <para>§5.1: reads <see cref="IOwnershipRepository"/>, writes
/// <see cref="ISessionRepository"/>, and knows nothing about the UI.</para>
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

    /// <summary>
    /// Pids whose name passed the Tier 1 filter but which turned out not to be
    /// one of ours — a same-named process running from somewhere outside every
    /// install directory. Without this, every poll would re-open and re-resolve
    /// the same innocent process for as long as it runs.
    /// </summary>
    private readonly Dictionary<int, string> _rejected = [];

    /// <summary>
    /// Exited processes whose handle is released on the next tick rather than
    /// inside the callback. Disposing a <c>Process</c> from its own
    /// <c>Exited</c> handler is asking for trouble, and the extra few seconds of
    /// held handle keeps the pid pinned that much longer.
    /// </summary>
    private readonly List<ITrackedProcess> _closing = [];

    /// <summary>
    /// Finished sessions that have not been written yet.
    ///
    /// <para><b>Why finalising and writing are separate steps with a queue
    /// between them.</b> A session leaves <see cref="_live"/> the moment it is
    /// finalised, so if the insert that follows fails there is nowhere left to
    /// recover it from — the tracking state has already forgotten the game ever
    /// ran. That is not a theoretical path: a host stop landing between the
    /// reconciliation and the first insert cancels the write, and SQLite's
    /// single writer means a lock contended by the snapshot scheduler can fail
    /// one too. Losing an hour of someone's evening to a five-millisecond lock
    /// is exactly the outcome this module exists to prevent, so a session sits
    /// here until the database has actually accepted it, and the next tick — or
    /// <see cref="FlushAsync"/> — tries again.</para>
    /// </summary>
    private readonly List<Session> _pending = [];

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
        ILogger<SessionWatcher>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _source = source;
        _indexBuilder = indexBuilder;
        _sessions = sessions;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<SessionWatcher>.Instance;
    }

    /// <summary>The executable index as of the last rebuild. Exposed for diagnostics and tests.</summary>
    public GameExecutableIndex Index => _index;

    /// <summary>
    /// One pass: refresh the index if it is due, discover newly started game
    /// processes, and write any session whose relaunch grace has elapsed.
    ///
    /// <para>Must be called from one caller at a time — the hosted service's
    /// sequential loop guarantees that. Concurrent calls would not corrupt the
    /// tracking state (it is locked) but could open two SQLite writes at once.</para>
    /// </summary>
    public async Task<SessionWatcherTick> TickAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await EnsureIndexAsync(now, ct).ConfigureAwait(false);

        var started = Discover();
        var debounced = Collect(now);
        var recorded = await DrainPendingAsync(ct).ConfigureAwait(false);

        int running, queued;
        lock (_gate)
        {
            running = _tracked.Count;
            queued = _pending.Count;
        }

        return new SessionWatcherTick(running, started, recorded, debounced, queued);
    }

    /// <summary>
    /// Writes as many queued sessions as the database will accept, oldest first,
    /// and returns how many landed. A session is removed from the queue only
    /// after its insert has returned, so a failure part-way through leaves the
    /// remainder queued for the next attempt rather than dropping them.
    ///
    /// <para>Cancellation stops the drain instead of throwing: the queue is the
    /// recovery mechanism, and <see cref="FlushAsync"/> — which runs on its own
    /// token during shutdown — is the next attempt.</para>
    /// </summary>
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

            try
            {
                await _sessions.InsertAsync(session, ct).ConfigureAwait(false);
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
                "Recorded a {Duration:n0}s session for ownership {OwnershipId} ({Start:u} → {End:u}).",
                session.DurationSeconds ?? 0, session.OwnershipId, session.StartedAt, session.EndedAt);
        }

        return recorded;
    }

    /// <summary>
    /// Closes the watcher's books because the app is shutting down. Sessions
    /// whose game has already exited are written normally; sessions still in
    /// flight are written <b>with no end time</b>.
    ///
    /// <para><b>Why an open row rather than "ended now", and rather than
    /// nothing.</b> The user closing Hoard while a game is running is not an
    /// edge case — closing the library before settling into a long session is a
    /// perfectly ordinary thing to do — so all three options here get exercised
    /// on real machines. Writing <c>ended_at = now</c> would state a falsehood
    /// (the game is still running) and would bias every duration in the table
    /// downward in exactly the population of longest sessions. Writing nothing
    /// would lose the session entirely, and §6.1's episode counting cares more
    /// that a sitting happened than how long it was. The schema already allows
    /// <c>ended_at</c> and <c>duration_s</c> to be null, which is precisely the
    /// fact available: this session started, and this process never saw it
    /// end.</para>
    ///
    /// <para>The debounce still applies, measured against elapsed-so-far: a game
    /// launched thirty seconds before Hoard was closed is as likely to be a
    /// mis-click as any other short run, and is dropped on the same rule.</para>
    ///
    /// <para>An open row is never repaired — nothing can know when the game
    /// actually stopped. Mechanism B (§5.2 B, the launch-option wrapper) is the
    /// answer for anyone who wants exactness here, and is the reason the spec
    /// ships both.</para>
    ///
    /// <para>Anything the tick loop finalised but could not write is still
    /// queued, so this drains that queue too — a failed write on the last tick
    /// before shutdown gets one more attempt here rather than being lost.</para>
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

    /// <summary>
    /// Adds a finished session to the write queue. Caller holds the lock.
    ///
    /// <para>Bounded, because the alternative to dropping the oldest row is
    /// growing without limit against a database that is never going to answer —
    /// and an app that eventually dies of memory pressure loses the whole queue
    /// anyway. The cap is far above any plausible backlog: it is thousands of
    /// sessions, and reaching it means the database has been unwritable for
    /// months.</para>
    /// </summary>
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

    /// <summary>
    /// Tier 1 discovery. Returns the number of processes newly attached.
    ///
    /// <para><b>The name filter runs before anything else touches a process.</b>
    /// §5.2 calls this out specifically and it is the only part of this loop with
    /// a cost worth reasoning about. Measured on the developer's machine: 528
    /// processes running, the enumeration 12 ms, resolving all 528 paths
    /// 1,007 ms. None of the 528 was a game, which is the normal state, so the
    /// steady state of this method is 528 hash lookups against a 30-name set and
    /// not one syscall. Resolving paths first and filtering after would spend a
    /// fifth of a core on that answer, permanently.</para>
    /// </summary>
    private int Discover()
    {
        var index = _index;
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
                if (!index.ProcessNames.Contains(listing.ProcessName))
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

            var ownershipId = index.Match(process.ExecutablePath, process.ProcessName);
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

            if (Attach(process, ownershipId.Value, pass))
            {
                started++;
            }
        }

        return started;
    }

    /// <summary>
    /// Adds a tracked process to its game's session, opening one if this is the
    /// first process for that game. Returns false when the watcher is already
    /// disposed or the pid is somehow taken.
    /// </summary>
    private bool Attach(ITrackedProcess process, long ownershipId, long discoveryPass)
    {
        lock (_gate)
        {
            if (_disposed || !_tracked.TryAdd(process.Pid, new TrackedGame(process, ownershipId)))
            {
                process.Dispose();
                return false;
            }

            if (!_live.TryGetValue(ownershipId, out var live))
            {
                live = new LiveSession(ownershipId, process.StartedAtUtc, discoveryPass);
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

    /// <summary>
    /// §5.2 Tier 2: the OS told us. Runs on a thread pool thread, does the least
    /// possible work, and touches no IO — the session it may have completed is
    /// written by the next <see cref="TickAsync"/>.
    /// </summary>
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

    /// <summary>
    /// Finalises every session whose last process has been gone for longer than
    /// the relaunch grace, and releases the handles of processes that exited
    /// since the previous tick.
    /// </summary>
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

    /// <summary>
    /// Turns a completed group into a row, or null when the debounce floor
    /// rejects it. §5.2: "ignore sessions under 60s by default (configurable)".
    /// </summary>
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
        };
    }

    /// <summary>
    /// The shutdown form: a session known to have started and not known to have
    /// ended. See <see cref="FlushAsync"/> for why this shape exists.
    /// </summary>
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

    /// <summary>
    /// One game's in-progress session. Spans however many processes the game
    /// runs through — see the type remarks on <see cref="SessionWatcher"/>.
    /// </summary>
    private sealed class LiveSession(long ownershipId, DateTime startedAtUtc, long openedInPass)
    {
        public long OwnershipId { get; } = ownershipId;

        /// <summary>
        /// When this sitting began. Seeded from the first process seen for the
        /// game and then narrowed by <see cref="Join"/>, under the rules there.
        /// </summary>
        public DateTime StartedAtUtc { get; private set; } = startedAtUtc;

        /// <summary>Discovery pass in which this session was opened. See <see cref="Join"/>.</summary>
        private long OpenedInPass { get; } = openedInPass;

        /// <summary>Latest exit seen; the session's end once nothing is left running.</summary>
        public DateTime? LastExitUtc { get; private set; }

        /// <summary>Processes in this group still running. Zero starts the grace clock.</summary>
        public int LiveProcesses { get; private set; }

        /// <summary>
        /// Adds a process to the group, narrowing the session start if this
        /// process began earlier — but by how much depends on when it joined.
        ///
        /// <para><b>Why the rule is not simply "take the minimum".</b> An
        /// unconditional minimum lets any process that ever matches this game
        /// drag the session's start backwards without limit, and the install
        /// directory is not a set of things that only run while the game does. A
        /// resident updater, a dedicated server left running, a mod manager, a
        /// tool the user parked in the game folder — any of them, once the
        /// executable index picks it up, joins the group carrying a start time
        /// that may be days old, and the session then claims the sitting began
        /// last Tuesday. That row is worse than no row: it is indistinguishable
        /// from a real week-long session, and every duration statistic built on
        /// the table inherits it.</para>
        ///
        /// <para>So the minimum is free only within the discovery pass that
        /// opened the session. Everything seen together in that first sweep is
        /// one launch — the launcher and the game it already spawned, the whole
        /// of a process tree, a game that was already running when Hoard started
        /// — and their earliest start is the honest start.</para>
        ///
        /// <para>A process joining in a <i>later</i> pass may still pull the
        /// start back, because a launcher's successor legitimately can have
        /// started fractionally before we noticed the handoff, but no further
        /// back than <paramref name="maxPullBack"/> (the relaunch grace, which
        /// is already the window in which two processes are considered the same
        /// sitting). Anything older than that is a stranger, and it joins the
        /// session as a participant without rewriting when the session
        /// began.</para>
        ///
        /// <para>The residual is that such a stranger still holds
        /// <see cref="LiveProcesses"/> above zero and keeps the session open
        /// while it runs. That costs latency and a longer session, not a
        /// fabricated start date, and the executable deny-list is what keeps the
        /// usual suspects out of the group in the first place.</para>
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
