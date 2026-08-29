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
    /// 15 minutes balances Steam's eventual-consistency flush rate against
    /// capturing multiple points per evening session. Non-positive disables
    /// the scheduler.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether the scheduler runs at all. Off mirrors <c>--no-sync</c>: UI work
    /// against a fixed database must not have rows appearing underneath it.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether to run a sync immediately on start. False by default because
    /// Program already syncs before the window opens.</summary>
    public bool RunOnStartup { get; set; }
}
