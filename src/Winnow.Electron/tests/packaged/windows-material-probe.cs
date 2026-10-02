using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WinnowMaterialProbe
{
    // This helper never changes a DWM attribute, OS preference or the target's opacity.
    public static class Capture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetFrame(IntPtr hwnd, int attribute, out RECT value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled(out bool enabled);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();

        private sealed class Underlay : Form
        {
            private readonly int pattern;
            public Underlay(int value)
            {
                pattern = value;
                Text = "Winnow owned material probe background";
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                DoubleBuffered = true;
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get
                {
                    var value = base.CreateParams;
                    value.ExStyle |= 0x08000000 | 0x00000080; // NOACTIVATE | TOOLWINDOW
                    return value;
                }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                // Broad alternating patches remain distinguishable after Acrylic blur.
                using (var first = new SolidBrush(pattern == 0 ? Color.FromArgb(244, 55, 78) : Color.FromArgb(24, 197, 219)))
                using (var second = new SolidBrush(pattern == 0 ? Color.FromArgb(30, 78, 235) : Color.FromArgb(240, 184, 30)))
                {
                    for (var x = 0; x < Width; x += 160)
                        e.Graphics.FillRectangle((x / 160) % 2 == 0 ? first : second, x, 0, 160, Height);
                }
            }
        }

        private static Rectangle RectangleOf(RECT value)
        { return Rectangle.FromLTRB(value.Left, value.Top, value.Right, value.Bottom); }

        private static Rectangle Frame(IntPtr hwnd)
        {
            RECT frame;
            if (DwmGetFrame(hwnd, 9, out frame, Marshal.SizeOf(typeof(RECT))) != 0)
                throw new InvalidOperationException("DWM could not provide the target's visible frame bounds.");
            return RectangleOf(frame);
        }

        private static bool Cloaked(IntPtr hwnd)
        {
            int value;
            return DwmGetWindowAttribute(hwnd, 14, out value, sizeof(int)) == 0 && value != 0;
        }

        private static bool Overlaps(IntPtr hwnd, Rectangle area)
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || Cloaked(hwnd)) return false;
            RECT value;
            if (!GetWindowRect(hwnd, out value))
                throw new InvalidOperationException("A window's bounds changed during the overlap guard.");
            return RectangleOf(value).IntersectsWith(area);
        }

        private static void ValidateTarget(IntPtr hwnd, int processId, Rectangle frame)
        {
            uint actualPid;
            GetWindowThreadProcessId(hwnd, out actualPid);
            if (!IsWindow(hwnd) || actualPid != processId)
                throw new InvalidOperationException("The supplied HWND does not belong to the supplied process.");
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || Cloaked(hwnd) || GetForegroundWindow() != hwnd)
                throw new InvalidOperationException(String.Format(
                    "Capture requires the target to be visible, unminimized and foreground (visible={0}, minimized={1}, cloaked={2}, foregroundMatches={3}).",
                    IsWindowVisible(hwnd), IsIconic(hwnd), Cloaked(hwnd), GetForegroundWindow() == hwnd));
            if (frame.Width <= 0 || frame.Height <= 0 || Frame(hwnd) != frame)
                throw new InvalidOperationException("The target's bounds changed or are empty.");
            if (!Screen.FromHandle(hwnd).Bounds.Contains(frame))
                throw new InvalidOperationException("The target must fit fully within one physical display.");
        }

        private static void Guard(IntPtr hwnd, int processId, IntPtr underlay, Rectangle frame, Rectangle area)
        {
            ValidateTarget(hwnd, processId, frame);
            // Fail closed even for a tooltip or another same-process window above the target.
            var above = GetWindow(hwnd, 3); // GW_HWNDPREV: higher in z-order
            var examined = 0;
            while (above != IntPtr.Zero)
            {
                if (++examined > 10000) throw new InvalidOperationException("Unstable window z-order.");
                if (Overlaps(above, area))
                    throw new InvalidOperationException("Another visible window overlaps the target; no image was retained.");
                above = GetWindow(above, 3);
            }
            var below = GetWindow(hwnd, 2); // GW_HWNDNEXT
            examined = 0;
            while (below != IntPtr.Zero && below != underlay)
            {
                if (++examined > 10000) throw new InvalidOperationException("Unstable window z-order.");
                if (Overlaps(below, area))
                    throw new InvalidOperationException("Another visible window sits between the target and its owned underlay.");
                below = GetWindow(below, 2);
            }
            // Hidden IME/helper windows may occupy z-order slots but contribute no pixels.
            if (below != underlay || !IsWindowVisible(underlay))
                throw new InvalidOperationException("The owned underlay is not behind the target.");
            RECT background;
            if (!GetWindowRect(underlay, out background) || !RectangleOf(background).Contains(area))
                throw new InvalidOperationException("The owned underlay does not cover the entire capture rectangle.");
            // Some shell overlays are not exposed by ordinary top-level z-order traversal.
            // Check physical hit-test ownership over the capture rectangle as a second guard.
            for (var y = area.Top + 1; ; y = Math.Min(y + 24, area.Bottom - 2))
            {
                for (var x = area.Left + 1; ; x = Math.Min(x + 24, area.Right - 2))
                {
                    var hit = WindowFromPoint(new POINT(x, y));
                    uint hitPid;
                    GetWindowThreadProcessId(hit, out hitPid);
                    if (hit == IntPtr.Zero || (hitPid != processId && hit != underlay))
                        throw new InvalidOperationException("Physical hit testing found an unowned surface in the capture rectangle.");
                    if (x == area.Right - 2) break;
                }
                if (y == area.Bottom - 2) break;
            }
        }

        private static Dictionary<string, object> Attribute(IntPtr hwnd, int id)
        {
            int value;
            var result = DwmGetWindowAttribute(hwnd, id, out value, sizeof(int));
            return new Dictionary<string, object> { { "hresult", result }, { "value", result == 0 ? (object)value : null } };
        }

        public static Dictionary<string, object> Run(IntPtr hwnd, int processId, string output, int pattern, bool clientCenter)
        {
            var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
            if (previousDpi == IntPtr.Zero) throw new InvalidOperationException("Per-monitor physical-pixel coordinates are unavailable.");
            try
            {
                var frame = Frame(hwnd);
                uint actualPid;
                GetWindowThreadProcessId(hwnd, out actualPid);
                if (!IsWindow(hwnd) || actualPid != processId)
                    throw new InvalidOperationException("The supplied HWND does not belong to the supplied process.");
                SetForegroundWindow(hwnd);
                ValidateTarget(hwnd, processId, frame);
                var area = frame;
                if (clientCenter)
                {
                    RECT client;
                    var origin = new POINT(0, 0);
                    if (!GetClientRect(hwnd, out client) || !ClientToScreen(hwnd, ref origin))
                        throw new InvalidOperationException("Could not locate the target client area.");
                    var width = Math.Min(1200, client.Right - client.Left);
                    var height = Math.Min(800, client.Bottom - client.Top);
                    area = new Rectangle(origin.X + (client.Right - client.Left - width) / 2,
                        origin.Y + (client.Bottom - client.Top - height) / 2, width, height);
                    if (!frame.Contains(area)) throw new InvalidOperationException("Client capture would leave the target frame.");
                }
                bool composition;
                var compositionResult = DwmIsCompositionEnabled(out composition);
                if (compositionResult != 0 || !composition) throw new InvalidOperationException("DWM composition is not available.");
                using (var underlay = new Underlay(pattern))
                {
                    underlay.Bounds = frame;
                    underlay.Show();
                    try
                    {
                        if (!SetWindowPos(underlay.Handle, hwnd, frame.X, frame.Y, frame.Width, frame.Height, 0x0010 | 0x0040))
                            throw new InvalidOperationException("Could not position the owned background directly behind the target.");
                        underlay.Refresh();
                        var settle = Stopwatch.StartNew();
                        while (settle.ElapsedMilliseconds < 600)
                        {
                            Application.DoEvents();
                            Thread.Sleep(15);
                        }
                        var flush = DwmFlush();
                        if (flush != 0) throw new InvalidOperationException("DWM frame synchronization failed.");
                        Guard(hwnd, processId, underlay.Handle, frame, area);
                        var before = Attribute(hwnd, 38); // DWMWA_SYSTEMBACKDROP_TYPE
                        using (var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
                        {
                            using (var graphics = Graphics.FromImage(bitmap))
                                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
                            Guard(hwnd, processId, underlay.Handle, frame, area);
                            var after = Attribute(hwnd, 38);
                            if (!Object.Equals(before["value"], after["value"]) || !Object.Equals(before["hresult"], after["hresult"]))
                                throw new InvalidOperationException("DWM backdrop configuration changed during capture.");
                            bitmap.Save(output, ImageFormat.Png);
                            return new Dictionary<string, object> {
                                { "captured", true }, { "utc", DateTime.UtcNow.ToString("O") },
                                { "targetHandle", hwnd.ToInt64().ToString() }, { "targetProcessId", processId },
                                { "underlayProcessId", Process.GetCurrentProcess().Id }, { "pattern", pattern },
                                { "capture", clientCenter ? "screen pixels in bounded central owned client region" : "screen pixels bounded to DWM extended-frame bounds" },
                                { "bounds", new Dictionary<string, int> { { "x", area.X }, { "y", area.Y }, { "width", area.Width }, { "height", area.Height } } },
                                { "windowBounds", new Dictionary<string, int> { { "x", frame.X }, { "y", frame.Y }, { "width", frame.Width }, { "height", frame.Height } } },
                                { "dpi", GetDpiForWindow(hwnd) }, { "foregroundAndZOrderGuardBeforeAndAfter", true },
                                { "physicalOwnershipGridBeforeAndAfter", true },
                                { "dwmCompositionEnabled", composition }, { "systemBackdropAttribute", after },
                                { "limitations", new[] {
                                    "The DWM backdrop attribute is configuration, not a material-visibility measurement.",
                                    "The PNG contains composed screen pixels; it is not a PrintWindow or Chromium page capture.",
                                    "Before/after z-order checks are not an atomic Windows capture transaction; captures require a quiescent desktop.",
                                    "The helper does not change or test OS transparency, high-contrast, wallpaper or remote-session settings."
                                } }
                            };
                        }
                    }
                    finally { underlay.Close(); }
                }
            }
            finally { SetThreadDpiAwarenessContext(previousDpi); }
        }
    }
}
