using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Winnow.Data;

namespace Winnow.App.Services;

/// <summary>
/// Hands back what the startup pipeline has stopped needing: the pooled SQLite
/// page caches its concurrent reads leave on the native heap, and the GC
/// regions its allocation burst commits.
///
/// <para>Nothing here changes what the pipeline does or what the library holds,
/// only when the memory goes back. A connection a finished stage returned keeps
/// its page cache for as long as the pool holds it, and the GC keeps its
/// regions for the next allocation burst — which, after startup, only arrives
/// on the next launch.</para>
///
/// <para>Lifetime: created when the pipeline starts, disposed when it ends, and
/// it trims on an interval in between — the pipeline runs for minutes on a
/// first-run library, so a trim only at the end would leave the burst's peak
/// standing for all of it.</para>
/// </summary>
internal sealed class StartupMemoryTrim : IDisposable
{
    /// <summary>
    /// Long enough that a stage's own allocations are not collected out from
    /// under it on every pass, short enough that the burst's peak does not
    /// stand for the several minutes a first-run pipeline takes.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    /// <summary>
    /// One trim at a time. <see cref="Timer.Dispose()"/> does not wait for a
    /// callback already running, so the final trim can meet a tick.
    /// </summary>
    private readonly object _gate = new();

    private readonly ISqliteConnectionFactory _factory;
    private readonly ILogger _logger;
    private readonly CancellationToken _shutdown;
    private readonly Timer _timer;

    private StartupMemoryTrim(
        ISqliteConnectionFactory factory, ILogger logger, CancellationToken shutdown)
    {
        _factory = factory;
        _logger = logger;
        _shutdown = shutdown;
        _timer = new Timer(_ => Trim(finished: false), state: null, Interval, Interval);
    }

    /// <param name="services">
    /// The host's provider. Resolved rather than injected because the startup
    /// pipeline is a <c>Task.Run</c> in <see cref="Program"/>, not a service.
    /// </param>
    /// <param name="shutdown">
    /// The run's shutdown token. A window closed two seconds in must not be
    /// held up by a blocking collection.
    /// </param>
    public static StartupMemoryTrim Start(IServiceProvider services, CancellationToken shutdown)
        => new(
            services.GetRequiredService<ISqliteConnectionFactory>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StartupMemoryTrim)),
            shutdown);

    public void Dispose()
    {
        _timer.Dispose();
        if (!_shutdown.IsCancellationRequested)
        {
            Trim(finished: true);
        }
    }

    private void Trim(bool finished)
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        lock (_gate)
        {
            Collect(finished);
        }
    }

    private void Collect(bool finished)
    {
        var before = Sample();

        // Costs the next reader one file open, and closes nothing that is
        // currently leased — see ReleasePooledConnections.
        _factory.ReleasePooledConnections();

        // Blocking and compacting, because the goal is pages returned to the
        // operating system rather than a smaller live set, and a background
        // collection promises neither. Aggressive is the mode that decommits
        // instead of keeping the regions warm for the next allocation burst.
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        // The second pass is not superstition: a cover bitmap the pipeline
        // dropped holds its pixels in native memory until its finalizer runs,
        // and the finalizer only queues on the collection above.
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        var after = Sample();

        // Information rather than Debug because the file sink's minimum level is
        // Information, and a memory report is exactly what a user complaining
        // about memory is asked to send. Two templates rather than a {Phase}
        // property: the formatter redacts string values by design.
        if (finished)
        {
            _logger.LogInformation(
                "Startup memory trim, pipeline finished: private {PrivateBefore}->{PrivateAfter} MB, "
                + "GC committed {CommittedBefore}->{CommittedAfter} MB.",
                before.Private, after.Private, before.Committed, after.Committed);
        }
        else
        {
            _logger.LogInformation(
                "Startup memory trim: private {PrivateBefore}->{PrivateAfter} MB, "
                + "GC committed {CommittedBefore}->{CommittedAfter} MB.",
                before.Private, after.Private, before.Committed, after.Committed);
        }
    }

    private static (long Private, long Committed) Sample()
    {
        using var self = Process.GetCurrentProcess();
        return (
            self.PrivateMemorySize64 / (1024 * 1024),
            GC.GetGCMemoryInfo().TotalCommittedBytes / (1024 * 1024));
    }
}
