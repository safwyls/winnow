using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class GamepadInputTests
{
    [Theory]
    [InlineData(0, GamepadButtons.None)]
    [InlineData(11999, GamepadButtons.None)]
    [InlineData(-11999, GamepadButtons.None)]
    [InlineData(12000, GamepadButtons.ScrollUp)]
    [InlineData(-32768, GamepadButtons.ScrollDown)]
    public void RightStickScrollUsesDeadzone(short y, GamepadButtons expected) =>
        Assert.Equal(expected, GamepadMapping.XInput(0, 0, 0, y));

    [Fact]
    public void HeldScrollRepeatsWithoutRepeatingAccept()
    {
        var filter = new GamepadInputFilter();
        filter.Update(new(GamepadButtons.None, null), true, TimeSpan.Zero);
        var held = new GamepadSnapshot(GamepadButtons.ScrollDown | GamepadButtons.Accept, null);
        Assert.Equal(held.Buttons, filter.Update(held, true, TimeSpan.FromMilliseconds(1)));
        Assert.Equal(GamepadButtons.None, filter.Update(held, true, TimeSpan.FromMilliseconds(400)));
        Assert.Equal(GamepadButtons.ScrollDown, filter.Update(held, true, TimeSpan.FromMilliseconds(401)));
        Assert.Equal(GamepadButtons.ScrollDown, filter.Update(held, true, TimeSpan.FromMilliseconds(511)));
    }

    [Fact]
    public void HeldAcceptDoesNotRepeatOrFireAfterFocusReturns()
    {
        var filter = new GamepadInputFilter();
        GamepadButtons Sample(GamepadButtons buttons, int ms, bool active = true) =>
            filter.Update(new(buttons, null), active, TimeSpan.FromMilliseconds(ms));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.None, 0));
        Assert.Equal(GamepadButtons.Accept, Sample(GamepadButtons.Accept, 1));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.Accept, 1000));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.Accept, 1001, false));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.Accept, 1002));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.Accept, 2000));
        Sample(GamepadButtons.None, 2001);
        Assert.Equal(GamepadButtons.Accept, Sample(GamepadButtons.Accept, 2002));
    }

    [Fact]
    public void DirectionsRepeatAfterDelayAndStallsDoNotQueueActions()
    {
        var filter = new GamepadInputFilter();
        GamepadButtons Sample(GamepadButtons buttons, int ms) =>
            filter.Update(new(buttons, null), true, TimeSpan.FromMilliseconds(ms));
        Sample(GamepadButtons.None, 0);
        Assert.Equal(GamepadButtons.Right, Sample(GamepadButtons.Right, 1));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.Right, 400));
        Assert.Equal(GamepadButtons.Right, Sample(GamepadButtons.Right, 401));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.Right, 510));
        Assert.Equal(GamepadButtons.Right, Sample(GamepadButtons.Right, 511));
        Assert.Equal(GamepadButtons.Right, Sample(GamepadButtons.Right, 10000));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.Right, 10001));
        Assert.Equal(GamepadButtons.Left, Sample(GamepadButtons.Left, 10002));
        Assert.Equal(GamepadButtons.None, Sample(GamepadButtons.Left, 10112));
    }

    [Fact]
    public void ReconnectRequiresHeldControlsToBeReleasedIndividually()
    {
        var filter = new GamepadInputFilter();
        filter.Update(null, true, TimeSpan.Zero);
        Assert.Equal(GamepadButtons.None, filter.Update(new(GamepadButtons.Accept, null), true, TimeSpan.Zero));
        Assert.Equal(GamepadButtons.Down, filter.Update(new(GamepadButtons.Accept | GamepadButtons.Down, null), true, TimeSpan.FromSeconds(1)));
        filter.Update(new(GamepadButtons.None, null), true, TimeSpan.FromSeconds(2));
        Assert.Equal(GamepadButtons.Accept, filter.Update(new(GamepadButtons.Accept, null), true, TimeSpan.FromSeconds(3)));
    }

    [Theory]
    [InlineData(0, 0, GamepadButtons.None)]
    [InlineData(11999, -11999, GamepadButtons.None)]
    [InlineData(12000, 0, GamepadButtons.Right)]
    [InlineData(-32768, 0, GamepadButtons.Left)]
    [InlineData(0, -32768, GamepadButtons.Down)]
    [InlineData(20000, 25000, GamepadButtons.Up)]
    public void StickHasDeadzoneAndChoosesDominantDirection(int x, int y, GamepadButtons expected) =>
        Assert.Equal(expected, GamepadMapping.Stick(x, y));

    [Fact]
    public void PlatformMappingsAgreeOnFaceAndShoulderActions()
    {
        Assert.Equal(GamepadButtons.Accept, GamepadMapping.XInput(0x1000, 0, 0));
        Assert.Equal(GamepadButtons.Back, GamepadMapping.XInput(0x2000, 0, 0));
        Assert.Equal(GamepadButtons.Keyboard, GamepadMapping.XInput(0x8000, 0, 0));
        Assert.Equal(GamepadButtons.Previous | GamepadButtons.Next, GamepadMapping.XInput(0x0300, 0, 0));
        Assert.Equal(GamepadButtons.Accept, GamepadMapping.LinuxButton(0x130));
        Assert.Equal(GamepadButtons.Back, GamepadMapping.LinuxButton(0x131));
        Assert.Equal(GamepadButtons.Keyboard, GamepadMapping.LinuxButton(0x133));
        Assert.Equal(GamepadButtons.Menu, GamepadMapping.LinuxButton(0x13b));
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(255, 3, null)]
    [InlineData(1, 0, "Wired controller")]
    [InlineData(2, 1, "Controller battery low")]
    [InlineData(3, 3, "Controller battery full")]
    public void UnknownBatteryIsAbsentRatherThanAnError(byte type, byte level, string? expected) =>
        Assert.Equal(expected, WindowsGamepadSource.BatteryLabel(type, level));
}
