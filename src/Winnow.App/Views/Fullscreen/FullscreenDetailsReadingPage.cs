using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

public sealed class FullscreenDetailsReadingPage : FullscreenPage
{
    private readonly string _title;
    private readonly ScrollViewer _scroll;

    public FullscreenDetailsReadingPage(FullscreenContext context, string title, string text) : base(context)
    {
        _title = title;
        var back = FullscreenUi.Button("Back", Context.Back);
        _scroll = FullscreenUi.Scroll(FullscreenUi.Text(text));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), MaxWidth = 1200 };
        layout.Children.Add(FullscreenUi.Text(title, 32));
        Grid.SetRow(_scroll, 1);
        layout.Children.Add(_scroll);
        Grid.SetRow(back, 2);
        layout.Children.Add(back);
        Content = layout;
        SetFocusRows([back]);
    }

    public override string Title => _title;
    public override string Hints => "↑ / ↓ Read   B Back";

    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Down) || buttons.HasFlag(GamepadButtons.ScrollDown))
        { _scroll.Offset = new Vector(0, _scroll.Offset.Y + 160); return true; }
        if (buttons.HasFlag(GamepadButtons.Up) || buttons.HasFlag(GamepadButtons.ScrollUp))
        { _scroll.Offset = new Vector(0, Math.Max(0, _scroll.Offset.Y - 160)); return true; }
        return base.Handle(buttons);
    }
}

public sealed class FullscreenDetailsScreenshotPage : FullscreenPage
{
    private readonly ScreenshotLightboxViewModel _lightbox;

    public FullscreenDetailsScreenshotPage(FullscreenContext context, ScreenshotLightboxViewModel lightbox) : base(context)
    {
        _lightbox = lightbox;
        var image = new Image { Stretch = Stretch.Uniform };
        image.Bind(Image.SourceProperty, new Binding(nameof(ScreenshotLightboxViewModel.Image)) { Source = lightbox });
        var caption = FullscreenUi.Text(lightbox.Caption, 24);
        caption.Bind(TextBlock.TextProperty, new Binding(nameof(ScreenshotLightboxViewModel.Caption)) { Source = lightbox });
        var back = FullscreenUi.Button("Back", Context.Back);
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        layout.Children.Add(image);
        Grid.SetRow(caption, 1);
        layout.Children.Add(caption);
        Grid.SetRow(back, 2);
        layout.Children.Add(back);
        Content = layout;
        SetFocusRows([back]);
    }

    public override string Title => "Screenshots";
    public override void Dispose()
    {
        _lightbox.CloseCommand.Execute(null);
        base.Dispose();
    }
    public override string Hints => _lightbox.CanNavigate ? "← / → Screenshot   B Back" : "B Back";
    public override bool Handle(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Left)) { _lightbox.PreviousCommand.Execute(null); return true; }
        if (buttons.HasFlag(GamepadButtons.Right)) { _lightbox.NextCommand.Execute(null); return true; }
        return base.Handle(buttons);
    }
}
