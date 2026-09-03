using Winnow.Core.Identity;
using Winnow.Resolve.Matching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Resolve;

/// <summary>
/// Composition for §5.3 step 2. The host's composition root calls
/// <see cref="AddSoftMatching(IServiceCollection)"/>; nothing outside this
/// assembly needs to know that scoring, queueing and candidate generation are
/// three separate objects.
/// </summary>
public static class SoftMatchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the soft matcher, the queue writer, the library sweep and the
    /// expansion scan. Requires <c>IReleaseRepository</c>,
    /// <c>IMergeCandidateRepository</c>, <c>IResolveStateRepository</c>,
    /// <c>IIdentityLinkRepository</c>, <c>IExpansionRefusalRepository</c> and
    /// <c>IUnitOfWorkFactory</c> from the data layer. All registrations use
    /// <c>TryAdd</c>.
    /// </summary>
    public static IServiceCollection AddSoftMatching(this IServiceCollection services)
        => services.AddSoftMatching(SoftMatchThresholds.Default, SoftMatchSweepOptions.Default);

    /// <inheritdoc cref="AddSoftMatching(IServiceCollection)"/>
    /// <param name="services">The container.</param>
    /// <param name="thresholds">
    /// Scoring bands. Retuning these changes what reaches a human's review
    /// queue, so it belongs in one place with the tests that pin it.
    /// </param>
    /// <param name="sweep">Candidate-generation rails — block size and the comparison ceiling.</param>
    public static IServiceCollection AddSoftMatching(
        this IServiceCollection services,
        SoftMatchThresholds thresholds,
        SoftMatchSweepOptions sweep)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(sweep);

        services.TryAddSingleton(thresholds);
        services.TryAddSingleton(sweep);
        services.TryAddSingleton(TimeProvider.System);

        // Stateless -- no library state is cached between passes.
        services.TryAddSingleton<SoftMatcher>();
        services.TryAddSingleton<SoftMatchResolver>();
        services.TryAddSingleton<LibrarySoftMatchSweep>();

        // The expansion detector's tuning and the scan that drives it.
        // Registered here because it reads the same release identities the
        // sweep does and answers the other half of §5.3 step 2:
        // the soft matcher scores title DISTANCE and can therefore never
        // propose Civilization IV with Beyond the Sword, so a separate
        // detector is not an optimisation but the only way that pair is ever
        // offered. It writes nothing and auto-applies nothing.
        services.TryAddSingleton(ExpansionDetectorOptions.Default);
        services.TryAddSingleton<LibraryExpansionScan>();

        return services;
    }
}
