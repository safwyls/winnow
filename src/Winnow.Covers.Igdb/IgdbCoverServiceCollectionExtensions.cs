using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Covers.Igdb;

/// <summary>Composition root hook for the IGDB gap-filling cover source.</summary>
public static class IgdbCoverServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IgdbCoverSource"/> as an additional
    /// <see cref="ICoverSource"/>. Call after <c>AddCoverCache()</c> so Steam's
    /// portrait capsule stays first in registration order.
    /// </summary>
    public static IServiceCollection AddIgdbCoverSource(this IServiceCollection services)
        => services.AddIgdbCoverSource(null);

    /// <inheritdoc cref="AddIgdbCoverSource(IServiceCollection)"/>
    public static IServiceCollection AddIgdbCoverSource(
        this IServiceCollection services,
        Action<IgdbCoverOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(_ =>
        {
            var options = new IgdbCoverOptions();
            configure?.Invoke(options);
            return options;
        });

        // Same User-Agent, timeout and retry policy as the Steam capsule client:
        // images.igdb.com is another unauthenticated public image CDN, and the
        // 403-retries-but-404-does-not rule is exactly as load-bearing here.
        services.AddCoverImageHttpClient(IgdbCoverSource.HttpClientName);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICoverSource, IgdbCoverSource>());
        return services;
    }
}
