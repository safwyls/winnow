using Microsoft.Extensions.DependencyInjection;

namespace Winnow.App.ViewModels;

/// <summary>
/// Registers the identity review presentation. Production supplies WinnowApiClient;
/// legacy tests may supply repositories directly while their fixtures migrate.
/// Covers, selection and focus remain local to the frontend.
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
