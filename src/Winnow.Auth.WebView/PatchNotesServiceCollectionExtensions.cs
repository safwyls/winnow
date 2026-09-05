using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Winnow.Core.Reading;

namespace Winnow.Auth.WebView;

/// <summary>
/// Composition for the embedded patch-notes reader.
/// </summary>
public static class PatchNotesServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="WebView2PatchNotesReader"/> as an
    /// <see cref="IPatchNotesReader"/>. Safe to call on any machine;
    /// the reader reports itself unavailable at use time if no runtime exists.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="profileRoot">
    /// Directory for the Chromium profile. The reader creates a
    /// <c>patch-notes</c> subdirectory under this root; <c>--data-dir</c>
    /// redirects it with everything else.
    /// </param>
    public static IServiceCollection AddWebViewPatchNotesReader(
        this IServiceCollection services, string profileRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoot);

        services.AddSingleton<IPatchNotesReader>(sp => new WebView2PatchNotesReader(
            profileRoot,
            sp.GetService<ILogger<WebView2PatchNotesReader>>()));

        return services;
    }
}
