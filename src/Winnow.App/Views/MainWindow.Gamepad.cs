using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;

namespace Winnow.App.Views;

public partial class MainWindow
{
    private IGamepadSource? _gamepadSource;
    private readonly GamepadInputFilter _gamepadFilter = new();
    private readonly Stopwatch _gamepadTime = Stopwatch.StartNew();
    private DispatcherTimer? _gamepadTimer;
    private GamepadKeyboardView? _gamepadKeyboard;

    private void InitializeGamepad()
    {
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (_gamepadKeyboard is { } keyboard && e.Key is Key.Escape or Key.Tab)
            {
                keyboard.Close();
                e.Handled = e.Key == Key.Escape;
            }
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            if (Avalonia.Controls.Design.IsDesignMode || _gamepadTimer is not null) return;
            _gamepadSource = GamepadSource.Create();
            _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _gamepadTimer.Tick += (_, _) => PollGamepad();
            _gamepadTimer.Start();
        };
        Closed += (_, _) =>
        {
            _gamepadTimer?.Stop();
            _gamepadSource?.Dispose();
            DisposeFullscreen();
        };
    }

    private void PollGamepad()
    {
        var snapshot = _gamepadSource?.Poll();
        UpdateGamepadStatus(snapshot is null ? null : snapshot.Value.BatteryStatus ?? "Controller connected");
        var buttons = _gamepadFilter.Update(snapshot,
            IsActive && IsVisible && WindowState != WindowState.Minimized, _gamepadTime.Elapsed);
        if (buttons != GamepadButtons.None) HandleGamepad(buttons);
    }

    // The platform filter owns edge/repeat detection; this entry point also lets
    // headless tests exercise exactly the dispatch used by a physical controller.
    internal void HandleGamepad(GamepadButtons buttons)
    {
        if (IsFullscreen && _tvView is { } television)
        {
            television.Handle(buttons);
            return;
        }
        if (buttons.HasFlag(GamepadButtons.Menu))
        {
            ToggleFullscreen();
            return;
        }
        if (_gamepadKeyboard is { } keyboard)
        {
            keyboard.Handle(buttons);
            return;
        }

        var scope = GamepadScope();
        var current = FocusManager?.GetFocusedElement() as Control;
        if (current is null || !current.IsEffectivelyVisible || (!ReferenceEquals(scope, this) &&
            (ReferenceEquals(current, scope) || !Within(current, scope))))
        {
            FocusFirst(scope);
            current = FocusManager?.GetFocusedElement() as Control;
        }

        if (buttons.HasFlag(GamepadButtons.Back))
        {
            SendGamepadKey(Key.Escape);
            return;
        }
        if (buttons.HasFlag(GamepadButtons.Keyboard) ||
            (buttons.HasFlag(GamepadButtons.Accept) && current is TextBox { IsReadOnly: false }))
        {
            if (current is TextBox { IsReadOnly: false } textBox) OpenGamepadKeyboard(textBox);
            return;
        }
        if (buttons.HasFlag(GamepadButtons.Previous) || buttons.HasFlag(GamepadButtons.Next))
        {
            MoveGamepadTab(scope, buttons.HasFlag(GamepadButtons.Previous));
            return;
        }
        if (buttons.HasFlag(GamepadButtons.Accept))
        {
            SendGamepadKey(current is ToggleButton ? Key.Space : Key.Enter);
            return;
        }
        if (buttons.HasFlag(GamepadButtons.ScrollUp) || buttons.HasFlag(GamepadButtons.ScrollDown))
        {
            var scroll = current?.GetVisualAncestors().OfType<ScrollViewer>()
                .FirstOrDefault(s => s.Extent.Height > s.Viewport.Height)
                ?? scope.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(s => s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height)
                    .OrderByDescending(s => s.Viewport.Width * s.Viewport.Height).FirstOrDefault();
            if (scroll is not null)
            {
                var delta = scroll.Viewport.Height * 0.25 * (buttons.HasFlag(GamepadButtons.ScrollUp) ? -1 : 1);
                scroll.Offset = new Vector(scroll.Offset.X,
                    Math.Clamp(scroll.Offset.Y + delta, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
            }
            return;
        }

        var key = buttons.HasFlag(GamepadButtons.Up) ? Key.Up
            : buttons.HasFlag(GamepadButtons.Down) ? Key.Down
            : buttons.HasFlag(GamepadButtons.Left) ? Key.Left
            : buttons.HasFlag(GamepadButtons.Right) ? Key.Right : Key.None;
        if (key == Key.None) return;

        // Preserve native value editing and the virtualized library's index-based
        // walk: a visual-tree walk alone cannot reach an unrealized game.
        if (current is Slider or ComboBox or ListBox or ListBoxItem or MenuItem ||
            _library?.Lightbox.IsOpen == true ||
            ReferenceEquals(scope, this) && (ReferenceEquals(current, this) ||
                current?.GetVisualAncestors().Any(v => v is GameTileView or FeedCardView) == true))
        {
            SendGamepadKey(key);
            return;
        }
        MoveGamepadSpatial(scope, current, key);
    }

    private Control GamepadScope()
    {
        var focused = FocusManager?.GetFocusedElement() as Control;
        if (focused is not null && TopLevel.GetTopLevel(focused) is { } popup && popup != this)
            return popup;
        var prompt = this.GetVisualDescendants().OfType<FeedListPromptView>()
            .LastOrDefault(v => v.IsEffectivelyVisible);
        if (prompt is not null) return prompt;
        if (_library?.Lightbox.IsOpen == true && LightboxPanel.Pane is Control lightbox) return lightbox;
        if (_library?.IsDetailsOpen == true && DetailsView is { } details) return details;
        return this;
    }

    private static bool Within(Control control, Control scope)
        => ReferenceEquals(control, scope) || control.GetVisualAncestors().Contains(scope);

    private static Control[] GamepadTargets(Control scope)
        => scope.GetVisualDescendants().OfType<Control>()
            .Where(c => c.Focusable && c.IsTabStop && c.IsEffectivelyVisible && c.IsEffectivelyEnabled
                && c.Bounds.Width > 0 && c.Bounds.Height > 0).ToArray();

    private static void FocusGamepad(Control target)
    {
        target.Focus(NavigationMethod.Tab);
        target.BringIntoView();
    }

    private static void FocusFirst(Control scope)
    {
        if (GamepadTargets(scope).FirstOrDefault() is { } first) FocusGamepad(first);
    }

    private void MoveGamepadTab(Control scope, bool previous)
    {
        var current = FocusManager?.GetFocusedElement();
        var next = current is null ? null : KeyboardNavigationHandler.GetNext(current,
            previous ? NavigationDirection.Previous : NavigationDirection.Next) as Control;
        if (next is not null && Within(next, scope) && next.IsEffectivelyVisible)
        {
            FocusGamepad(next);
            return;
        }
        var targets = GamepadTargets(scope);
        if (targets.Length > 0) FocusGamepad(previous ? targets[^1] : targets[0]);
    }

    private void MoveGamepadSpatial(Control scope, Control? current, Key key)
    {
        if (current?.TranslatePoint(new Point(current.Bounds.Width / 2, current.Bounds.Height / 2), scope) is not { } origin)
        {
            FocusFirst(scope);
            return;
        }
        var horizontal = key is Key.Left or Key.Right;
        var sign = key is Key.Left or Key.Up ? -1 : 1;
        var next = GamepadTargets(scope).Where(c => c != current)
            .Select(c => (Control: c, Point: c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), scope)))
            .Where(item => item.Point.HasValue)
            .Select(item => (item.Control, Delta: item.Point!.Value - origin))
            .Where(item => (horizontal ? item.Delta.X : item.Delta.Y) * sign > 1)
            .OrderBy(item => Math.Abs(horizontal ? item.Delta.X : item.Delta.Y)
                + 3 * Math.Abs(horizontal ? item.Delta.Y : item.Delta.X))
            .Select(item => item.Control).FirstOrDefault();
        if (next is not null) FocusGamepad(next);
    }

    private void SendGamepadKey(Key key)
    {
        var target = FocusManager?.GetFocusedElement() as InputElement ?? this;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key });
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyUpEvent, Key = key });
    }

    private void OpenGamepadKeyboard(TextBox target)
    {
        var panel = ShellContent;
        var keyboard = new GamepadKeyboardView(target);
        var layer = new Grid { Background = Brushes.Transparent, ZIndex = 1000 };
        Grid.SetColumnSpan(layer, panel.ColumnDefinitions.Count);
        layer.Children.Add(keyboard);
        layer.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, layer))
            {
                keyboard.Close();
                e.Handled = true;
            }
        };
        _gamepadKeyboard = keyboard;
        keyboard.Closed += (_, _) =>
        {
            panel.Children.Remove(layer);
            _gamepadKeyboard = null;
        };
        panel.Children.Add(layer);
    }
}
