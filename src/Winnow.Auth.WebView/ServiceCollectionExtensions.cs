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
    /// <see cref="IInteractiveAuthPrompt"/>.
    ///
    /// <para><b>Registration order is the fallback order.</b> Prompts are
    /// consulted in the order they were registered, so this goes in before the
    /// console peer: a machine with a WebView2 runtime gets the automatic flow,
    /// and one without falls through. Nothing here inspects the runtime — this
    /// is safe to call on any machine, and the prompt reports itself unavailable
    /// at the moment of use rather than at startup.</para>
    ///
    /// <para><b>A plain <c>AddSingleton</c> rather than
    /// <c>TryAddEnumerable</c></b>, because the prompt is constructed by a
    /// factory (it takes a path) and <c>TryAddEnumerable</c> rejects a
    /// factory-built descriptor it cannot distinguish by implementation type.
    /// Call this once; calling it twice would put two browsers in the
    /// chain.</para>
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
