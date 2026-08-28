using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Winnow.Monitor;

/// <summary>A launch Winnow started that the watcher has now seen running.</summary>
/// <param name="OwnershipId">The ownership the user clicked Play on.</param>
/// <param name="ObservedAtUtc">When the watcher attached to the first process.</param>
public readonly record struct LaunchObserved(long OwnershipId, DateTime ObservedAtUtc);

/// <summary>
/// <b>The M3b attribution seam: what Winnow knows when Winnow did the launching.</b>
///
/// <para>§5.2's process watcher is an inference engine. It sees a process, asks
/// which owned game's install directory contains it, and answers from what the
/// filesystem happens to say. §5.2 lists where that reasoning is thin — launchers
/// spawn children, some games relaunch through a second executable, Proton wraps
/// the whole thing in a tree — and the design doc is right that it is the fragile
/// half of session detection. The watcher's own fallback is thinner still: when a
/// process's main module cannot be read (an elevated game, an anti-cheat driver,
/// a 32-bit host reading a 64-bit process) there is no path to join on at all,
/// and all it can do is accept the name if exactly one owned game claims it.
/// <c>Game.exe</c> is not one game.</para>
///
/// <para><b>None of that arises when the user clicked Play inside Winnow.</b> The
/// app knows the ownership before the URI is even fired. This registry is where
/// it puts that answer so the watcher can use it instead of guessing, and it is
/// the whole reason M3b is worth more than a button that opens a URL.</para>
///
/// <para><b>What it deliberately does NOT do.</b> A pending intent never makes an
/// unknown process into a game. It only resolves a question the watcher was
/// already asking about a process that already looks like <i>that</i> game — by
/// install root or by executable name. Accepting "any new process while a launch
/// is pending" would attribute the user's music player to Portal 2, and one wrong
/// eight-hour session is worse for every downstream number than ten missed ones.
/// The rules in <see cref="Attribute"/> are the whole policy and there is no
/// broader fallback behind them.</para>
///
/// <para><b>Lifetime.</b> An intent is live for <see cref="Window"/> from the
/// moment it is declared. Fulfilment does not end it: a game that hands off to a
/// second executable needs the intent to still be there when the successor
/// appears, which is the same handoff <c>RelaunchGrace</c> exists for. Expiry is
/// silent — a user who cancelled at Steam's own prompt has done nothing wrong and
/// gets told nothing.</para>
///
/// <para>Thread-safe. <see cref="Declare"/> is called from the UI thread;
/// everything else runs on the watcher's tick and on OS exit callbacks.</para>
/// </summary>
public sealed class LaunchIntents
{
    private readonly Lock _gate = new();

    /// <summary>Newest first, so the most recent click wins a tie.</summary>
    private readonly List<PendingLaunch> _pending = [];

    private readonly ILogger<LaunchIntents> _logger;

    private static readonly IReadOnlySet<string> EmptyNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A ceiling on live intents. Each one is three fields and a name set, so
    /// this is not about memory — it is about a UI bug that declares in a loop
    /// never turning into an ever-widening attribution window.
    /// </summary>
    private const int MaxPending = 16;

    public LaunchIntents(
        IOptions<SessionWatcherOptions>? options = null,
        ILogger<LaunchIntents>? logger = null)
    {
        Window = options?.Value.LaunchWindow ?? new SessionWatcherOptions().LaunchWindow;
        _logger = logger ?? NullLogger<LaunchIntents>.Instance;
    }

    /// <summary>
    /// How long a declared launch stays eligible to claim a process. Long enough
    /// for a store client to cold-start, show its own prompt and hand off — §5.2
    /// notes a game can take 30 seconds to appear — and short enough that a
    /// launch the user abandoned cannot claim something they started later.
    /// </summary>
    public TimeSpan Window { get; }

    /// <summary>
    /// Raised the first time the watcher attaches to a process belonging to a
    /// declared launch. This is the only positive signal in the system that a
    /// launch actually worked, and it is what the UI's ambient indicator waits
    /// for.
    ///
    /// <para>Raised on the watcher's tick thread and never under a lock.
    /// Subscribers are invoked defensively: a throwing handler is logged and
    /// swallowed, because the alternative is an exception escaping onto a thread
    /// nobody is catching on — the M3a lesson, applied to a new event rather than
    /// relearned.</para>
    /// </summary>
    public event EventHandler<LaunchObserved>? Observed;

