namespace Winnow.App.Services;

/// <summary>
/// Knobs for <see cref="SnapshotSchedulerService"/>. Defaults are the shipped
/// values; tests shorten the interval and Program flips <see cref="Enabled"/>
/// off for <c>--no-sync</c>.
/// </summary>
public sealed class SnapshotSchedulerOptions
{
    /// <summary>
    /// How often the local Steam scan re-runs while the app is resident.
    ///
    /// <para><b>Why 15 minutes.</b> Two bounds meet here and neither is cost.
    /// The scan itself is filesystem-only — ~0.2s for 616 games, no network, no
    /// user-facing path blocked — so at this interval it is a 0.02% duty cycle
    /// and the interval could be far shorter without anyone noticing the
    /// machine. What stops it being shorter is the other end: §4.1 says the
    /// Steam client "does not flush config changes to disk immediately… treat a
    /// running Steam client as an eventually-consistent writer". Polling
    /// <c>localconfig.vdf</c> faster than Steam rewrites it does not sample
    /// playtime more finely, it just re-reads the same bytes and the resolver
    /// (idempotent by change detection) writes nothing — real work traded for
    /// no extra history. 15 minutes sits under the coarsest thing the data is
    /// used for — §6.1's staleness buckets reason in months — while still
    /// catching several points across a single evening's session, and it bounds
    /// the loss when the app is killed rather than closed to one quarter hour
    /// of a series that previously only gained a point per app launch.</para>
    ///
    /// <para>Non-positive disables the scheduler outright rather than
    /// busy-looping.</para>
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether the scheduler runs at all. Off mirrors <c>--no-sync</c>: UI work
    /// against a fixed database must not have rows appearing underneath it.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether the scheduler performs its own sync the moment it starts.
    ///
    /// <para><b>False by design.</b> Program already syncs once, synchronously,
    /// before the window opens — a scheduler tick at T+0 would be a second scan
    /// seconds behind the first, resolving byte-identical files. Left false, the
    /// first tick lands a full <see cref="Interval"/> after startup, which is
    /// exactly the spacing wanted. Set true only if Program's startup sync is
    /// removed and the scheduler is to own that path too; the window would then
    /// open before the first scan finishes.</para>
    /// </summary>
    public bool RunOnStartup { get; set; }
}
