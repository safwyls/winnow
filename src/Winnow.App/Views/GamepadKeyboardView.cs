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
    private static readonly string[] Rows = ["1234567890", "qwertyuiop", "asdfghjkl;", "zxcvbnm,./", "[]\\'`-="];
    private static readonly string[] ShiftRows = ["!@#$%^&*()", "QWERTYUIOP", "ASDFGHJKL:", "ZXCVBNM<>?", "{}|\"~_+"];
    private static readonly string[] Actions = ["Case", "Space", "Backspace", "Delete", "Left", "Right", "Enter", "Done"];
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

    public GamepadKeyboardView(TextBox target)
    {
        _target = target;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        MaxWidth = 920;
        Margin = new Thickness(16);
        Padding = new Thickness(16);
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        this[!BackgroundProperty] = new DynamicResourceExtension("Surface");
        this[!BorderBrushProperty] = new DynamicResourceExtension("Line");
        var stack = new StackPanel { Spacing = 6 };
        var heading = new TextBlock { Text = "Text entry · D-pad to move · A to type · B to close", Margin = new Thickness(0, 0, 0, 6) };
        heading[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Text");
        heading[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("BodyFont");
        stack.Children.Add(heading);
        _preview = new TextBlock { Name = "GamepadTextPreview", MaxHeight = 48, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
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
        for (var row = 0; row <= Rows.Length; row++)
        {
            var labels = row == Rows.Length ? Actions : Rows[row].Select(c => c.ToString()).ToArray();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', labels.Select(_ => "*"))) };
            var buttons = new List<Button>();
            for (var column = 0; column < labels.Length; column++)
            {
                var r = row;
                var c = column;
                var button = new Button
                {
                    Content = labels[column], Focusable = false, MinHeight = 40,
                    Padding = new Thickness(8, 4), Margin = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    BorderThickness = new Thickness(2)
                };
                button[!Button.FontFamilyProperty] = new DynamicResourceExtension("BodyFont");
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
        if ((buttons & GamepadButtons.Up) != 0) _row = (_row + _keys.Count - 1) % _keys.Count;
        if ((buttons & GamepadButtons.Down) != 0) _row = (_row + 1) % _keys.Count;
        _column = Math.Min(_column, _keys[_row].Count - 1);
        if ((buttons & GamepadButtons.Left) != 0) _column = (_column + _keys[_row].Count - 1) % _keys[_row].Count;
        if ((buttons & GamepadButtons.Right) != 0) _column = (_column + 1) % _keys[_row].Count;
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
        if (_row < Rows.Length) { Insert((_uppercase ? ShiftRows : Rows)[_row][_column].ToString()); return; }
        switch (Actions[_column])
        {
            case "Case": _uppercase = !_uppercase; break;
            case "Space": Insert(" "); break;
            case "Backspace": Delete(backward: true); break;
            case "Delete": Delete(backward: false); break;
            case "Left": MoveCaret(backward: true); break;
            case "Right": MoveCaret(backward: false); break;
            case "Enter":
                if (_target.AcceptsReturn) Insert(Environment.NewLine);
                else
                {
                    Close();
                    _target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter });
                    _target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyUpEvent, Key = Key.Enter });
                }
                break;
            case "Done": Close(); break;
        }
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
            if (row < Rows.Length) button.Content = (_uppercase ? ShiftRows : Rows)[row][column].ToString();
            else if (column == 0) button.Content = _uppercase ? "Case: ABC" : "Case: abc";
            button[!Button.BorderBrushProperty] = new DynamicResourceExtension(row == _row && column == _column ? "Volt" : "Line");
        }
    }
}
