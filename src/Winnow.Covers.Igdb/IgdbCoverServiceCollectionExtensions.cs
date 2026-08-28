using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.Covers.Igdb;

/// <summary>Composition root hook for the IGDB gap-filling cover source.</summary>
public static class IgdbCoverServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IgdbCoverSource"/> as an additional
    /// <see cref="ICoverSource"/>.
    ///
    /// <para><b>Call this after <c>AddCoverCache()</c>.</b> Sources are tried in
    /// registration order and the first one to answer wins, so the order is the
    /// policy: Steam's <c>library_600x900_2x.jpg</c> is the 2:3 portrait
    /// <c>design-system.md</c> §5 is drawn around and must stay first, with IGDB
    /// filling only what Steam declines. Registering this first would silently
    /// swap the art on 500 tiles that were never in question.</para>
    ///
    /// <para>Also requires <c>AddIgdbEnrichment()</c> somewhere in the same
    /// container for <c>IIgdbClient</c>; order relative to that call does not
    /// matter. With no credentials configured the source declines every key
    /// without a request and the grid behaves exactly as it did before it
    /// existed.</para>
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
