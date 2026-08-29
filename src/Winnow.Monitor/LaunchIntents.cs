using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Winnow.Monitor;

/// <summary>A launch Winnow started that the watcher has now seen running.</summary>
/// <param name="OwnershipId">The ownership the user clicked Play on.</param>
/// <param name="ObservedAtUtc">When the watcher attached to the first process.</param>
public readonly record struct LaunchObserved(long OwnershipId, DateTime ObservedAtUtc);

/// <summary>
/// M3b attribution seam: records launches Winnow started so the watcher can
/// attribute sessions exactly instead of inferring. An intent only resolves
/// processes that already look like the declared game (by install root or
/// executable name); it never makes an unknown process into a game. Thread-safe.
/// </summary>
public sealed class LaunchIntents
{
    private readonly Lock _gate = new();

    /// <summary>Newest first, so the most recent click wins a tie.</summary>
    private readonly List<PendingLaunch> _pending = [];

    private readonly ILogger<LaunchIntents> _logger;

    private static readonly IReadOnlySet<string> EmptyNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cap on live intents to prevent a buggy UI loop from widening the attribution window.</summary>
    private const int MaxPending = 16;

    public LaunchIntents(
        IOptions<SessionWatcherOptions>? options = null,
        ILogger<LaunchIntents>? logger = null)
    {
        Window = options?.Value.LaunchWindow ?? new SessionWatcherOptions().LaunchWindow;
        _logger = logger ?? NullLogger<LaunchIntents>.Instance;
    }

    /// <summary>How long a declared launch stays eligible to claim a process.</summary>
    public TimeSpan Window { get; }

    /// <summary>Raised once when the watcher first attaches to a process for a declared launch. Raised off-lock on the tick thread.</summary>
    public event EventHandler<LaunchObserved>? Observed;

    /// <summary>Declares a launch intent BEFORE firing the store URI. Returns false if one already exists (double-click guard).</summary>
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

    /// <summary>Withdraws an intent (dispatch failed or UI gave up).</summary>
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

    /// <summary>Ownerships with a live intent awaiting a <see cref="Describe"/> call.</summary>
    public IReadOnlyList<long> AwaitingDescription(DateTime nowUtc)
    {
        lock (_gate)
        {
            DropExpiredLocked(nowUtc);
            return _pending.Where(p => !p.Described).Select(p => p.OwnershipId).ToList();
        }
    }

    /// <summary>Tells an intent what this game's processes look like (install root and executable names).</summary>
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

    /// <summary>Executable names the live launches are waiting for, used to widen the Tier 1 filter.</summary>
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
    /// Attributes a process to a declared launch, or null. Consulted only after the
    /// index failed. Matches by install root or executable name.
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

    /// <summary>Marks a launch fulfilled and raises <see cref="Observed"/> once. The intent stays live for handoffs.</summary>
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

    /// <summary>Path-under check with platform-appropriate case sensitivity.</summary>
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
