namespace Winnow.Monitor;

/// <summary>
/// Knobs for the §5.2 mechanism-A process watcher. Defaults are the shipped
/// values; only <see cref="Enabled"/> is currently set from the composition
/// root, mirroring <c>--no-sync</c>.
/// </summary>
public sealed class SessionWatcherOptions
{
    /// <summary>
    /// How often Tier 1 enumerates processes looking for a game that started.
    ///
    /// <para><b>Five seconds, and this is a responsiveness setting rather than
    /// an accuracy one.</b> §5.2 is unusually direct about this and it is worth
    /// restating where someone might otherwise "improve" it: the recorded start
    /// time comes from <c>Process.StartTime</c>, not from the poll, so a game
    /// discovered five seconds late is written with its real start and its real
    /// duration. Dropping to one second produces byte-identical rows at five
    /// times the cost. Ten seconds would also be defensible.</para>
    ///
    /// <para>What the interval does govern is the width of the window in which a
    /// game can start and finish without ever being seen — but that window is
    /// far below <see cref="MinimumSessionDuration"/>, so anything lost to it
    /// would have been debounced away regardless.</para>
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Sessions shorter than this are discarded rather than written — §5.2's
    /// debounce, configurable as the spec requires.
    ///
    /// <para><b>Sixty seconds because a minute of runtime is not a play
    /// session.</b> It is a mis-click, a game opened to check a setting, a
    /// launcher that failed to reach the title screen, or a crash on startup.
    /// Writing those would put the recommendation engine's episode count —
    /// "distinct times the user came back to this" — permanently out of step
    /// with what happened, and §6.1's whole Bounced-versus-Never-played
    /// distinction is built on believing that number.</para>
    ///
    /// <para>Measured on the finished session, after every relaunch has been
    /// folded in, so a launcher that runs for three seconds before handing off
    /// to a two-hour game is not debounced — the session it belongs to is two
    /// hours long.</para>
    /// </summary>
    public TimeSpan MinimumSessionDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long after a game's last process exits the watcher waits before
    /// writing the session, in case another process for the same game appears.
    ///
    /// <para><b>This is the answer to two of §5.2's four named noise
    /// sources.</b> "Some games relaunch through a second executable (the first
    /// exits immediately)" and "launchers spawn child processes that outlive or
    /// precede the game" are the same event seen from two ends, and both look
    /// identical to a naive watcher: one short session followed by a long one,
    /// or a long one followed by a short one. Holding the session open across
    /// the handoff collapses them into the single record that actually
    /// happened.</para>
    ///
    /// <para><b>It must exceed <see cref="PollInterval"/>, and by more than a
    /// little.</b> The successor process is discovered by the poll, so a grace
    /// shorter than the interval would routinely finalise the session in the gap
    /// before the successor was even looked for. Thirty seconds is six polls of
    /// slack at the default interval, and is still short enough that two
    /// genuinely separate sittings — nobody relaunches a game they quit and
    /// resumes within half a minute — are not merged.</para>
    ///
    /// <para><b>The effective window is this plus <see cref="PollInterval"/>,
    /// not this value alone.</b> Discovery runs before reconciliation within a
    /// tick, so a successor is looked for once more after the grace has
    /// technically elapsed: thirty seconds configured behaves as up to
    /// thirty-five. That is the safe direction to be wrong in, and it is stated
    /// here so the name is not read as a precise guarantee.</para>
    ///
    /// <para>The cost is latency: a session lands in the database up to
    /// grace + interval after the game closes. Nothing user-facing waits on it,
    /// and the row's own timestamps are unaffected.</para>
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
    /// How long a launch Winnow fired itself stays eligible to claim a process
    /// (M3b, <see cref="LaunchIntents"/>).
    ///
    /// <para>Ninety seconds, and the bound is set by two opposite mistakes.
    /// Too short and the window closes while a cold store client is still
    /// starting — §5.2 already notes a game can take thirty seconds to appear,
    /// and that is measured from a client that was already running. Too long and
    /// a launch the user abandoned at Steam's own prompt is still sitting there
    /// when they start something else half an hour later, ready to put that
    /// session on the wrong game.</para>
    ///
    /// <para>Being wrong in the short direction costs an attribution that falls
    /// back to inference — which is M3a's behaviour and is usually correct
    /// anyway. Being wrong in the long direction costs a fabricated fact. So the
    /// window is deliberately shorter than "surely the game has started by
    /// now".</para>
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
