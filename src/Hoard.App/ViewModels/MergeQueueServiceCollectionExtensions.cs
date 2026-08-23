using Microsoft.Extensions.DependencyInjection;

namespace Hoard.App.ViewModels;

/// <summary>
/// Composition-root hook for the merge confirm queue, so
/// <c>Program.ConfigureServices</c> gains one line rather than a list.
///
/// <para>Its dependencies — <c>IMergeCandidateRepository</c>,
/// <c>IReleaseRepository</c>, <c>IWorkRepository</c> — are already registered
/// by the host; <c>ICoverCache</c> is optional, so calling
/// <c>AddCoverCache()</c> is what upgrades the 200×300 covers from the
/// procedural placeholder to real capsule art.</para>
/// </summary>
public static class MergeQueueServiceCollectionExtensions
{
    /// <summary>Registers <see cref="MergeQueueViewModel"/> as a singleton, like the other view models.</summary>
    public static IServiceCollection AddMergeQueue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddSingleton<MergeQueueViewModel>();
    }
}
