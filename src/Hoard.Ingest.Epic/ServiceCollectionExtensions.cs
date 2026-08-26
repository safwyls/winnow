using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hoard.Ingest.Epic;

/// <summary>
/// Composition for the Epic local-file ingest. The host's composition root calls
/// <see cref="AddEpicIngest(IServiceCollection)"/>; nothing outside this assembly
/// needs to know which readers are involved.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EpicLibrarySource"/> and its readers as singletons.
    ///
    /// <para>No configuration and no credentials: everything this module reads is
    /// a local file the launcher already wrote. A machine without Epic resolves
    /// the same services and its scan returns an empty list, so registration is
    /// unconditional and callers need no "is Epic installed" check.</para>
    ///
    /// <para>Every registration is <c>TryAdd</c>, so a caller can substitute any
    /// piece — most usefully <see cref="IEpicThirdPartyInstallProbe"/> — by
    /// registering it first.</para>
    /// </summary>
    public static IServiceCollection AddEpicIngest(this IServiceCollection services)
        => services.AddEpicIngest(dataRoot: null);

    /// <inheritdoc cref="AddEpicIngest(IServiceCollection)"/>
    /// <param name="services">The container.</param>
    /// <param name="dataRoot">
    /// Launcher <c>Data</c> root to scan, overriding registry/well-known
    /// discovery. Null — the default — means discover it.
    /// </param>
    public static IServiceCollection AddEpicIngest(this IServiceCollection services, string? dataRoot)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<EpicManifestReader>();
        services.TryAddSingleton<EpicCatalogReader>();
        services.TryAddSingleton<EpicThirdPartyAppReader>();
        services.TryAddSingleton<IEpicThirdPartyInstallProbe, WindowsEpicThirdPartyInstallProbe>();

        services.TryAddSingleton(sp => new EpicLibrarySource(
            sp.GetRequiredService<EpicManifestReader>(),
            sp.GetRequiredService<EpicCatalogReader>(),
            sp.GetRequiredService<EpicThirdPartyAppReader>(),
            sp.GetRequiredService<IEpicThirdPartyInstallProbe>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<EpicLibrarySource>>(),
            sp.GetRequiredService<TimeProvider>(),
            dataRoot));

        return services;
    }
}