    /// <summary>
    /// Records that Winnow is about to fire the store's launch URI for this
    /// ownership. Called BEFORE the dispatch, deliberately: a warm store client
    /// can have the game running before <c>LaunchUriAsync</c> has returned, and
    /// an intent declared after that race is an intent that missed its own
    /// launch.
    /// </summary>
    /// <returns>
    /// False when a live intent for this ownership already exists — the caller's
    /// cue that this is a double-click, not a second launch. Declining here is
    /// what stops two Steam prompts appearing for one impatient user.
    /// </returns>
    public bool Declare(long ownershipId, DateTime nowUtc)
    {
        lock (_gate)
        {
            DropExpiredLocked(nowUtc);

            if (_pending.Any(p => p.OwnershipId == ownershipId))
            {
                return false;
            }

            if (_pending.Count >= MaxPending)
            {
                _logger.LogWarning(
                    "Dropping the oldest launch intent: {Count} are already live, which is more "
                    + "than a person can have clicked.",
                    _pending.Count);
                _pending.RemoveAt(_pending.Count - 1);
            }

            _pending.Insert(0, new PendingLaunch(ownershipId, nowUtc, nowUtc + Window));
            return true;
        }
    }

    /// <summary>
    /// Withdraws an intent: the dispatch failed, or the UI gave up waiting.
    /// Silent — nothing is raised, because nothing happened.
    /// </summary>
    public void Abandon(long ownershipId)
    {
        lock (_gate)
        {
            _pending.RemoveAll(p => p.OwnershipId == ownershipId);
        }
    }

    /// <summary>Whether a launch of this ownership is still in its window.</summary>
    public bool IsLive(long ownershipId, DateTime nowUtc)
    {
        lock (_gate)
        {
            return _pending.Any(p => p.OwnershipId == ownershipId && nowUtc < p.ExpiresAtUtc);
        }
    }

    /// <summary>
    /// Ownerships with a live intent that has not yet been told what to look
    /// for. The watcher answers with <see cref="Describe"/>.
    /// </summary>
    public IReadOnlyList<long> AwaitingDescription(DateTime nowUtc)
    {
        lock (_gate)
        {
            DropExpiredLocked(nowUtc);
            return _pending.Where(p => !p.Described).Select(p => p.OwnershipId).ToList();
        }
    }

