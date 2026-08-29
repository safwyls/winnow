using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Winnow.Core.Auth;

namespace Winnow.Auth.WebView;

/// <summary>
/// Composition for the embedded Steam account-page session.
/// </summary>
public static class SteamHarvestServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WebView2SteamPageHarvester"/> as the
    /// <see cref="ISteamAccountPageHarvester"/>. Safe to call on any machine: the
    /// harvester reports itself unavailable at use time when there is no runtime
    /// or no window to host it in, and the caller falls back to the saved-file
    /// route.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="profileRoot">
    /// Where the throwaway browser profile is created. Defaults to the machine's
    /// temp directory. Unlike the sign-in prompt's profile root, nothing
    /// accumulates here: every run makes its own subdirectory and deletes it.
    /// </param>
    public static IServiceCollection AddSteamAccountPageHarvester(
        this IServiceCollection services, string? profileRoot = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISteamAccountPageHarvester>(sp => new WebView2SteamPageHarvester(
            profileRoot,
            sp.GetService<ILogger<WebView2SteamPageHarvester>>()));

        return services;
    }
}
