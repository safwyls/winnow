using Hoard.Resolve.Matching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hoard.Resolve;

/// <summary>
/// Composition for §5.3 step 2. The host's composition root calls
/// <see cref="AddSoftMatching(IServiceCollection)"/>; nothing outside this
/// assembly needs to know that scoring, queueing and candidate generation are
/// three separate objects.
/// </summary>
public static class SoftMatchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the soft matcher, the queue writer and the library sweep that
    /// drives them.
    ///
    /// <para><b>What the container must already hold.</b>
    /// <c>IReleaseRepository</c>, <c>IMergeCandidateRepository</c>,
    /// <c>IResolveStateRepository</c> and <c>IUnitOfWorkFactory</c> — all of
    /// them Hoard.Core abstractions the data layer supplies. Resolve depends on
    /// Core alone (§5.1) and so cannot register their implementations
    /// itself.</para>
    ///
    /// <para>Every registration is <c>TryAdd</c>, so re-tuned thresholds or a
    /// fake sweep registered beforehand win.</para>
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

        // Singletons because all three are stateless: the matcher is pure, and
        // the other two hold only their dependencies. Nothing here caches a
        // library between passes — a sweep re-reads everything, which is what
        // makes it idempotent rather than incremental.
        services.TryAddSingleton<SoftMatcher>();
        services.TryAddSingleton<SoftMatchResolver>();
        services.TryAddSingleton<LibrarySoftMatchSweep>();

        return services;
    }
}