    /// <summary>
    /// Tells an intent what this game's processes look like: its install root and
    /// the executable names found under it.
    ///
    /// <para>The watcher supplies both from its own index rather than the caller
    /// supplying them, which keeps §5.1 intact — the UI declares an ownership id
    /// and knows nothing about install paths — and keeps one reader of
    /// <c>ownerships.install_path</c> rather than two that can disagree.</para>
    ///
    /// <para>An empty name set is a normal outcome (an ownership with no recorded
    /// install path, an unreadable directory) and it means this intent will
    /// attribute nothing. That is the correct answer: with no idea what the game's
    /// executable is called, the only remaining rule would be "believe whatever
    /// starts next", which this class does not do.</para>
    /// </summary>
    public void Describe(long ownershipId, string? installRoot, IReadOnlySet<string> processNames)
    {
        ArgumentNullException.ThrowIfNull(processNames);

        lock (_gate)
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].OwnershipId == ownershipId)
                {
                    _pending[i] = _pending[i] with
                    {
                        Described = true,
                        InstallRoot = installRoot,
                        ProcessNames = processNames,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Every executable name the live launches are waiting for.
    ///
    /// <para><b>This widens the Tier 1 filter, and it is the only thing allowed
    /// to.</b> §5.2's cost rule is that the five-second poll resolves a path only
    /// for processes whose NAME is already known to belong to a game, because
    /// resolving all of them costs a fifth of a core permanently. That rule is
    /// untouched here: these names come from a scan of the launched game's own
    /// install directory, so a process only gets past the filter if it is named
    /// like the game the user just asked for. What it buys is the game whose
    /// binary the library-wide index never saw — buried past the depth limit, or
    /// past the per-game executable cap — which without this is filtered out
    /// before attribution is ever consulted.</para>
    ///
    /// <para>Empty in the overwhelmingly common case of no launch pending, and
    /// the watcher snapshots it once per pass rather than asking per
    /// process.</para>
    /// </summary>
    public IReadOnlySet<string> ExpectedNames(DateTime nowUtc)
    {
        lock (_gate)
        {
            DropExpiredLocked(nowUtc);

            if (_pending.Count == 0)
            {
                return EmptyNames;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var intent in _pending)
            {
                names.UnionWith(intent.ProcessNames);
            }

            return names;
        }
    }

    /// <summary>
    /// The answer the watcher could not have worked out for itself, or null.
    ///
    /// <para>Consulted only AFTER the index has failed to place the process, so
    /// filesystem evidence always outranks a declared intent — if a path resolves
    /// inside some other game's install directory, that is what it is, whatever
    /// the user last clicked.</para>
    ///
    /// <para>Two rules, both requiring the process to already resemble this
    /// game:</para>
    /// <list type="number">
    /// <item><b>Inside the declared install root.</b> Reached when the index's own
    /// root list is stale — the game was installed, or moved to another library
    /// folder, since the last rebuild.</item>
    /// <item><b>Named like one of this game's executables.</b> This is the case
    /// the seam exists for. The watcher's name fallback requires the name to be
    /// unique across the whole library and gives up otherwise; a declared launch
    /// makes uniqueness irrelevant, because the ambiguity was never about which
    /// game the user meant.</item>
    /// </list>
    /// </summary>
    public long? Attribute(string? executablePath, string processName, DateTime nowUtc)
    {
        lock (_gate)
        {
            DropExpiredLocked(nowUtc);

            foreach (var intent in _pending)
            {
                if (!string.IsNullOrEmpty(executablePath)
                    && intent.InstallRoot is { Length: > 0 } root
                    && PathUnder(executablePath, root))
                {
                    return intent.OwnershipId;
                }

                if (intent.ProcessNames.Contains(processName))
                {
                    return intent.OwnershipId;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The watcher has attached to a process for this ownership. Raises
    /// <see cref="Observed"/> exactly once per declared launch; the intent itself
    /// stays live so a handoff to a second executable is still covered.
    /// </summary>
    public void Fulfil(long ownershipId, DateTime nowUtc)
    {
        var announce = false;

        lock (_gate)
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].OwnershipId != ownershipId || _pending[i].Fulfilled)
                {
                    continue;
                }

                _pending[i] = _pending[i] with { Fulfilled = true };
                announce = true;
            }
        }

        if (!announce)
        {
            return;
        }

        _logger.LogInformation(
            "Launch of ownership {OwnershipId} is running; the session will be attributed exactly.",
            ownershipId);

        // Outside the lock, and defended: this reaches the UI, and a handler that
        // throws must not take down the watcher tick that raised it.
        try
        {
            Observed?.Invoke(this, new LaunchObserved(ownershipId, nowUtc));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A launch-observed handler threw; ignoring it.");
        }
    }

    /// <summary>Drops intents past their window. Returns how many went.</summary>
    public int Sweep(DateTime nowUtc)
    {
        lock (_gate)
        {
            return DropExpiredLocked(nowUtc);
        }
    }

    /// <summary>Live intents, for tests and diagnostics.</summary>
    public int PendingCount(DateTime nowUtc)
    {
        lock (_gate)
        {
            DropExpiredLocked(nowUtc);
            return _pending.Count;
        }
    }

    private int DropExpiredLocked(DateTime nowUtc)
        => _pending.RemoveAll(p => nowUtc >= p.ExpiresAtUtc);

    /// <summary>
    /// Same prefix rule and same case sensitivity as
    /// <see cref="GameExecutableIndex"/>: folding case on Linux would let one
    /// game's directory swallow another's.
    /// </summary>
    private static bool PathUnder(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return path.Length > root.Length
            && (path[root.Length] == Path.DirectorySeparatorChar
                || path[root.Length] == Path.AltDirectorySeparatorChar)
            && path.AsSpan(0, root.Length).Equals(root, comparison);
    }

    private sealed record PendingLaunch(long OwnershipId, DateTime DeclaredAtUtc, DateTime ExpiresAtUtc)
    {
        public bool Described { get; init; }

        public bool Fulfilled { get; init; }

        public string? InstallRoot { get; init; }

        public IReadOnlySet<string> ProcessNames { get; init; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
