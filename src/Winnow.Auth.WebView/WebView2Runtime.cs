using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace Winnow.Auth.WebView;

/// <summary>
/// Answers one question without side effects: is there a WebView2 runtime on
/// this machine?
/// </summary>
public static class WebView2Runtime
{
    private static readonly Lock Gate = new();
    private static bool _probed;
    private static string? _version;

    /// <summary>The installed runtime's version, or null when there is none.</summary>
    public static string? Version
    {
        get
        {
            lock (Gate)
            {
                if (!_probed)
                {
                    _probed = true;
                    _version = Probe();
                }

                return _version;
            }
        }
    }

    /// <summary>Whether an embedded browser can be created here at all.</summary>
    public static bool IsAvailable => OperatingSystem.IsWindows() && Version is not null;

    /// <summary>
    /// The probe itself, kept in a separate method with
    /// <see cref="MethodImplOptions.NoInlining"/> so assembly-load failures
    /// are caught here rather than at the caller.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // The documented "no runtime installed" answer.
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException
            or BadImageFormatException
            or TypeInitializationException
            or FileNotFoundException
            or NotSupportedException)
        {
            // The loader itself could not be brought up: WebView2Loader.dll
            // missing from the publish output, an architecture mismatch, or a
            // single-file/trimmed layout that lost the native asset. All of them
            // mean the same thing to a caller — no embedded browser here — and
            // all of them must degrade rather than take the app down.
            return null;
        }
    }
}
