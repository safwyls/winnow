using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class WindowsJumpListTests
{
    [Fact]
    public void Isolated_directories_have_distinct_stable_taskbar_identities()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-jump-list-identity");
        Assert.Equal(WindowsJumpList.GetAppId(directory), WindowsJumpList.GetAppId(directory + Path.DirectorySeparatorChar));
        Assert.NotEqual(WindowsJumpList.GetAppId(directory), WindowsJumpList.GetAppId(directory + "-other"));
        Assert.StartsWith("Winnow.Isolated.", WindowsJumpList.GetAppId(directory));
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.Equal("Winnow", WindowsJumpList.GetAppId(Path.Combine(local, "Winnow")));
        Assert.Equal("Winnow", WindowsJumpList.GetAppId(Path.Combine(local, "Hoard")));
    }

    [Theory]
    [InlineData("a b", "\"a b\"")]
    [InlineData("C:\\data\\", "\"C:\\data\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    public void Windows_arguments_preserve_spaces_quotes_and_trailing_backslashes(string input, string expected)
        => Assert.Equal(expected, WindowsJumpList.QuoteArgument(input));

    [Fact]
    public void Every_destination_carries_its_isolated_data_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Winnow jump list test");
        var arguments = WindowsJumpList.BuildArguments(directory, "--jump-list-game 42");
        Assert.Contains("--jump-list-game 42 --data-dir ", arguments);
        Assert.EndsWith(WindowsJumpList.QuoteArgument(Path.GetFullPath(directory)), arguments);
    }

    [Fact]
    public void Native_publish_smoke_uses_only_an_isolated_app_id()
    {
        // Shell integration is opt-in: ordinary test runs never alter the user's taskbar.
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("WINNOW_JUMP_LIST_SMOKE") != "1") return;
        var directory = Path.Combine(Path.GetTempPath(), "winnow-jump-list-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var bitmap = new SkiaSharp.SKBitmap(32, 32);
            bitmap.Erase(SkiaSharp.SKColors.Lime);
            var icon = Path.Combine(directory, "game.ico");
            File.WriteAllBytes(icon, JumpListIcons.Encode(bitmap));
            Assert.True(WindowsJumpList.Publish(directory, [new JumpListGame(42, "Jump List smoke game", icon)]));
            Assert.True(WindowsJumpList.Publish(directory, []));
        }
        finally
        {
            WindowsJumpList.Delete(directory);
            Directory.Delete(directory, true);
        }
    }
}
