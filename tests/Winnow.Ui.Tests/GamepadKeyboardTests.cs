using Avalonia.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class GamepadKeyboardTests
{
    [AvaloniaFact]
    public void Typing_replaces_selection_and_preserves_two_way_binding_and_length_limit()
    {
        var model = new TextValue { Value = "hello" };
        var target = new TextBox { MaxLength = 5 };
        target.Bind(TextBox.TextProperty, new Binding(nameof(TextValue.Value)) { Source = model, Mode = BindingMode.TwoWay });
        target.SelectionStart = 1;
        target.SelectionEnd = 4;
        var keyboard = new GamepadKeyboardView(target);
        keyboard.Handle(GamepadButtons.Accept);
        Assert.Equal("h1o", target.Text);
        Assert.Equal("h1o", model.Value);
        keyboard.Handle(GamepadButtons.Accept);
        keyboard.Handle(GamepadButtons.Accept);
        keyboard.Handle(GamepadButtons.Accept);
        Assert.Equal("h111o", model.Value);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Controller_can_change_case_type_and_backspace_without_a_physical_keyboard(bool television)
    {
        var target = new TextBox();
        var keyboard = new GamepadKeyboardView(target, television);
        keyboard.Handle(GamepadButtons.Down);
        keyboard.Handle(GamepadButtons.Down); // Case beside the home row.
        keyboard.Handle(GamepadButtons.Accept);
        keyboard.Handle(GamepadButtons.Up); // Q row.
        keyboard.Handle(GamepadButtons.Accept);
        Assert.Equal("Q", target.Text);
        keyboard.Handle(GamepadButtons.Up);
        keyboard.Handle(GamepadButtons.Up); // Done at the bottom left.
        keyboard.Handle(GamepadButtons.Right); // Wide space bar.
        keyboard.Handle(GamepadButtons.Accept);
        Assert.Equal("Q ", target.Text);
        keyboard.Handle(GamepadButtons.Play); // X backspaces from any key.
        Assert.Equal("Q", target.Text);
    }

    [AvaloniaFact]
    public void Backspace_deletes_a_complete_grapheme_and_read_only_fields_cannot_change()
    {
        var target = new TextBox { Text = "a👩‍💻", CaretIndex = "a👩‍💻".Length };
        var keyboard = new GamepadKeyboardView(target);
        keyboard.Handle(GamepadButtons.Play);
        Assert.Equal("a", target.Text);
        target.IsReadOnly = true;
        keyboard.Handle(GamepadButtons.Accept);
        keyboard.Handle(GamepadButtons.Down);
        keyboard.Handle(GamepadButtons.Accept);
        Assert.Equal("a", target.Text);
    }

    [AvaloniaFact]
    public void Back_closes_once_restores_focus_and_stops_editing()
    {
        var target = new TextBox();
        var keyboard = new GamepadKeyboardView(target);
        var window = new Window { Content = new StackPanel { Children = { target, keyboard } } };
        window.Show();
        try
        {
            var closed = 0;
            keyboard.Closed += (_, _) => closed++;
            Assert.True(keyboard.Handle(GamepadButtons.Back));
            Assert.True(target.IsFocused);
            keyboard.Close();
            Assert.False(keyboard.Handle(GamepadButtons.Accept));
            Assert.Equal(1, closed);
            Assert.True(string.IsNullOrEmpty(target.Text));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enter_submits_single_line_field_after_closing_but_done_only_closes(bool television)
    {
        var target = new TextBox();
        var window = new Window { Content = target };
        window.Show();
        try
        {
        var closed = false;
        var submissions = 0;
        target.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Assert.True(closed);
            submissions++;
        };
        var keyboard = new GamepadKeyboardView(target, television);
        keyboard.Closed += (_, _) => closed = true;
        keyboard.Handle(GamepadButtons.PageNext); // RT submits from any key.
        Assert.True(closed);
        Assert.Equal(1, submissions);
        closed = false;
        keyboard = new GamepadKeyboardView(target, television);
        keyboard.Closed += (_, _) => closed = true;
        keyboard.Handle(GamepadButtons.Up); // Done.
        keyboard.Handle(GamepadButtons.Accept);
        Assert.True(closed);
        Assert.Equal(1, submissions);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Secure_field_value_is_never_copied_into_keyboard_content()
    {
        var target = new TextBox { PasswordChar = '●', Text = "secret-token", CaretIndex = 12 };
        var keyboard = new GamepadKeyboardView(target);
        var window = new Window { Content = keyboard };
        window.Show();
        try
        {
            keyboard.Handle(GamepadButtons.Accept);
            Assert.Equal("secret-token1", target.Text);
            var preview = keyboard.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "GamepadTextPreview");
            Assert.Equal(new string('●', 13), preview.Text);
            Assert.DoesNotContain(keyboard.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("secret-token") == true);
            Assert.Empty(keyboard.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal('●', target.PasswordChar);
            target.PasswordChar = default;
            Assert.Equal(new string('●', 13), preview.Text);
            keyboard.Close();
            target.Text = "changed";
            Assert.Equal(new string('●', 13), preview.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Preview_tracks_external_edits_only_while_attached()
    {
        var target = new TextBox { Text = "Original" };
        var keyboard = new GamepadKeyboardView(target);
        var window = new Window { Content = keyboard };
        window.Show();
        try
        {
            var preview = keyboard.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "GamepadTextPreview");
            target.Text = "Updated note";
            Assert.Equal("Updated note", preview.Text);
            window.Content = null;
            target.Text = "Detached";
            Assert.Equal("Updated note", preview.Text);
            window.Content = keyboard;
            Assert.Equal("Detached", preview.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Multiline_enter_and_arrow_cluster_preserve_caret_and_edit_original_field(bool television)
    {
        var target = new TextBox { AcceptsReturn = true, Text = "hello", CaretIndex = 5 };
        var keyboard = new GamepadKeyboardView(target, television);
        var window = new Window { Content = keyboard };
        window.Show();
        try
        {
            var closed = false;
            keyboard.Closed += (_, _) => closed = true;
            keyboard.Handle(GamepadButtons.PageNext);
            keyboard.Handle(GamepadButtons.Accept);
            Assert.Equal("hello" + Environment.NewLine + "1", target.Text);
            Assert.False(closed);
            ClickKey(keyboard, "Up");
            Assert.Equal(1, target.CaretIndex);
            ClickKey(keyboard, "Right");
            Assert.Equal(2, target.CaretIndex);
            ClickKey(keyboard, "Delete");
            Assert.Equal("helo" + Environment.NewLine + "1", target.Text);
            ClickKey(keyboard, "Down");
            Assert.Equal(target.Text.Length, target.CaretIndex);
            keyboard.Handle(GamepadButtons.Play);
            Assert.Equal("helo" + Environment.NewLine, target.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Standard_keys_fit_and_arrows_form_an_inverted_T(bool television)
    {
        var keyboard = new GamepadKeyboardView(new TextBox { Text = "Search your library" }, television);
        var canvas = new Grid { Width = television ? 1920 : 1200, Height = television ? 1080 : 640, Children = { keyboard } };
        var window = new Window { Width = television ? 1280 : 1200, Height = television ? 720 : 640, Content = new Viewbox { Child = canvas } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var keyboardPosition = keyboard.TranslatePoint(default, canvas)!.Value;
            Assert.True(keyboardPosition.X >= 0 && keyboardPosition.X + keyboard.Bounds.Width <= canvas.Width);
            Assert.True(keyboardPosition.Y >= 0 && keyboardPosition.Y + keyboard.Bounds.Height <= canvas.Height);
            var up = FindKey(keyboard, "Up");
            var down = FindKey(keyboard, "Down");
            var left = FindKey(keyboard, "Left");
            var right = FindKey(keyboard, "Right");
            var upPosition = up.TranslatePoint(default, keyboard)!.Value;
            var downPosition = down.TranslatePoint(default, keyboard)!.Value;
            Assert.Equal(upPosition.X, downPosition.X, 2);
            Assert.True(upPosition.Y < downPosition.Y);
            Assert.True(left.TranslatePoint(default, keyboard)!.Value.X < downPosition.X);
            Assert.True(right.TranslatePoint(default, keyboard)!.Value.X > downPosition.X);
            Assert.True(FindKey(keyboard, "Space").Bounds.Width > up.Bounds.Width * 8);
            foreach (var button in keyboard.GetVisualDescendants().OfType<Button>().Where(button => button.IsVisible))
            {
                var position = button.TranslatePoint(default, keyboard)!.Value;
                Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= keyboard.Bounds.Width);
                Assert.True(position.Y >= 0 && position.Y + button.Bounds.Height <= keyboard.Bounds.Height);
            }
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, television ? "keyboard-tv-720p.png" : "keyboard-desktop.png"));
            }
        }
        finally { window.Close(); }
    }

    private static Button FindKey(GamepadKeyboardView keyboard, string name) =>
        keyboard.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == name);

    private static void ClickKey(GamepadKeyboardView keyboard, string name) =>
        FindKey(keyboard, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    public sealed class TextValue
    {
        public string? Value { get; set; }
    }
}
