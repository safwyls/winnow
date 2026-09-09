using Winnow.App.Services;
using Avalonia.Headless.XUnit;
using Winnow.Auth.WebView;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenBrowserInputTests
{
    [AvaloniaFact]
    public async Task Input_lock_drops_controller_input_before_waiting_for_a_browser()
    {
        var host = new WebView2Host("unused-headless-profile");
        host.SetInputEnabled(false);
        var key = host.SendControllerKeyAsync("Enter", 13);
        var text = host.InsertControllerTextAsync("local draft");
        Assert.True(key.IsCompletedSuccessfully);
        Assert.True(text.IsCompletedSuccessfully);
        Assert.False(host.Ready.IsCompleted);
        await Task.WhenAll(key, text);
    }

    [Theory]
    [InlineData(GamepadButtons.Up, false, "Tab", 9, true)]
    [InlineData(GamepadButtons.Down, false, "Tab", 9, false)]
    [InlineData(GamepadButtons.Up, true, "ArrowUp", 38, false)]
    [InlineData(GamepadButtons.Down, true, "ArrowDown", 40, false)]
    [InlineData(GamepadButtons.Accept, false, "Enter", 13, false)]
    [InlineData(GamepadButtons.Play, false, " ", 32, false)]
    [InlineData(GamepadButtons.Search, false, "Backspace", 8, false)]
    [InlineData(GamepadButtons.PageNext, true, "PageDown", 34, false)]
    [InlineData(GamepadButtons.Previous, true, "Tab", 9, true)]
    public void Browser_navigation_keeps_reading_and_form_input_distinct(GamepadButtons button, bool reading, string key, int virtualKey, bool shift)
    {
        var mapped = FullscreenWebViewInputSupport.Map(button, reading);
        Assert.NotNull(mapped);
        Assert.Equal((key, virtualKey, shift), mapped.Value);
    }

    [Theory]
    [InlineData(GamepadButtons.Back)]
    [InlineData(GamepadButtons.Keyboard)]
    [InlineData(GamepadButtons.Menu)]
    public void Window_and_composer_actions_are_never_sent_to_the_provider(GamepadButtons button)
        => Assert.Null(FullscreenWebViewInputSupport.Map(button, false));
}
