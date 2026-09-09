using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using Winnow.App.Services;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Explicit focus rows belong to each TV page, including controls outside the viewport.</summary>
public abstract class FullscreenPage : UserControl, IDisposable
{
    protected FullscreenContext Context { get; }
    private Control[][] _rows = [];
    private Control? _focused;
    public virtual string Title => "Winnow";
    public virtual string Hints => "A  Select     B  Back";
    public event EventHandler? PageChanged;
    protected FullscreenPage(FullscreenContext context) { Context = context; }
    public virtual void Dispose() { GC.SuppressFinalize(this); }
    protected void Changed() => PageChanged?.Invoke(this, EventArgs.Empty);
    protected void SetFocusRows(params Control[][] rows)
    {
        foreach (var control in _rows.SelectMany(r => r).Distinct()) control.GotFocus -= TrackFocus;
        _rows = rows.Where(r => r.Length > 0).ToArray();
        foreach (var control in _rows.SelectMany(r => r).Distinct()) control.GotFocus += TrackFocus;
    }
    private void TrackFocus(object? sender, GotFocusEventArgs e) => _focused = sender as Control;
    protected void FocusControl(Control control)
    {
        _focused = control;
        if (!control.Focus(NavigationMethod.Directional))
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_focused, control) && control.IsEffectivelyVisible && TopLevel.GetTopLevel(control) is not null)
                { control.Focus(NavigationMethod.Directional); control.BringIntoView(); }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        control.BringIntoView();
    }
    public virtual void FocusInitial()
    {
        var target = _focused is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } && _rows.Any(r => r.Contains(_focused))
            ? _focused : _rows.SelectMany(r => r).FirstOrDefault(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
        if (target is not null) FocusControl(target);
    }
    public virtual bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.ScrollUp) || buttons.HasFlag(GamepadButtons.ScrollDown))
        {
            var scroll = this.GetVisualDescendants().OfType<ScrollViewer>()
                .Where(s => s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height)
                .OrderByDescending(s => s.Viewport.Width * s.Viewport.Height).FirstOrDefault();
            if (scroll is not null)
            {
                var delta = scroll.Viewport.Height * .25 * (buttons.HasFlag(GamepadButtons.ScrollUp) ? -1 : 1);
                scroll.Offset = new Vector(scroll.Offset.X, Math.Clamp(scroll.Offset.Y + delta, 0, scroll.Extent.Height - scroll.Viewport.Height));
            }
            return true;
        }
        if (_focused is null) FocusInitial();
        if (buttons.HasFlag(GamepadButtons.Accept))
        {
            if (_focused is TextBox text) Context.EditText(text);
            else if (_focused is Button button && button.IsEffectivelyEnabled)
            {
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                if (button.Command?.CanExecute(button.CommandParameter) == true) button.Command.Execute(button.CommandParameter);
            }
            return true;
        }
        var dr = buttons.HasFlag(GamepadButtons.Up) ? -1 : buttons.HasFlag(GamepadButtons.Down) ? 1 : 0;
        var dc = buttons.HasFlag(GamepadButtons.Left) ? -1 : buttons.HasFlag(GamepadButtons.Right) ? 1 : 0;
        if (dr == 0 && dc == 0) return false;
        var row = Array.FindIndex(_rows, r => r.Contains(_focused));
        if (row < 0) { FocusInitial(); return true; }
        var col = Array.IndexOf(_rows[row], _focused);
        var nr = row;
        var nc = col;
        while (true)
        {
            nr += dr; nc += dc;
            if (nr < 0 || nr >= _rows.Length) break;
            if (dr != 0) nc = Math.Min(col, _rows[nr].Length - 1);
            if (nc < 0 || nc >= _rows[nr].Length) break;
            var target = _rows[nr][nc];
            if (!target.IsEffectivelyEnabled || !target.IsEffectivelyVisible) continue;
            FocusControl(target); break;
        }
        return true;
    }
}

public sealed record FullscreenAction(string Label, Action Invoke, bool IsEnabled = true);

public static class FullscreenUi
{
    public static TextBlock Text(string text, double size = 28, string resource = "Text")
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(resource);
        block[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension(size >= 48 ? "DisplayFont" : "BodyFont");
        return block;
    }
    public static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, FontSize = 28, MinHeight = 64,
            Padding = new Thickness(24, 12), HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        button.Classes.Add("tv-action");
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }
    public static StackPanel Stack(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 16 };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
    public static ScrollViewer Scroll(Control child) => new() { Content = child,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
}
