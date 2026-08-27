using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace Hoard.Auth.WebView;

/// <summary>
/// Answers one question without side effects: is there a WebView2 runtime on
/// this machine?
///
/// <para><b>Why this is a real question and not a formality.</b> WebView2 ships
/// no browser — the Chromium engine is the OS-provided Evergreen runtime.
/// Microsoft documents it as included in Windows 11, and it was found
/// preinstalled at 151.0.4129.107 during the spike, but that is one machine.
/// Windows 10, an LTSC or Server SKU, an image built with it stripped, or a
/// managed fleet that blocks Edge updates are all real; treating its presence as
/// certain would turn a graceful fallback into a crash on somebody's
/// laptop.</para>
///
/// <para><b>The detection point is an exception, by design of the API.</b>
/// <see cref="CoreWebView2Environment.GetAvailableBrowserVersionString()"/>
/// throws <c>WebView2RuntimeNotFoundException</c> when there is nothing to find
/// — there is no TryGet form — so the throw IS the answer and catching it is not
/// swallowing an error.</para>
/// </summary>
public static class WebView2Runtime
{
    private static readonly Lock Gate = new();
    private static bool _probed;
    private static string? _version;

    /// <summary>
    /// The installed runtime's version, or null when there is none.
    ///
    /// <para>Probed once and remembered. A runtime installed while Hoard is
    /// running will not be noticed until the next launch, which is the right
    /// trade: this is called on a UI path, and the failing case is a native load
    /// attempt, not a cheap registry read.</para>
    /// </summary>
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
    /// The probe itself, kept out of its caller so that a failure to LOAD the
    /// WebView2 assembly or its native loader is caught too.
    ///
    /// <para><see cref="MethodImplOptions.NoInlining"/> is load-bearing. The JIT
    /// resolves the types a method references when it compiles that method, so an
    /// inlined body would raise <c>FileNotFoundException</c> or
    /// <c>DllNotFoundException</c> at the CALLER's frame — outside this try —
    /// and the fallback would never run. The same trick, for the same reason, is
    /// standard around optional native dependencies.</para>
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
