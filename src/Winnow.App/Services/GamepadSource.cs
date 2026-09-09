using System.Runtime.InteropServices;

namespace Winnow.App.Services;

public static class GamepadSource
{
    public static IGamepadSource Create() => OperatingSystem.IsWindows()
        ? new WindowsGamepadSource()
        : OperatingSystem.IsLinux() ? new LinuxGamepadSource() : new UnavailableGamepadSource();

    private sealed class UnavailableGamepadSource : IGamepadSource
    {
        public GamepadSnapshot? Poll() => null;
        public void Dispose() { }
    }
}

internal sealed class WindowsGamepadSource : IGamepadSource
{
    private nint _library;
    private readonly GetState? _getState;
    private readonly GetBattery? _getBattery;
    private uint? _controller;
    private long _nextScan;
    private long _nextBattery;
    private string? _battery;

    public WindowsGamepadSource()
    {
        // Restrict loading to Windows' system directory rather than the current directory.
        foreach (var name in new[] { "xinput1_4.dll", "xinput9_1_0.dll" })
        {
            if (!NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, name), out _library)) continue;
            if (NativeLibrary.TryGetExport(_library, "XInputGetState", out var state))
                _getState = Marshal.GetDelegateForFunctionPointer<GetState>(state);
            if (NativeLibrary.TryGetExport(_library, "XInputGetBatteryInformation", out var battery))
                _getBattery = Marshal.GetDelegateForFunctionPointer<GetBattery>(battery);
            break;
        }
    }

    public GamepadSnapshot? Poll()
    {
        if (_library == 0 || _getState is null) return null;
        if (_controller is { } selected)
        {
            if (_getState(selected, out var state) == 0) return Snapshot(selected, state);
            _controller = null;
            _battery = null;
            // Expose a disconnect frame so the filter suppresses held buttons on the next pad.
            return null;
        }
        if (Environment.TickCount64 < _nextScan) return null;
        _nextScan = Environment.TickCount64 + 2000;
        for (uint i = 0; i < 4; i++)
        {
            if (_getState(i, out var state) != 0) continue;
            _controller = i;
            _nextBattery = 0;
            return Snapshot(i, state);
        }
        return null;
    }

    private GamepadSnapshot Snapshot(uint index, State state)
    {
        if (Environment.TickCount64 >= _nextBattery)
        {
            _nextBattery = Environment.TickCount64 + 30000;
            _battery = _getBattery is not null && _getBattery(index, 0, out var battery) == 0
                ? BatteryLabel(battery.Type, battery.Level) : null;
        }
        return new(GamepadMapping.XInput(state.Buttons, state.LeftX, state.LeftY, state.RightY), _battery);
    }

    internal static string? BatteryLabel(byte type, byte level) => type switch
    {
        1 => "Wired controller",
        2 or 3 => level switch
        {
            0 => "Controller battery empty", 1 => "Controller battery low",
            2 => "Controller battery medium", 3 => "Controller battery full", _ => null
        },
        _ => null
    };

    public void Dispose()
    {
        if (_library == 0) return;
        NativeLibrary.Free(_library);
        _library = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint GetState(uint index, out State state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint GetBattery(uint index, byte deviceType, out Battery battery);
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Battery { public byte Type, Level; }
}

/// <summary>Reads the kernel joydev interface without grabbing the device or requiring SDL.</summary>
internal sealed class LinuxGamepadSource : IGamepadSource
{
    private int _fd = -1;
    private bool _disposed;
    private long _nextScan;
    private readonly byte[] _axes = new byte[64];
    private readonly byte[] _axisCount = new byte[1];
    private readonly byte[] _buttonMap = new byte[1024];
    private readonly short[] _axisValues = new short[64];
    private readonly bool[] _buttons = new bool[512];

    public GamepadSnapshot? Poll()
    {
        if (_disposed) return null;
        if (_fd < 0)
        {
            if (Environment.TickCount64 < _nextScan) return null;
            _nextScan = Environment.TickCount64 + 2000;
            // Bounded discovery avoids directory enumeration and permission failures on the UI timer.
            for (var index = 0; index < 16; index++)
            {
                _fd = Open($"/dev/input/js{index}", 0x800 | 0x80000); // O_RDONLY | O_NONBLOCK | O_CLOEXEC
                if (_fd < 0) continue;
                if (Ioctl(_fd, 0x80016a11, _axisCount) == 0 &&
                    Ioctl(_fd, 0x80406a32, _axes) == 0 && Ioctl(_fd, 0x84006a34, _buttonMap) == 0)
                    break;
                CloseDevice();
            }
            if (_fd < 0) return null;
        }
        // Bound draining so an unusually noisy device cannot monopolize the dispatcher.
        var drained = false;
        for (var i = 0; i < 256; i++)
        {
            var count = Read(_fd, out var input, 8);
            if (count == -1 && Marshal.GetLastPInvokeError() == 11) { drained = true; break; } // EAGAIN
            if (count == -1 && Marshal.GetLastPInvokeError() == 4) break; // EINTR
            if (count != 8) { CloseDevice(); return null; }
            var type = input.Type & 0x7f;
            if (type == 1) _buttons[input.Number] = input.Value != 0;
            else if (type == 2 && input.Number < Math.Min(_axisCount[0], _axisValues.Length))
                _axisValues[input.Number] = input.Value;
        }
        // Do not dispatch a partial initial state: held buttons may still be queued.
        if (!drained) return null;
        var result = GamepadButtons.None;
        for (var i = 0; i < _buttons.Length; i++)
            if (_buttons[i]) result |= GamepadMapping.LinuxButton(BitConverter.ToUInt16(_buttonMap, i * 2));
        int x = 0, y = 0, rightY = 0, hatX = 0, hatY = 0;
        for (var i = 0; i < Math.Min(_axisCount[0], _axes.Length); i++)
        {
            switch (_axes[i])
            {
                case 0: x = _axisValues[i]; break;
                case 1: y = -_axisValues[i]; break;
                case 4: rightY = -_axisValues[i]; break;
                case 16: hatX = _axisValues[i]; break;
                case 17: hatY = -_axisValues[i]; break;
            }
        }
        return new(result | GamepadMapping.Stick(x, y) | GamepadMapping.Stick(hatX, hatY) |
            GamepadMapping.Scroll(rightY), null);
    }

    private void CloseDevice()
    {
        if (_fd >= 0) Close(_fd);
        _fd = -1;
        Array.Clear(_axisValues);
        Array.Clear(_buttons);
    }

    public void Dispose() { CloseDevice(); _disposed = true; }

    [StructLayout(LayoutKind.Sequential)]
    private struct JoystickEvent { public uint Time; public short Value; public byte Type, Number; }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint Read(int fd, out JoystickEvent input, nuint length);
    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int Ioctl(int fd, nuint request, [Out] byte[] buffer);
    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int fd);
}
