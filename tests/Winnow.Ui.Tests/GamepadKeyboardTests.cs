using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    [AvaloniaFact]
    public void Controller_can_change_case_type_and_backspace_without_a_physical_keyboard()
    {
        var target = new TextBox();
        var keyboard = new GamepadKeyboardView(target);
        keyboard.Handle(GamepadButtons.Up); // Actions row, Case.
        keyboard.Handle(GamepadButtons.Accept);
        keyboard.Handle(GamepadButtons.Down);
        keyboard.Handle(GamepadButtons.Down); // Q row.
        keyboard.Handle(GamepadButtons.Accept);
        Assert.Equal("Q", target.Text);
        keyboard.Handle(GamepadButtons.Up);
        keyboard.Handle(GamepadButtons.Up);
        keyboard.Handle(GamepadButtons.Right);
        keyboard.Handle(GamepadButtons.Accept); // Space.
        Assert.Equal("Q ", target.Text);
        keyboard.Handle(GamepadButtons.Right);
        keyboard.Handle(GamepadButtons.Accept); // Backspace.
        Assert.Equal("Q", target.Text);
    }

    [AvaloniaFact]
    public void Backspace_deletes_a_complete_grapheme_and_read_only_fields_cannot_change()
    {
        var target = new TextBox { Text = "a👩‍💻", CaretIndex = "a👩‍💻".Length };
        var keyboard = new GamepadKeyboardView(target);
        keyboard.Handle(GamepadButtons.Up);
        keyboard.Handle(GamepadButtons.Right);
        keyboard.Handle(GamepadButtons.Right);
        keyboard.Handle(GamepadButtons.Accept);
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

    [AvaloniaFact]
    public void Enter_submits_single_line_field_after_closing_but_done_only_closes()
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
        var keyboard = new GamepadKeyboardView(target);
        keyboard.Closed += (_, _) => closed = true;
        keyboard.Handle(GamepadButtons.Up);
        keyboard.Handle(GamepadButtons.Left); // Done.
        keyboard.Handle(GamepadButtons.Left); // Enter.
        keyboard.Handle(GamepadButtons.Accept);
        Assert.True(closed);
        Assert.Equal(1, submissions);
        closed = false;
        keyboard = new GamepadKeyboardView(target);
        keyboard.Closed += (_, _) => closed = true;
        keyboard.Handle(GamepadButtons.Up);
        keyboard.Handle(GamepadButtons.Left);
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

    public sealed class TextValue
    {
        public string? Value { get; set; }
    }
}
