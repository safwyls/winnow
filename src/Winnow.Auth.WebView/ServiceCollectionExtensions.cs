using Winnow.Core.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Winnow.Auth.WebView;

/// <summary>
/// Composition for the embedded-browser sign-in prompt.
/// </summary>
public static class WebViewAuthServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WebView2AuthPrompt"/> as an
    /// <see cref="IInteractiveAuthPrompt"/>. Safe to call on any machine;
    /// the prompt reports itself unavailable at use time if no runtime exists.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="profileRoot">
    /// Directory for the Chromium profiles, one subdirectory per provider. Must
    /// be writable: WebView2 defaults to the executable's own folder, which is
    /// read-only for an installed application, and the resulting failure looks
    /// like a browser that will not start rather than like a permissions
    /// problem.
    /// </param>
    public static IServiceCollection AddWebViewAuthPrompt(this IServiceCollection services, string profileRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoot);

        services.AddSingleton<IInteractiveAuthPrompt>(sp => new WebView2AuthPrompt(
            profileRoot,
            sp.GetService<ILogger<WebView2AuthPrompt>>()));

        return services;
    }
}
