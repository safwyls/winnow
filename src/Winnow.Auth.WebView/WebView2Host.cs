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
    private readonly TaskCompletionSource<CoreWebView2Controller> _ready
        = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IntPtr _childWindow;
    private CoreWebView2Controller? _controller;

    /// <param name="userDataFolder">
    /// Where Chromium keeps its profile. Must be supplied and must be writable:
    /// the default is beside the executable, which is read-only for an installed
    /// application, and the failure surfaces as an opaque environment-creation
    /// error rather than as a permissions one.
    /// </param>
    public WebView2Host(string userDataFolder) => _userDataFolder = userDataFolder;

    /// <summary>
    /// Completes when the browser is attached and usable, or faults when it
    /// cannot be created.
    /// </summary>
    public Task<CoreWebView2Controller> Ready => _ready.Task;

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
            return;
        }

        base.DestroyNativeControlCore(control);
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

            var controller = await environment.CreateCoreWebView2ControllerAsync(hwnd);

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
}
