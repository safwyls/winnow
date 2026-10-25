using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Winnow.Covers;

public static class AvaloniaCoverServiceCollectionExtensions
{
    public static IServiceCollection AddCoverCache(this IServiceCollection services,
        Action<CoverCacheOptions>? configure = null)
    {
        services.AddCoverPipeline(configure);
        services.TryAddSingleton<ICoverCache>(sp => new CoverCache(
            sp.GetRequiredService<CoverPipeline>(),
            sp.GetRequiredService<CoverCacheOptions>(),
            sp.GetService<ILogger<CoverCache>>()));
        services.TryAddSingleton<ICoverLeases, CoverLeasePool>();
        return services;
    }
}
