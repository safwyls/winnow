using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.Core.Ingest;
using Winnow.Ingest.Steam.AccountPages;

namespace Winnow.App.Services;

/// <summary>DI registration for the Steam account-page import pipeline.</summary>
public static class SteamAccountPageImportRegistration
{
    /// <summary>Registers the file loader, the importer and its interface binding.</summary>
    public static IServiceCollection AddSteamAccountPageImport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SteamAccountPageFileLoader>();

        // Same instance under the Core contract. The import screen resolves the
        // interface so that no view model names an ingest type (§5.1); a
        // separately constructed loader would be a second reader with its own
        // clock, which is what stamps CapturedAt.
        services.TryAddSingleton<ISteamAccountPageFileLoader>(
            sp => sp.GetRequiredService<SteamAccountPageFileLoader>());

        services.TryAddSingleton<SteamAccountPageImportService>();
        services.TryAddSingleton<ISteamAccountPageImport>(
            sp => sp.GetRequiredService<SteamAccountPageImportService>());

        return services;
    }
}
