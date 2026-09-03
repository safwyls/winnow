using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Winnow.App.Services;

/// <summary>
/// Composition for M5's historical playtime backfill. Registered from the
/// composition root beside the other sync jobs; the startup pipeline calls it
/// after the remote ownership sync, which is what creates the rows it attaches
/// to.
/// </summary>
public static class SteamPlaytimeBackfillRegistration
{
    /// <summary>
    /// Registers <see cref="ISteamPlaytimeBackfill"/>. Requires
    /// <c>AddSteamWebApi()</c> for <see cref="Winnow.Enrich.SteamWeb.ISteamHistoryClient"/>
    /// and the repository registrations the rest of the App layer already makes.
    /// No API key is required: an install without one gets a backfill that
    /// declines every pass.
    /// </summary>
    public static IServiceCollection AddSteamPlaytimeBackfill(this IServiceCollection services)
        => services.AddSteamPlaytimeBackfill(configure: null);

    /// <inheritdoc cref="AddSteamPlaytimeBackfill(IServiceCollection)"/>
    /// <param name="services">The container.</param>
    /// <param name="configure">Overrides for <see cref="SteamPlaytimeBackfillOptions"/>.</param>
    public static IServiceCollection AddSteamPlaytimeBackfill(
        this IServiceCollection services, Action<SteamPlaytimeBackfillOptions>? configure)
    {
        var options = new SteamPlaytimeBackfillOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        // The same gate the two sync jobs take. A singleton because it is what
        // stops the backfill's write transaction from overlapping a resolver
        // pass on SQLite's single writer.
        services.TryAddSingleton<LibrarySyncGate>();

        // The one writer of the owned-account settings rows, shared with the
        // sign-in path. TryAdd because whichever of the two paths is composed
        // first registers it and the other must get the SAME one: two instances
        // would be two writers again, which is the thing the seam exists to
        // prevent.
        services.TryAddSingleton<ISteamAccountConfirmation, SteamAccountConfirmation>();

        services.TryAddSingleton<SteamPlaytimeBackfillService>();
        services.TryAddSingleton<ISteamPlaytimeBackfill>(
            sp => sp.GetRequiredService<SteamPlaytimeBackfillService>());

        return services;
    }
}
