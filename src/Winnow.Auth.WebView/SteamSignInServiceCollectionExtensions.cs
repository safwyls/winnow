using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Winnow.Core.Auth;

namespace Winnow.Auth.WebView;

/// <summary>
/// Composition for the embedded Steam sign-in.
/// </summary>
public static class SteamSignInServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WebView2SteamSignInSession"/> as the
    /// <see cref="ISteamSignInSession"/>. Safe to call on any machine: the
    /// session reports itself unavailable at use time when there is no runtime or
    /// no window to host it in, and the Web API key remains a complete
    /// alternative for that user.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="profileRoot">
    /// Where the throwaway browser profile is created. Defaults to the machine's
    /// temp directory. Nothing accumulates here: every run makes its own
    /// subdirectory and deletes it, so the only durable home for anything this
    /// session captures is the encrypted session store.
    /// </param>
    public static IServiceCollection AddSteamWebViewSignIn(
        this IServiceCollection services, string? profileRoot = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISteamSignInSession>(sp => new WebView2SteamSignInSession(
            profileRoot,
            sp.GetService<ILogger<WebView2SteamSignInSession>>()));

        return services;
    }
}
