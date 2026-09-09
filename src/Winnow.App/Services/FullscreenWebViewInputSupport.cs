using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Auth.WebView;

namespace Winnow.App.Services;

/// <summary>TV input for provider-owned browser windows, without reading credentials or weakening origin gates.</summary>
public sealed class FullscreenWebViewInputSupport : IWebViewInputSupport
{
    public Control Wrap(Window window, Control content, WebView2Host? browser = null, bool reading = false)
    {
        if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not MainWindow { IsFullscreen: true }) return content;
        window.MaxHeight = double.PositiveInfinity;
        window.MaxWidth = double.PositiveInfinity;
        window.SizeToContent = SizeToContent.Manual;
        window.CanResize = true;
        window.WindowState = WindowState.FullScreen;
        return new BrowserInputPanel(window, content, browser, reading);
    }

    public static (string Key, int VirtualKey, bool Shift)? Map(GamepadButtons buttons, bool reading)
    {
        if (buttons.HasFlag(GamepadButtons.Accept)) return ("Enter", 13, false);
        if (buttons.HasFlag(GamepadButtons.Play)) return (" ", 32, false);
        if (!reading && buttons.HasFlag(GamepadButtons.Search)) return ("Backspace", 8, false);
        if (buttons.HasFlag(GamepadButtons.PagePrevious)) return ("PageUp", 33, false);
        if (buttons.HasFlag(GamepadButtons.PageNext)) return ("PageDown", 34, false);
        if (buttons.HasFlag(GamepadButtons.Left)) return ("ArrowLeft", 37, false);
        if (buttons.HasFlag(GamepadButtons.Right)) return ("ArrowRight", 39, false);
        if (buttons.HasFlag(GamepadButtons.Up)) return reading ? ("ArrowUp", 38, false) : ("Tab", 9, true);
        if (buttons.HasFlag(GamepadButtons.Down)) return reading ? ("ArrowDown", 40, false) : ("Tab", 9, false);
        if (buttons.HasFlag(GamepadButtons.Previous)) return ("Tab", 9, true);
        if (buttons.HasFlag(GamepadButtons.Next)) return ("Tab", 9, false);
        return null;
    }

    private sealed class BrowserInputPanel : Grid, IDisposable
    {
        private readonly Window _window;
        private readonly WebView2Host? _browser;
        private readonly bool _reading;
        private readonly ContentControl _keyboardArea = new();
        private readonly TextBlock _status;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly GamepadInputFilter _filter = new();
        private IGamepadSource? _source;
        private GamepadKeyboardView? _keyboard;
        private TextBox? _text;
        private bool _disposed;
        private bool _busy;
        private bool _sendingText;
        private int _consentIndex;
        private readonly Control _content;
        private readonly ScrollViewer? _consentScroll;

        public BrowserInputPanel(Window window, Control content, WebView2Host? browser, bool reading)
        {
            _window = window; _browser = browser; _reading = reading; _content = content;
            RowDefinitions = new RowDefinitions("*,Auto,Auto");
            this[!BackgroundProperty] = new DynamicResourceExtension("Ground");
            if (browser is null)
            {
                content.MaxWidth = 1200;
                _consentScroll = new ScrollViewer { Content = content };
                Children.Add(_consentScroll);
            }
            else Children.Add(content);
            Grid.SetRow(_keyboardArea, 1); Children.Add(_keyboardArea);
            _status = FullscreenUi.Text(browser is null ? "← / →  Choose     A  Select     LT / RT  Read     B  Cancel" : reading ? "↑ ↓  Scroll     LB / RB  Links     A  Open     B  Close" : "↑ ↓  Field     A  Select     X  Check     Y  Type     View  Backspace     B Cancel", 24);
            _status.Margin = new Thickness(48, 16); Grid.SetRow(_status, 2); Children.Add(_status);
            _timer.Tick += Tick;
            if (browser is not null) browser.ControllerNavigationStarted += OnNavigation;
            AttachedToVisualTree += async (_, _) =>
            {
                if (_disposed) return;
                _source = GamepadSource.Create(); _timer.Start();
                if (_browser is not null)
                {
                    try { await _browser.SetControllerZoomAsync(1.5); }
                    catch (Exception) { if (!_disposed) _status.Text = "Browser input is unavailable. B closes this window."; }
                }
                else
                {
                    foreach (var text in content.GetVisualDescendants().OfType<TextBlock>()) text.FontSize = Math.Max(28, text.FontSize);
                    foreach (var panel in content.GetVisualDescendants().OfType<Panel>()) if (!double.IsPositiveInfinity(panel.MaxWidth)) panel.MaxWidth = 1100;
                    foreach (var button in content.GetVisualDescendants().OfType<Button>()) { button.FontSize = 28; button.MinHeight = 64; }
                    FocusConsent(0);
                }
            };
            DetachedFromVisualTree += (_, _) => Dispose();
            window.Closed += Closed;
            window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        }

        private void OnKey(object? sender, KeyEventArgs e)
        {
            if (_keyboard is not null && e.Key == Key.Escape) { CancelText(); e.Handled = true; }
        }

        private async void Tick(object? sender, EventArgs e)
        {
            if (_disposed || _busy || _sendingText) return;
            var snapshot = _source?.Poll();
            var buttons = _filter.Update(snapshot, _window.IsActive && _window.IsVisible, _clock.Elapsed);
            if (buttons == GamepadButtons.None) return;
            _busy = true;
            try { await HandleAsync(buttons); }
            catch (Exception) { if (!_disposed) _status.Text = "Couldn't send this input. Try again, or press B to close."; }
            finally { _busy = false; }
        }

        private async Task HandleAsync(GamepadButtons buttons)
        {
            if (_keyboard is not null)
            {
                if (buttons.HasFlag(GamepadButtons.Back)) { CancelText(); return; }
                _keyboard.Handle(buttons); return;
            }
            if (buttons.HasFlag(GamepadButtons.Back)) { _window.Close(); return; }
            if (_browser is null)
            {
                if (_consentScroll is not null && (buttons.HasFlag(GamepadButtons.PagePrevious) || buttons.HasFlag(GamepadButtons.PageNext)))
                {
                    _consentScroll.Offset = new Vector(0, Math.Max(0, _consentScroll.Offset.Y + (buttons.HasFlag(GamepadButtons.PagePrevious) ? -400 : 400)));
                    return;
                }
                var choices = _content.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled).ToArray();
                if (choices.Length == 0) return;
                if (buttons.HasFlag(GamepadButtons.Accept)) choices[Math.Clamp(_consentIndex, 0, choices.Length - 1)].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else FocusConsent(buttons.HasFlag(GamepadButtons.Up) || buttons.HasFlag(GamepadButtons.Left) ? -1 : 1);
                return;
            }
            if (!_browser.Ready.IsCompletedSuccessfully) { _status.Text = "Starting the browser… B cancels."; return; }
            if (!_reading && buttons.HasFlag(GamepadButtons.Keyboard)) { OpenText(); return; }
            if (Map(buttons, _reading) is { } key) await _browser.SendControllerKeyAsync(key.Key, key.VirtualKey, key.Shift);
        }

        private void FocusConsent(int delta)
        {
            var choices = _content.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled).ToArray();
            if (choices.Length == 0) return;
            _consentIndex = Math.Clamp(_consentIndex + delta, 0, choices.Length - 1);
            choices[_consentIndex].Focus(NavigationMethod.Directional); choices[_consentIndex].BringIntoView();
        }

        private void OpenText()
        {
            // Always mask the local composer. The browser's existing value, type and DOM are never inspected.
            _text = new TextBox { PasswordChar = '●', Text = "" };
            _keyboard = new GamepadKeyboardView(_text, television: true);
            _keyboard.Closed += TextDone;
            _keyboardArea.Content = _keyboard;
            _status.Text = "Type into the focused browser field. Done inserts text; B discards it.";
        }

        private async void TextDone(object? sender, EventArgs e)
        {
            var text = _text?.Text ?? "";
            CancelText();
            if (_disposed || _browser is null || text.Length == 0) return;
            _sendingText = true;
            try { await _browser.InsertControllerTextAsync(text); }
            catch (Exception) { if (!_disposed) _status.Text = "Couldn't insert text. Focus a field and try again."; }
            finally { _sendingText = false; }
        }

        private void CancelText()
        {
            if (_keyboard is not null) { _keyboard.Closed -= TextDone; _keyboard.Close(); }
            _keyboard = null; _keyboardArea.Content = null;
            if (_text is not null) _text.Text = "";
            _text = null;
            _status.Text = "↑ ↓  Field     A  Select     X  Check     Y  Type     View  Backspace     B  Cancel";
        }

        private void Closed(object? sender, EventArgs e) => Dispose();
        private void OnNavigation(object? sender, EventArgs e)
        {
            if (_keyboard is null) return;
            CancelText(); _status.Text = "The page changed. Choose a field before entering text.";
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            CancelText(); _timer.Stop(); _source?.Dispose(); _source = null;
            if (_browser is not null) _browser.ControllerNavigationStarted -= OnNavigation;
            _window.Closed -= Closed; _window.RemoveHandler(InputElement.KeyDownEvent, OnKey);
        }
    }
}
