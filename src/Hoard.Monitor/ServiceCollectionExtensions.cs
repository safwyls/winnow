using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hoard.Monitor;

/// <summary>
/// Composition for the §5.2 mechanism-A session watcher. The host's composition
/// root calls <see cref="AddSessionWatching"/>; nothing outside this assembly
/// needs to know which pieces are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the process watcher and the hosted service that drives it.
    ///
    /// <para>Requires <c>IOwnershipRepository</c> and <c>ISessionRepository</c>
    /// from the container (§5.1: this module reads and writes through the
    /// repository interfaces and nothing else). Registration is unconditional
    /// and safe on any machine — a library with no installed games produces an
    /// empty executable index, and a poll against an empty name set matches
    /// nothing and opens no handles.</para>
    ///
    /// <para>Every registration is substitutable: the singletons are
    /// <c>TryAdd</c>, and <c>AddHostedService</c> is <c>TryAddEnumerable</c>
    /// underneath, so registering this twice yields one watcher rather than two
    /// polling loops fighting over the same SQLite writer. A caller can replace
    /// any piece — most usefully <see cref="IProcessSource"/>, which is the seam
    /// the whole test suite drives — by registering it first.</para>
    /// </summary>
    public static IServiceCollection AddSessionWatching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<SessionWatcherOptions>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessSource, SystemProcessSource>();
        services.TryAddSingleton<GameExecutableIndexBuilder>();
        services.TryAddSingleton<SessionWatcher>();
        services.AddHostedService<SessionWatcherService>();

        return services;
    }
}
