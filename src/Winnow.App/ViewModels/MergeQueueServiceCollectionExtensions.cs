using Microsoft.Extensions.DependencyInjection;

namespace Winnow.App.ViewModels;

/// <summary>
/// Composition-root hook for the Same Game screen, so
/// <c>Program.ConfigureServices</c> gains one line rather than a list.
///
/// <para>Its required dependencies, <c>IMergeCandidateRepository</c>,
/// <c>IReleaseRepository</c>, <c>IWorkRepository</c> and
/// <c>IIdentityLinkRepository</c>, are already registered by the host.
/// The link repository is required, not optional, so omitting it breaks
/// the container at startup rather than rendering a screen whose answers
/// quietly write nothing.</para>
///
/// <para><c>ICoverCache</c> is optional; calling <c>AddCoverCache()</c> is
/// what upgrades the 200x300 covers from the procedural placeholder to real
/// capsule art.</para>
///
/// <para><c>IResolveStateRepository</c> is optional too, and it is what lets
/// the empty state tell "the matcher found nothing" apart from "the matcher
/// has not run". Without it the screen falls back to the second, weaker
/// claim, which is the safe direction, but it means registering the
/// soft-match sweep without also registering this repository leaves the
/// queue permanently understating what it knows.</para>
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
