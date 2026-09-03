using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;

namespace Winnow.Auth.WebView;

/// <summary>
/// An Avalonia control that hosts a Chromium browser via
/// <see cref="NativeControlHost"/> and WebView2.
/// </summary>
public sealed class WebView2Host : NativeControlHost
{
    private readonly string _userDataFolder;
    private readonly bool _inPrivate;
    private readonly TaskCompletionSource<CoreWebView2Controller> _ready
        = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<bool> _closed
        = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IntPtr _childWindow;
    private CoreWebView2Controller? _controller;

    /// <param name="userDataFolder">
    /// Where Chromium keeps its profile. Must be supplied and must be writable:
    /// the default is beside the executable, which is read-only for an installed
    /// application, and the failure surfaces as an opaque environment-creation
    /// error rather than as a permissions one.
    /// </param>
    /// <param name="inPrivate">
    /// Create the browser on an off-the-record profile, so that cookies, history
    /// and cache live only for the life of the controller.
    ///
    /// <para>Defaults to <see langword="false"/>, which is the sign-in prompt's
    /// existing behaviour: an Epic session is deliberately persistent so a user
    /// who reconnects is not made to sign in again. A caller that needs
    /// ephemerality asks for it here <em>and</em> hands over a user-data folder
    /// it is prepared to delete. Private mode is why nothing of consequence is
    /// written there, and the deletion is why nothing at all is left behind.</para>
    ///
    /// <para>If the installed runtime cannot make a private profile, this fails
    /// the <see cref="Ready"/> task rather than quietly falling back to a
    /// persistent one.</para>
    /// </param>
    public WebView2Host(string userDataFolder, bool inPrivate = false)
    {
        _userDataFolder = userDataFolder;
        _inPrivate = inPrivate;
    }

    /// <summary>
    /// Completes when the browser is attached and usable, or faults when it
    /// cannot be created.
    /// </summary>
    public Task<CoreWebView2Controller> Ready => _ready.Task;

    /// <summary>
    /// Completes once the controller has been closed and the child window
    /// destroyed.
    ///
    /// <para>The signal a caller needs before deleting the user-data folder:
    /// until the browser process has let go, the profile's files are locked and
    /// the delete fails.</para>
    /// </summary>
    public Task Closed => _closed.Task;

