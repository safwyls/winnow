using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Winnow.App.Services;

namespace Winnow.App.Views;

/// <summary>Controller text entry into the original field, including its binding and constraints.</summary>
public sealed class GamepadKeyboardView : Border
{
    private sealed record Keycap(string Label, string Shifted = "", double Width = 1, bool Action = false);
    private static Keycap[] Characters(string normal, string shifted) =>
        normal.Select((c, i) => new Keycap(c.ToString(), shifted[i].ToString())).ToArray();
    private static readonly Keycap[][] Rows =
    [
        [.. Characters("1234567890-=", "!@#$%^&*()_+"), new("Backspace", Width: 2.5, Action: true)],
        [.. Characters("qwertyuiop[]\\", "QWERTYUIOP{}|"), new("Delete", Width: 1.5, Action: true)],
        [new("Case", Width: 1.75, Action: true), .. Characters("asdfghjkl;'", "ASDFGHJKL:\""), new("Enter", Width: 1.75, Action: true)],
        [new("`", "~", Width: 1.75), .. Characters("zxcvbnm,./", "ZXCVBNM<>?"), new("", Width: .75), new("Up", Action: true), new("", Width: 1)],
        [new("Done", Width: 2, Action: true), new("Space", Width: 9.5, Action: true), new("Left", Action: true), new("Down", Action: true), new("Right", Action: true)]
    ];
    private readonly TextBox _target;
    private readonly List<List<Button>> _keys = [];
    private int _row;
    private int _column;
    private bool _uppercase;
    private bool _closed;
    private bool _observing;
    private char _mask;
    private readonly TextBlock _preview;

    public event EventHandler? Closed;

