using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Winnow.App.Services;

public enum JournalNotificationDelivery { Submitted, Unavailable, Suppressed, Failed }

public interface IJournalNotification
{
    JournalNotificationDelivery Show(string title, Action activate, Action unavailable);
    void Dismiss();
}

/// <summary>Process-local Windows notifications; activation never launches another app instance.</summary>
public sealed class WindowsJournalNotification : IJournalNotification, IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x574;
    private Window? _window;
    private uint _nextId;
    private NotifyIconData _data;
    private Action? _activate;
    private Action? _unavailable;
    private DispatcherTimer? _deliveryTimer;
    private bool _shown;
    private nint _notificationIcon;

    public void Attach(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        _window = window;
        Win32Properties.AddWndProcHookCallback(window, WindowMessage);
        window.Closed += WindowClosed;
    }

    public JournalNotificationDelivery Show(string title, Action activate, Action unavailable)
    {
        Dismiss();
        if (!OperatingSystem.IsWindows() || _window?.TryGetPlatformHandle() is not { Handle: var handle, HandleDescriptor: "HWND" } || handle == 0)
            return JournalNotificationDelivery.Unavailable;
        try
        {
            var query = SHQueryUserNotificationState(out var state);
            if (query != 0) return JournalNotificationDelivery.Unavailable;
            if (state != 5) return JournalNotificationDelivery.Suppressed;
            _notificationIcon = LoadDragonIcon();
            if (_notificationIcon == 0) return JournalNotificationDelivery.Unavailable;
            _data = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = handle, Id = ++_nextId,
                Flags = 0x1 | 0x2 | 0x4 | 0x10 | 0x40,
                Callback = CallbackMessage, Icon = _notificationIcon, Tip = "Winnow journal",
                Info = "Your session finished. Select to add a note or rating.",
                Title = title.Length > 63 ? title[..60] + "…" : title,
                // NIIF_USER | NIIF_NOSOUND | NIIF_LARGE_ICON | NIIF_RESPECT_QUIET_TIME.
                InfoFlags = 0x4 | 0x10 | 0x20 | 0x80, BalloonIcon = _notificationIcon,
            };
            _activate = activate;
            _unavailable = unavailable;
            if (!Shell_NotifyIconW(0, ref _data)) { Dismiss(); return JournalNotificationDelivery.Failed; }
            // Windows can accept a request without showing it (for example, notifications
            // disabled in Settings). A missing SHOW callback therefore keeps the fallback.
            _deliveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _deliveryTimer.Tick += DeliveryTimedOut;
            _deliveryTimer.Start();
            return JournalNotificationDelivery.Submitted;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException or IOException)
        {
            Dismiss();
            return JournalNotificationDelivery.Unavailable;
        }
    }

    private void DeliveryTimedOut(object? sender, EventArgs e)
    {
        var fallback = _shown ? null : _unavailable;
        Dismiss();
        fallback?.Invoke();
    }

    private nint WindowMessage(nint window, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != CallbackMessage || (uint)wParam != _data.Id || _data.Window == 0) return 0;
        var notification = (uint)lParam;
        if (notification == 0x402) // NIN_BALLOONSHOW
        {
            _shown = true;
            _deliveryTimer?.Stop();
        }
        else if (notification == 0x405) // NIN_BALLOONUSERCLICK
        {
            var activate = _activate;
            Dismiss();
            Dispatcher.UIThread.Post(() => activate?.Invoke());
        }
        else if (notification is 0x403 or 0x404) // HIDE or TIMEOUT: no journal write.
        {
            var fallback = _shown ? null : _unavailable;
            Dismiss();
            if (fallback is not null) Dispatcher.UIThread.Post(fallback);
        }
        return 0;
    }

    public void Dismiss()
    {
        _deliveryTimer?.Stop();
        _deliveryTimer = null;
        if (_data.Window != 0 && OperatingSystem.IsWindows()) Shell_NotifyIconW(2, ref _data);
        if (_notificationIcon != 0)
        {
            DestroyIcon(_notificationIcon);
            _notificationIcon = 0;
        }
        _data = default;
        _activate = null;
        _unavailable = null;
        _shown = false;
    }

    private void WindowClosed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        Dismiss();
        if (_window is { } window)
        {
            window.Closed -= WindowClosed;
            Win32Properties.RemoveWndProcHookCallback(window, WindowMessage);
            _window = null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
    // Read the bundled ICO rather than the window icon, which can show the
    // selected game's cover. The notification always represents Winnow.
    internal static nint LoadDragonIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Winnow/Assets/Icons/dragon.ico"));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        using var reader = new BinaryReader(buffer);
        reader.ReadUInt16(); reader.ReadUInt16();
        var count = reader.ReadUInt16();
        var size = GetSystemMetrics(11); // SM_CXICON: custom balloons require a large icon.
        if (size <= 0) size = 32;
        var closest = int.MaxValue;
        uint length = 0, offset = 0;
        for (var i = 0; i < count; i++)
        {
            var width = (int)reader.ReadByte();
            if (width == 0) width = 256;
            reader.ReadBytes(7);
            var frameLength = reader.ReadUInt32();
            var frameOffset = reader.ReadUInt32();
            if (Math.Abs(width - size) >= closest) continue;
            closest = Math.Abs(width - size);
            length = frameLength; offset = frameOffset;
        }
        if (length == 0) return 0;
        buffer.Position = offset;
        var pixels = reader.ReadBytes(checked((int)length));
        return CreateIconFromResourceEx(pixels, (uint)pixels.Length, true, 0x00030000, size, size, 0);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern nint CreateIconFromResourceEx(byte[] bits, uint size,
        [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);
}
