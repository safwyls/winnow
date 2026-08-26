using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hoard.Ingest.Gog;

/// <summary>
/// Composition for the GOG local ingest. The host's composition root calls
/// <see cref="AddGogIngest(IServiceCollection)"/>; nothing outside this assembly
/// needs to know which readers are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="GogLibrarySource"/> and its readers as singletons.
    ///
    /// <para>No configuration and no credentials: everything this module reads is
    /// a local file GOG already wrote. A machine with neither Galaxy nor a GOG
    /// install registry resolves the same services and its scan returns an empty
    /// list, so registration is unconditional.</para>
    ///
    /// <para>Every registration is <c>TryAdd</c>, so a caller can substitute any
    /// piece — most usefully <see cref="IGogInstalledGameRegistry"/> — by
    /// registering it first.</para>
    /// </summary>
    public static IServiceCollection AddGogIngest(this IServiceCollection services)
        => services.AddGogIngest(galaxyRoot: null);

    /// <inheritdoc cref="AddGogIngest(IServiceCollection)"/>
    /// <param name="services">The container.</param>
    /// <param name="galaxyRoot">
    /// Galaxy root (the directory holding <c>config.json</c>) to scan, overriding
    /// discovery. Null — the default — means discover it.
    /// </param>
    public static IServiceCollection AddGogIngest(this IServiceCollection services, string? galaxyRoot)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<GalaxyLibraryReader>();
        services.TryAddSingleton<GogGameInfoReader>();
        services.TryAddSingleton<IGogInstalledGameRegistry, WindowsGogInstalledGameRegistry>();

        services.TryAddSingleton(sp => new GogLibrarySource(
            sp.GetRequiredService<GalaxyLibraryReader>(),
            sp.GetRequiredService<GogGameInfoReader>(),
            sp.GetRequiredService<IGogInstalledGameRegistry>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<GogLibrarySource>>(),
            sp.GetRequiredService<TimeProvider>(),
            galaxyRoot));

        return services;
    }
}
