using Avalonia;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>The TV shell owns its stack and never navigates the desktop shell.</summary>
public sealed class FullscreenView : UserControl, IDisposable
{
    private readonly FullscreenContext _context;
    private readonly List<FullscreenPage> _stack = [];
    private readonly FullscreenPage[] _roots;
    private readonly ContentControl _body = new();
    private readonly ContentControl _backdrop = new() { Name = "FullscreenPageBackdrop", IsHitTestVisible = false,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly StackPanel _brand = new() { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _navigation = new() { Name = "FullscreenRootNavigation", Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button _back;
    private readonly TextBlock _backLabel = FullscreenUi.Text("", 28);
    private readonly Panel _overlay = new();
    private readonly Grid _safe = new() { RowDefinitions = new RowDefinitions("80,*,64") };
    private readonly Grid _canvas = new() { Width = 1920, Height = 1080 };
    private readonly TextBlock _clock = FullscreenUi.Text("", 24);
    private readonly TextBlock _status = FullscreenUi.Text("Controller disconnected", 24, "TextDim");
    private readonly ContentControl _hints = new();
    private readonly ContentControl _rightHints = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _launch = FullscreenUi.Text("", 28);
    private readonly Button[] _tabs;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private GamepadKeyboardView? _keyboard;
    private int _section;
    private bool _disposed;
    private readonly ConditionalWeakTable<Control, TypeSize> _typeSizes = new();
    private sealed record TypeSize(double Value);
    public event Action? ExitRequested;
    public event Action? QuitRequested;
    public FullscreenPage CurrentPage => _stack.Count > 0 ? _stack[^1] : _roots[_section];

    public FullscreenView(FullscreenContext context)
    {
        _context = context;
        _body.LayoutUpdated += (_, _) => ApplyTextSize();
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-action"))
        {
            Setters = {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("Text")),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 3)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<Button>((button, _) =>
                {
                    var border = new Border();
                    border.Bind(Border.BackgroundProperty, new Binding(nameof(Button.Background)) { Source = button });
                    border.Bind(Border.BorderBrushProperty, new Binding(nameof(Button.BorderBrush)) { Source = button });
                    border.Bind(Border.BorderThicknessProperty, new Binding(nameof(Button.BorderThickness)) { Source = button });
                    border.Bind(Border.PaddingProperty, new Binding(nameof(Button.Padding)) { Source = button });
                    var presenter = new ContentPresenter { VerticalContentAlignment = VerticalAlignment.Center };
                    presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Button.Content)) { Source = button });
                    border.Child = presenter; return border;
                }))
            }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-action").Class(":pointerover"))
        { Setters = { new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("TextDim")) } });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-action").Class("current"))
        { Setters = { new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("Text")), new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("TextDim")) } });
        Styles.Add(new Style(s => s.OfType<Button>().Class("tv-action").Class(":focus"))
        { Setters = { new Setter(TemplatedControl.BorderBrushProperty, new DynamicResourceExtension("Volt")), new Setter(TemplatedControl.ForegroundProperty, new DynamicResourceExtension("Volt")) } });
        _roots = [new FullscreenBrowsePage(context, true), new FullscreenBrowsePage(context, false), new FullscreenActivityPage(context), new FullscreenSettingsPage(context)];
        _tabs = new[] { "For you", "Library", "Activity", "Settings" }.Select((label, index) => FullscreenUi.Button(label, () => SelectSection(index))).ToArray();
        foreach (var tab in _tabs)
        {
            var label = FullscreenUi.Text(tab.Content?.ToString() ?? "", 28);
            tab.Content = new Border { Child = label, Padding = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(0, 0, 0, 3) };
            tab.Background = Brushes.Transparent;
            tab.Padding = new Thickness(12, 8);
        }
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var brand = FullscreenUi.Text("WINNOW", 32);
        brand[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("DisplayFont");
        brand.FontWeight = FontWeight.Bold;
        _clock[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("DataFont");
        _brand.Children.Add(FullscreenGlyphs.Icon("Winnow", 42));
        _brand.Children.Add(brand);
        header.Children.Add(_brand);
        _navigation.Children.Add(FullscreenGlyphs.Icon("LB"));
        foreach (var tab in _tabs) _navigation.Children.Add(tab);
        _navigation.Children.Add(FullscreenGlyphs.Icon("RB"));
        Grid.SetColumn(_navigation, 1); header.Children.Add(_navigation);
        _back = FullscreenUi.Button("Back", Back);
        _back.Name = "FullscreenBack";
        _back.Padding = new Thickness(0, 8);
        _back.HorizontalAlignment = HorizontalAlignment.Left;
        _backLabel.MaxWidth = 700;
        _backLabel.TextWrapping = TextWrapping.NoWrap;
        _backLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        _backLabel.VerticalAlignment = VerticalAlignment.Center;
        var backContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        backContent.Children.Add(FullscreenGlyphs.Icon("B", 40));
        backContent.Children.Add(_backLabel);
        _back.Content = backContent;
        header.Children.Add(_back);
        _status.Margin = new Thickness(16, 0, 24, 0);
        Grid.SetColumn(_status, 2); header.Children.Add(_status);
        Grid.SetColumn(_clock, 3); header.Children.Add(_clock);
        _safe.Children.Add(header); Grid.SetRow(_body, 1); _safe.Children.Add(_body);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), VerticalAlignment = VerticalAlignment.Bottom };
        footer.Children.Add(_hints);
        Grid.SetColumn(_rightHints, 1); footer.Children.Add(_rightHints);
        Grid.SetRow(footer, 2); _safe.Children.Add(footer);
        _canvas.Children.Add(_backdrop); _canvas.Children.Add(_safe); _canvas.Children.Add(_overlay);
        _launch.HorizontalAlignment = HorizontalAlignment.Center;
        _launch.VerticalAlignment = VerticalAlignment.Top;
        _launch.Margin = new Thickness(0, 125, 0, 0);
        _launch.IsHitTestVisible = false;
        _canvas.Children.Add(_launch);
        _canvas[!Panel.BackgroundProperty] = new DynamicResourceExtension("Ground");
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas };
        SizeChanged += (_, _) => FitCanvas();
        Background = Brushes.Black;
        context.PageRequested += Push;
        context.BackRequested += Back;
        context.TextRequested += EditText;
        context.Notice += Notice;
        context.PreferencesChanged += Preferences;
        context.DetailsChanged += ReplaceDetails;
        context.Shared.Library.Journal.PropertyChanged += JournalChanged;
        context.Shared.Library.LaunchStatus.PropertyChanged += LaunchChanged;
        context.Library.LaunchStatus.PropertyChanged += LaunchChanged;
        context.FilePicker = PickFile;
        context.SaveFilePicker = SaveFile;
        _timer.Tick += (_, _) => _clock.Text = DateTime.Now.ToString("t");
        AttachedToVisualTree += (_, _) => { _clock.Text = DateTime.Now.ToString("t"); _timer.Start(); _context.SetActive(true); FocusPage(); JournalChanged(this, new PropertyChangedEventArgs(null)); LaunchChanged(this, new PropertyChangedEventArgs(null)); };
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); _context.SetActive(false); };
        Preferences(this, EventArgs.Empty);
        ShowPage();
    }
    private void Preferences(object? sender, EventArgs e)
    {
        var theme = _context.Themes.FirstOrDefault(t => t.Id == _context.ThemeId) ?? _context.Themes.First();
        foreach (var (key, color) in theme.Tokens(0)) Resources[key] = new SolidColorBrush(color);
        FitCanvas();
        ApplyTextSize();
    }
    private void FitCanvas()
    {
        // Keep pixel proportions and the TV type scale while opening horizontal space on wide displays.
        _canvas.Width = _context.FitUltrawide && Bounds.Height > 0
            ? Math.Max(1920, 1080 * Bounds.Width / Bounds.Height) : 1920;
        _safe.Margin = new Thickness(_canvas.Width * _context.SafeMarginPercent / 100, 1080 * _context.SafeMarginPercent / 100);
    }
    private void ApplyTextSize()
    {
        // Typography grows inside the page; stable chrome and safe margins keep navigation reachable.
        foreach (var control in _body.GetVisualDescendants().OfType<Control>())
        {
            if (control is TextBlock block && block.IsSet(TextBlock.FontSizeProperty))
            {
                var original = _typeSizes.GetValue(block, c => new(((TextBlock)c).FontSize)).Value;
                block.FontSize = original >= 48 ? original : original * _context.TextScale;
            }
            else if (control is TemplatedControl templated && templated.IsSet(TemplatedControl.FontSizeProperty))
                templated.FontSize = _typeSizes.GetValue(templated, c => new(((TemplatedControl)c).FontSize)).Value * _context.TextScale;
        }
    }
    private void Push(FullscreenPage page)
    {
        if (_disposed) { page.Dispose(); return; }
        _keyboard?.Close();
        if (page is FullscreenDetailsPage)
        {
            var previous = _stack.FindIndex(item => item is FullscreenDetailsPage);
            if (previous >= 0)
            {
                foreach (var old in _stack.Skip(previous)) old.Dispose();
                _stack.RemoveRange(previous, _stack.Count - previous);
            }
        }
        _stack.Add(page); ShowPage();
    }
    private void ReplaceDetails(GameDetailsViewModel? details)
    {
        var index = _stack.FindIndex(page => page is FullscreenDetailsPage);
        if (index < 0) return;
        if (details is null)
        {
            _keyboard?.Close();
            foreach (var page in _stack.Skip(index)) page.Dispose();
            _stack.RemoveRange(index, _stack.Count - index);
            ShowPage();
            return;
        }
        var previous = (FullscreenDetailsPage)_stack[index];
        _stack[index] = new FullscreenDetailsPage(_context, details, previous.SelectedSection);
        previous.Dispose();
        // Text-editor pages keep their unsaved drafts. The underlying detail is fresh when Back returns.
        // A screenshot belongs to the outgoing detail lease and cannot survive its disposal.
        var screenshot = _stack.FindIndex(index + 1, page => page is FullscreenDetailsScreenshotPage);
        if (screenshot >= 0)
        {
            foreach (var page in _stack.Skip(screenshot)) page.Dispose();
            _stack.RemoveRange(screenshot, _stack.Count - screenshot);
        }
        ShowPage();
    }
    private void JournalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is null || !IsEffectivelyVisible) return;
        var prompt = _context.Shared.Library.Journal;
        var existing = _stack.FindIndex(page => page is FullscreenSessionJournalPage);
        if (prompt.IsOpen && existing < 0)
            Push(new FullscreenSessionJournalPage(_context, prompt));
        else if (!prompt.IsOpen && existing >= 0)
        {
            _keyboard?.Close();
            foreach (var page in _stack.Skip(existing)) page.Dispose();
            _stack.RemoveRange(existing, _stack.Count - existing);
            ShowPage();
        }
    }
    public void SetActive(bool active)
    {
        _context.SetActive(active);
        if (active) JournalChanged(this, new PropertyChangedEventArgs(null));
    }
    private void LaunchChanged(object? sender, PropertyChangedEventArgs e)
    {
        var status = _context.Library.LaunchStatus.IsOpen ? _context.Library.LaunchStatus : _context.Shared.Library.LaunchStatus;
        _launch.Text = status.IsOpen ? status.Message : string.Empty;
        _launch.IsVisible = status.IsOpen;
        _launch[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(status.IsProblem ? "Amber" : "Text");
    }
    public void Back()
    {
        if (_keyboard is { } keyboard) { keyboard.Close(); return; }
        if (_stack.Count == 0) { QuickMenu(); return; }
        var page = _stack[^1]; _stack.RemoveAt(_stack.Count - 1); page.Dispose();
        if (page is FullscreenDetailsPage) _context.Library.CloseDetailsCommand.Execute(null);
        ShowPage();
    }
    private void SelectSection(int section)
    {
        foreach (var page in _stack) page.Dispose(); _stack.Clear();
        _context.Library.CloseDetailsCommand.Execute(null);
        _section = (section + _roots.Length) % _roots.Length; ShowPage();
    }
    private void ShowPage()
    {
        if (_body.Content is FullscreenPage old) old.PageChanged -= PageChanged;
        var page = CurrentPage; _body.Content = page; page.PageChanged += PageChanged;
        _backdrop.Content = page.Backdrop;
        var details = page is FullscreenDetailsPage;
        _brand.IsVisible = _navigation.IsVisible = !details;
        _back.IsVisible = details;
        _backLabel.Text = _stack.Count > 1 ? _stack[^2].Title : _roots[_section].Title;
        Avalonia.Automation.AutomationProperties.SetName(_back, $"Back to {_backLabel.Text}");
        for (var i = 0; i < _tabs.Length; i++)
        {
            _tabs[i].Opacity = i == _section ? 1 : .7;
            _tabs[i].IsEnabled = _stack.Count == 0;
            if (_tabs[i].Content is Border underline)
            {
                underline[!Border.BorderBrushProperty] = new DynamicResourceExtension("Volt");
                underline.BorderThickness = new Thickness(0, 0, 0, i == _section ? 3 : 0);
                underline.Padding = new Thickness(0, 0, 0, i == _section ? 8 : 11);
            }
        }
        PageChanged(this, EventArgs.Empty); FocusPage();
    }
    private void PageChanged(object? sender, EventArgs e)
    {
        _hints.Content = FullscreenGlyphs.Hints(CurrentPage.Hints);
        _rightHints.Content = FullscreenGlyphs.Hints(CurrentPage.RightHints);
    }
    public void FocusPage() => Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed && _keyboard is null && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null) CurrentPage.FocusInitial();
    }, DispatcherPriority.Loaded);
    public void UpdateController(string? status) => _status.Text = status ?? "Controller disconnected";
    public void Handle(GamepadButtons buttons)
    {
        if (_disposed || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null) return;
        if (_keyboard is { } keyboard) { keyboard.Handle(buttons); return; }
        if (buttons.HasFlag(GamepadButtons.Menu)) { QuickMenu(); return; }
        if (CurrentPage.Handle(buttons)) return;
        if (buttons.HasFlag(GamepadButtons.Back)) Back();
        else if (_stack.Count == 0 && buttons.HasFlag(GamepadButtons.Previous)) SelectSection(_section - 1);
        else if (_stack.Count == 0 && buttons.HasFlag(GamepadButtons.Next)) SelectSection(_section + 1);
    }
    public bool HandleKey(KeyEventArgs e)
    {
        if (e.Source is TextBox && _keyboard is null && e.Key is not (Key.Escape or Key.Tab)) return false;
        var buttons = e.Key switch { Key.Up => GamepadButtons.Up, Key.Down => GamepadButtons.Down, Key.Left => GamepadButtons.Left, Key.Right => GamepadButtons.Right,
            Key.Enter => GamepadButtons.Accept, Key.Escape => GamepadButtons.Back, Key.Q => GamepadButtons.Previous, Key.E => GamepadButtons.Next,
            Key.X => GamepadButtons.Play, Key.Y => GamepadButtons.Keyboard, Key.F => GamepadButtons.Search,
            Key.PageUp => GamepadButtons.PagePrevious, Key.PageDown => GamepadButtons.PageNext,
            Key.Tab => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? GamepadButtons.Up : GamepadButtons.Down, _ => GamepadButtons.None };
        if (buttons == GamepadButtons.None) return false;
        Handle(buttons); return true;
    }
    private void QuickMenu()
    {
        var actions = new List<FullscreenAction> { new("Resume", () => { }) };
        if (_stack.Count == 0) actions.Add(new("Settings", () => SelectSection(3)));
        actions.Add(new("Exit fullscreen", () => ExitRequested?.Invoke()));
        actions.Add(new("Quit Winnow", () => _context.ShowActions("Quit Winnow? Unsaved edits will be lost.",
            [new("Cancel", () => { }), new("Quit Winnow", () => QuitRequested?.Invoke())])));
        _context.ShowActions("Quick menu", actions);
    }
    private void Notice(string text) => _context.ShowActions(text, [new("Continue", () => { })]);
    private void EditText(TextBox text)
    {
        if (_keyboard is not null) return;
        var keyboard = new GamepadKeyboardView(text, television: true);
        _keyboard = keyboard;
        var veil = new Border { Background = new SolidColorBrush(Color.FromArgb(230, 15, 28, 30)), Child = keyboard };
        _overlay.Children.Add(veil);
        keyboard.Closed += (_, _) => { _overlay.Children.Remove(veil); _keyboard = null; text.Focus(); };
    }
    private Task<string?> PickFile(string title, IReadOnlyList<string>? extensions)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Push(new FullscreenFilePage(_context, title, extensions, completion)); return completion.Task;
    }
    private Task<string?> SaveFile(string title, string suggestedName)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Push(new FullscreenFilePage(_context, title, null, completion, suggestedName: suggestedName));
        return completion.Task;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _keyboard?.Close();
        _context.PageRequested -= Push; _context.BackRequested -= Back; _context.TextRequested -= EditText;
        _context.Notice -= Notice; _context.PreferencesChanged -= Preferences; _context.FilePicker = null; _context.SaveFilePicker = null;
        _context.DetailsChanged -= ReplaceDetails;
        _context.Shared.Library.Journal.PropertyChanged -= JournalChanged;
        _context.Shared.Library.LaunchStatus.PropertyChanged -= LaunchChanged;
        _context.Library.LaunchStatus.PropertyChanged -= LaunchChanged;
        foreach (var page in _stack.Concat(_roots)) page.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