    public GamepadKeyboardView(TextBox target, bool television = false)
    {
        _target = target;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Width = television ? 1500 : 920;
        MaxWidth = television ? 1500 : 920;
        Margin = new Thickness(16);
        Padding = new Thickness(16);
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        this[!BackgroundProperty] = new DynamicResourceExtension("Surface");
        this[!BorderBrushProperty] = new DynamicResourceExtension("Line");
        var stack = new StackPanel { Spacing = 6 };
        var heading = new TextBlock { Text = "D-pad Move · A Type · X Backspace · RT Enter · B Close", Margin = new Thickness(0, 0, 0, 6) };
        if (television) heading.FontSize = 28;
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Text");
        heading[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("BodyFont");
        stack.Children.Add(television
            ? Fullscreen.FullscreenGlyphs.Hints("Dpad  Move    A  Type    X  Backspace    RT  Enter    B  Close") : heading);
        _preview = new TextBlock { Name = "GamepadTextPreview", MaxHeight = 48, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        if (television) { _preview.FontSize = 28; _preview.MaxHeight = 80; }
        _preview[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Text");
        _preview[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("BodyFont");
        stack.Children.Add(_preview);
        UpdatePreview();
        AttachedToVisualTree += (_, _) =>
        {
            if (_closed || _observing) return;
            _target.PropertyChanged += TargetChanged;
            _observing = true;
            UpdatePreview();
        };
        DetachedFromVisualTree += (_, _) => StopObserving();
        for (var row = 0; row < Rows.Length; row++)
        {
            var labels = Rows[row];
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', labels.Select(key => key.Width.ToString(CultureInfo.InvariantCulture) + "*"))) };
            var buttons = new List<Button>();
            for (var column = 0; column < labels.Length; column++)
            {
                var r = row;
                var c = column;
                var button = new Button
                {
                    Content = labels[column].Label, Focusable = false, MinHeight = 40,
                    IsVisible = labels[column].Label.Length > 0,
                    Padding = new Thickness(8, 4), Margin = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    BorderThickness = new Thickness(2)
                };
                button[!Button.FontFamilyProperty] = new DynamicResourceExtension("BodyFont");
                if (television) { button.FontSize = 28; button.MinHeight = 64; }
                button.Click += (_, _) => { _row = r; _column = c; Activate(); Refresh(); };
                Grid.SetColumn(button, column);
                grid.Children.Add(button);
                buttons.Add(button);
            }
            _keys.Add(buttons);
            stack.Children.Add(grid);
        }
        Child = stack;
        Refresh();
    }

    public bool Handle(GamepadButtons buttons)
    {
        if (_closed) return false;
        if ((buttons & GamepadButtons.Back) != 0) { Close(); return true; }
        if ((buttons & GamepadButtons.Play) != 0) { Delete(backward: true); return true; }
        if ((buttons & GamepadButtons.PageNext) != 0) { Enter(); return true; }
        if ((buttons & GamepadButtons.Up) != 0) MoveRow(-1);
        if ((buttons & GamepadButtons.Down) != 0) MoveRow(1);
        if ((buttons & GamepadButtons.Left) != 0) MoveColumn(-1);
        if ((buttons & GamepadButtons.Right) != 0) MoveColumn(1);
        if ((buttons & GamepadButtons.Accept) != 0) Activate();
        Refresh();
        return true;
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        StopObserving();
        Closed?.Invoke(this, EventArgs.Empty);
        _target.Focus(NavigationMethod.Tab);
    }

    private void TargetChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty || e.Property == TextBox.PasswordCharProperty) UpdatePreview();
    }

    private void UpdatePreview()
    {
        // Keep masking for the lifetime of this keyboard even if the field toggles reveal.
        if (_target.PasswordChar != default) _mask = _target.PasswordChar;
        var text = _target.Text ?? string.Empty;
        _preview.Text = _mask == default ? text : new string(_mask, new StringInfo(text).LengthInTextElements);
    }

    private void StopObserving()
    {
        if (!_observing) return;
        _target.PropertyChanged -= TargetChanged;
        _observing = false;
    }

    private void Activate()
    {
        if (_closed) return;
        var key = Rows[_row][_column];
        if (!key.Action) { Insert(_uppercase ? key.Shifted : key.Label); return; }
        switch (key.Label)
        {
            case "Case": _uppercase = !_uppercase; break;
            case "Space": Insert(" "); break;
            case "Backspace": Delete(backward: true); break;
            case "Delete": Delete(backward: false); break;
            case "Left": MoveCaret(backward: true); break;
            case "Right": MoveCaret(backward: false); break;
            case "Up": MoveCaretLine(-1); break;
            case "Down": MoveCaretLine(1); break;
            case "Enter": Enter(); break;
            case "Done": Close(); break;
        }
    }

    private void Enter()
    {
        if (_target.AcceptsReturn) Insert(Environment.NewLine);
        else
        {
            Close();
            _target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter });
            _target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyUpEvent, Key = Key.Enter });
        }
    }

    private static double Center(int row, int column) =>
        Rows[row].Take(column).Sum(key => key.Width) + Rows[row][column].Width / 2;

    private void MoveRow(int direction)
    {
        var center = Center(_row, _column);
        _row = (_row + direction + Rows.Length) % Rows.Length;
        _column = Enumerable.Range(0, Rows[_row].Length)
            .Where(column => Rows[_row][column].Label.Length > 0)
            .MinBy(column => Math.Abs(Center(_row, column) - center));
    }

    private void MoveColumn(int direction)
    {
        do { _column = (_column + direction + Rows[_row].Length) % Rows[_row].Length; }
        while (Rows[_row][_column].Label.Length == 0);
    }

    private void MoveCaretLine(int direction)
    {
        // Handle both CRLF and LF notes without leaving the caret inside a line break.
        var text = _target.Text ?? string.Empty;
        var start = _target.CaretIndex == 0 ? 0 : text.LastIndexOf('\n', _target.CaretIndex - 1) + 1;
        var column = _target.CaretIndex - start;
        int nextStart;
        if (direction < 0)
        {
            if (start == 0) return;
            nextStart = start < 2 ? 0 : text.LastIndexOf('\n', start - 2) + 1;
        }
        else
        {
            var end = text.IndexOf('\n', start);
            if (end < 0) return;
            nextStart = end + 1;
        }
        var nextEnd = text.IndexOf('\n', nextStart);
        if (nextEnd < 0) nextEnd = text.Length;
        if (nextEnd > nextStart && text[nextEnd - 1] == '\r') nextEnd--;
        var position = Math.Min(nextStart + column, nextEnd);
        position = StringInfo.ParseCombiningCharacters(text).Append(text.Length).Last(value => value <= position);
        _target.CaretIndex = position;
        _target.SelectionStart = _target.SelectionEnd = position;
    }

    private void Insert(string value)
    {
        if (_target.IsReadOnly || !_target.IsEffectivelyEnabled) return;
        var selectionLength = Math.Abs(_target.SelectionEnd - _target.SelectionStart);
        if (_target.MaxLength > 0 && (_target.Text?.Length ?? 0) - selectionLength + value.Length > _target.MaxLength) return;
        _target.SelectedText = value;
    }

    private void Delete(bool backward)
    {
        if (_target.IsReadOnly || !_target.IsEffectivelyEnabled) return;
        if (_target.SelectionStart == _target.SelectionEnd)
        {
            var caret = _target.CaretIndex;
            var adjacent = AdjacentCaret(backward);
            _target.SelectionStart = Math.Min(caret, adjacent);
            _target.SelectionEnd = Math.Max(caret, adjacent);
        }
        _target.SelectedText = string.Empty;
    }

    private int AdjacentCaret(bool backward)
    {
        var text = _target.Text ?? string.Empty;
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length);
        return backward
            ? boundaries.LastOrDefault(position => position < _target.CaretIndex)
            : boundaries.FirstOrDefault(position => position > _target.CaretIndex, text.Length);
    }

    private void MoveCaret(bool backward)
    {
        var position = _target.SelectionStart != _target.SelectionEnd
            ? backward ? Math.Min(_target.SelectionStart, _target.SelectionEnd) : Math.Max(_target.SelectionStart, _target.SelectionEnd)
            : AdjacentCaret(backward);
        _target.CaretIndex = position;
        _target.SelectionStart = _target.SelectionEnd = position;
    }

    private void Refresh()
    {
        for (var row = 0; row < _keys.Count; row++)
        for (var column = 0; column < _keys[row].Count; column++)
        {
            var button = _keys[row][column];
            var key = Rows[row][column];
            button.Content = key.Label switch
            {
                "Case" => _uppercase ? "Case: ABC" : "Case: abc",
                "Left" => "←", "Right" => "→", "Up" => "↑", "Down" => "↓",
                _ => key.Action ? key.Label : _uppercase ? key.Shifted : key.Label
            };
            Avalonia.Automation.AutomationProperties.SetName(button, key.Action ? key.Label : button.Content?.ToString());
            button[!Button.BorderBrushProperty] = new DynamicResourceExtension(row == _row && column == _column ? "Volt" : "Line");
        }
    }
}
