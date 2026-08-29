namespace Winnow.Monitor;

/// <summary>
/// Knobs for the §5.2 mechanism-A process watcher. Defaults are the shipped
/// values; only <see cref="Enabled"/> is currently set from the composition
/// root, mirroring <c>--no-sync</c>.
/// </summary>
public sealed class SessionWatcherOptions
{
    /// <summary>
    /// How often Tier 1 enumerates processes. Governs responsiveness, not
    /// accuracy: recorded start times come from the OS, not the poll.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sessions shorter than this are discarded rather than written (the
    /// debounce). Measured on the finished session, after relaunches are folded in.
    /// </summary>
    public TimeSpan MinimumSessionDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long after a game's last process exits the watcher waits before
    /// writing, in case a successor process appears. Must exceed
    /// <see cref="PollInterval"/>.
    /// </summary>
    public TimeSpan RelaunchGrace { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the executable index is rebuilt from the ownership rows, so a
    /// game installed while Winnow is running becomes watchable.
    ///
    /// <para>Matched to <c>SnapshotSchedulerOptions.Interval</c> on purpose:
    /// that scheduler is what notices the install in the first place, and
    /// rebuilding faster than the data underneath changes only re-reads the same
    /// rows. A newly installed game therefore becomes watchable within two of
    /// these, worst case.</para>
    /// </summary>
    public TimeSpan IndexRefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long a scanned install directory's executable list is reused before
    /// the directory is walked again. Six hours: patches add executables, but
    /// not often, and the miss costs one session at worst — a name that is not
    /// in the Tier 1 filter is simply not noticed, never mis-attributed.
    /// </summary>
    public TimeSpan ExecutableScanTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Directory levels below an install root the executable scan descends.
    ///
    /// <para>Four covers the deepest real layout in play: Unreal keeps the
    /// shipping binary at
    /// <c>&lt;install&gt;/&lt;Project&gt;/Binaries/Win64/Game-Win64-Shipping.exe</c>,
    /// which is three, and leaves a shim at the root. Deeper trees than that are
    /// engine and middleware, which the builder's skipped-directory list
    /// prunes anyway.</para>
    /// </summary>
    public int ExecutableScanDepth { get; set; } = 4;

    /// <summary>
    /// Upper bound on executables indexed per game, so one pathological install
    /// directory (a game bundling an SDK, a modding toolchain) cannot inflate
    /// the Tier 1 name set for the whole library.
    /// </summary>
    public int MaxExecutablesPerGame { get; set; } = 64;

    /// <summary>
    /// How long a launch Winnow fired stays eligible to claim a process
    /// (M3b, <see cref="LaunchIntents"/>).
    /// </summary>
    public TimeSpan LaunchWindow { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Whether the watcher runs at all. Off mirrors <c>--no-sync</c> and
    /// <c>--seed-sample</c>: both mean "leave this database alone", and a
    /// session appearing under a seeded library would be a fabricated fact about
    /// a game the user does not own.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