    /// <inheritdoc/>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            _ready.TrySetException(new PlatformNotSupportedException("WebView2 is Windows-only."));
            return base.CreateNativeControlCore(parent);
        }

        // A bare STATIC child. It exists only to give WebView2 an HWND of its own
        // to parent to, so that Avalonia keeps owning the layout and WebView2
        // keeps owning everything inside the rectangle.
        _childWindow = CreateWindowExW(
            dwExStyle: 0,
            lpClassName: "STATIC",
            lpWindowName: null,
            dwStyle: WsChild | WsVisible | WsClipChildren | WsClipSiblings,
            x: 0,
            y: 0,
            nWidth: 1,
            nHeight: 1,
            hWndParent: parent.Handle,
            hMenu: IntPtr.Zero,
            hInstance: IntPtr.Zero,
            lpParam: IntPtr.Zero);

        if (_childWindow == IntPtr.Zero)
        {
            // Essentially unreachable — creating a STATIC child fails only if the
            // parent handle is already gone — but layout must not throw, so hand
            // back Avalonia's own empty child and let the caller see a failed
            // sign-in instead of a crashed window.
            _ready.TrySetException(new InvalidOperationException(
                "Could not create the child window for the embedded browser "
                + $"(Win32 error {Marshal.GetLastWin32Error()})."));
            return base.CreateNativeControlCore(parent);
        }

        // Fire-and-forget deliberately: this runs on the UI thread, so the
        // continuations resume on it, and everything a caller needs is observable
        // through Ready. Awaiting here is not an option — the layout pass that
        // called this is synchronous.
        _ = AttachBrowserAsync(_childWindow);

        return new PlatformHandle(_childWindow, "HWND");
    }

    /// <inheritdoc/>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        // Close the controller BEFORE the window it is parented to. The reverse
        // order leaves the browser process holding a handle to a destroyed HWND,
        // which is a hang rather than an exception.
        try
        {
            _controller?.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException)
        {
            // Already gone, or the browser process died first. Nothing to do:
            // this runs during teardown and must not throw.
        }

        _controller = null;

        if (OperatingSystem.IsWindows() && _childWindow != IntPtr.Zero)
        {
            DestroyWindow(_childWindow);
            _childWindow = IntPtr.Zero;
            _closed.TrySetResult(true);
            return;
        }

        base.DestroyNativeControlCore(control);
        _closed.TrySetResult(true);
    }

    /// <summary>
    /// Blocks or restores mouse and keyboard input to the hosted browser.
    ///
    /// <para><b>Avalonia's <c>IsHitTestVisible</c> cannot do this.</b> The
    /// browser lives in a native child window, and Windows delivers input to it
    /// directly; Avalonia's hit testing never sees those messages and has
    /// nothing to suppress. Disabling the child window is the level the input
    /// actually arrives at, and a disabled window's children are disabled with
    /// it, so this reaches the browser's own windows too.</para>
    ///
    /// <para>Best effort by design. A caller uses this to stop a stray click
    /// navigating away mid-capture; if it does not take, the capture is no worse
    /// off than it was before.</para>
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows() || _childWindow == IntPtr.Zero)
        {
            return;
        }

        EnableWindow(_childWindow, enabled);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);

        if (!OperatingSystem.IsWindows() || _childWindow == IntPtr.Zero)
        {
            return arranged;
        }

        var scale = VisualRoot?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)Math.Round(finalSize.Width * scale));
        var height = Math.Max(1, (int)Math.Round(finalSize.Height * scale));

        MoveWindow(_childWindow, 0, 0, width, height, bRepaint: true);

        if (_controller is not null)
        {
            // Relative to the child window, so the origin is always (0,0) — the
            // control's position within the Avalonia window is already expressed
            // by where Avalonia put the child HWND.
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
        }

        return arranged;
    }

    private async Task AttachBrowserAsync(IntPtr hwnd)
    {
        try
        {
            Directory.CreateDirectory(_userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _userDataFolder,
                options: null);

            var controller = _inPrivate
                ? await environment.CreateCoreWebView2ControllerAsync(hwnd, PrivateOptions(environment))
                : await environment.CreateCoreWebView2ControllerAsync(hwnd);

            // The window may have closed while the environment was starting — a
            // second or two on a cold start. Closing a controller nobody will
            // ever see is cheaper than leaking a browser process.
            if (_childWindow == IntPtr.Zero)
            {
                controller.Close();
                _ready.TrySetCanceled();
                return;
            }

            _controller = controller;

            // The first arrange almost always ran before this completed, so its
            // Bounds assignment was skipped. Without this line the browser is
            // 0x0 and the window looks empty.
            InvalidateArrange();

            _ready.TrySetResult(controller);
        }
        catch (Exception ex)
        {
            // Includes WebView2RuntimeNotFoundException (a runtime uninstalled
            // between the availability probe and now), an unwritable user-data
            // folder, and a browser process that failed to start. All of them are
            // a failed sign-in, never a crash.
            _ready.TrySetException(ex);
            _closed.TrySetResult(true);
        }
    }

    /// <summary>
    /// Controller options for an off-the-record profile.
    ///
    /// <para>Failing loudly is the point. A runtime too old to expose controller
    /// options would otherwise be served a perfectly working browser that writes
    /// the session to disk, which is the one outcome a caller asking for private
    /// mode cannot accept.</para>
    /// </summary>
    private static CoreWebView2ControllerOptions PrivateOptions(CoreWebView2Environment environment)
    {
        try
        {
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            return options;
        }
        catch (Exception ex) when (ex is NotImplementedException
            or COMException
            or PlatformNotSupportedException
            or MissingMethodException)
        {
            throw new NotSupportedException(
                "This WebView2 runtime cannot open a private browsing session.", ex);
        }
    }

    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string? lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);
}
