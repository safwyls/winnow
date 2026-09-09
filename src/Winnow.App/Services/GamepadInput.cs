namespace Winnow.App.Services;

[Flags]
public enum GamepadButtons
{
    None = 0, Up = 1, Down = 2, Left = 4, Right = 8,
    Accept = 16, Back = 32, Previous = 64, Next = 128, Menu = 256, Keyboard = 512,
    ScrollUp = 1024, ScrollDown = 2048,
    Play = 4096, Search = 8192, PagePrevious = 16384, PageNext = 32768
}

public readonly record struct GamepadSnapshot(GamepadButtons Buttons, string? BatteryStatus);

public interface IGamepadSource : IDisposable
{
    GamepadSnapshot? Poll();
}

/// <summary>Turns sampled state into actions, with repeat only for navigation.</summary>
public sealed class GamepadInputFilter
{
    private const GamepadButtons Directions = GamepadButtons.Up | GamepadButtons.Down |
        GamepadButtons.Left | GamepadButtons.Right | GamepadButtons.ScrollUp | GamepadButtons.ScrollDown;
    private GamepadButtons _previous;
    private GamepadButtons _suppressed;
    private bool _active;
    private TimeSpan _nextRepeat;

    public GamepadButtons Update(GamepadSnapshot? snapshot, bool isActive, TimeSpan elapsed)
    {
        if (!isActive || snapshot is null)
        {
            _active = false;
            _previous = GamepadButtons.None;
            return GamepadButtons.None;
        }

        var buttons = snapshot.Value.Buttons;
        if (!_active)
        {
            // A held launch/confirm button must not fire on reconnect or return from a game.
            _suppressed = buttons;
            _active = true;
        }
        _suppressed &= buttons;
        buttons &= ~_suppressed;
        var pressed = buttons & ~_previous;
        var directions = buttons & Directions;
        if (directions != (_previous & Directions))
            _nextRepeat = elapsed + TimeSpan.FromMilliseconds(400);
        else if (directions != GamepadButtons.None && elapsed >= _nextRepeat)
        {
            pressed |= directions;
            // Do not replay a backlog after a stalled UI frame.
            _nextRepeat = elapsed + TimeSpan.FromMilliseconds(110);
        }
        _previous = buttons;
        return pressed;
    }
}

internal static class GamepadMapping
{
    internal const int Deadzone = 12000;

    internal static GamepadButtons Stick(int x, int y)
    {
        if (Math.Max(Math.Abs(x), Math.Abs(y)) < Deadzone) return GamepadButtons.None;
        // One direction per sample makes diagonals predictable in a focus grid.
        return Math.Abs(x) > Math.Abs(y)
            ? (x < 0 ? GamepadButtons.Left : GamepadButtons.Right)
            : (y < 0 ? GamepadButtons.Down : GamepadButtons.Up);
    }

    internal static GamepadButtons Scroll(int y) => Math.Abs(y) < Deadzone
        ? GamepadButtons.None : y > 0 ? GamepadButtons.ScrollUp : GamepadButtons.ScrollDown;

    internal static GamepadButtons XInput(ushort buttons, short x, short y, short rightY = 0,
        byte leftTrigger = 0, byte rightTrigger = 0)
    {
        var result = (GamepadButtons)(buttons & 15);
        if ((buttons & 0x1000) != 0) result |= GamepadButtons.Accept;
        if ((buttons & 0x2000) != 0) result |= GamepadButtons.Back;
        if ((buttons & 0x4000) != 0) result |= GamepadButtons.Play;
        if ((buttons & 0x0020) != 0) result |= GamepadButtons.Search;
        if ((buttons & 0x0100) != 0) result |= GamepadButtons.Previous;
        if ((buttons & 0x0200) != 0) result |= GamepadButtons.Next;
        if ((buttons & 0x0010) != 0) result |= GamepadButtons.Menu;
        if ((buttons & 0x8000) != 0) result |= GamepadButtons.Keyboard;
        return result | Stick(x, y) | Scroll(rightY) | Triggers(leftTrigger, rightTrigger);
    }

    // XInput's documented threshold also applies after joydev's signed-axis normalization.
    internal static GamepadButtons Triggers(byte left, byte right) =>
        (left > 30 ? GamepadButtons.PagePrevious : GamepadButtons.None) |
        (right > 30 ? GamepadButtons.PageNext : GamepadButtons.None);

    internal static GamepadButtons LinuxTriggers(short? left, short? right) => Triggers(
        left is { } l ? (byte)(((int)l + 32768) * 255 / 65535) : (byte)0,
        right is { } r ? (byte)(((int)r + 32768) * 255 / 65535) : (byte)0);

    internal static GamepadButtons LinuxButton(ushort code) => code switch
    {
        0x130 => GamepadButtons.Accept, 0x131 => GamepadButtons.Back,
        0x133 => GamepadButtons.Keyboard, 0x134 => GamepadButtons.Play,
        0x13a => GamepadButtons.Search, 0x136 => GamepadButtons.Previous,
        0x137 => GamepadButtons.Next, 0x13b => GamepadButtons.Menu,
        0x138 => GamepadButtons.PagePrevious, 0x139 => GamepadButtons.PageNext,
        0x220 => GamepadButtons.Up, 0x221 => GamepadButtons.Down,
        0x222 => GamepadButtons.Left, 0x223 => GamepadButtons.Right,
        _ => GamepadButtons.None
    };
}
