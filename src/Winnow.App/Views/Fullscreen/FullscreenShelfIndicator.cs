using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Path = Avalonia.Controls.Shapes.Path;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Mouse-accessible shelf position, without extra controller focus stops.</summary>
public sealed class FullscreenShelfIndicator : UserControl
{
    public FullscreenShelfIndicator(IReadOnlyList<string> titles, int selected, Action<int> select)
    {
        Name = "FullscreenShelfIndicator";
        Width = 44;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        var current = Math.Clamp(selected, 0, Math.Max(0, titles.Count - 1));
        AutomationProperties.SetName(this, "Recommendation shelves");
        if (titles.Count > 0)
            AutomationProperties.SetItemStatus(this, $"{titles[current]}, shelf {current + 1} of {titles.Count}");

        Styles.Add(new Style(s => s.OfType<Button>().Class(":pointerover"))
        { Setters = { new Setter(OpacityProperty, 1d) } });
        Styles.Add(new Style(s => s.OfType<Button>().Class(":disabled"))
        { Setters = { new Setter(OpacityProperty, .3d) } });

        var rail = new StackPanel { Spacing = 4 };
        rail.Children.Add(Arrow(true, current > 0, () => select(current - 1)));
        for (var i = 0; i < titles.Count; i++)
        {
            var index = i;
            var active = index == current;
            var dot = new Border
            {
                Width = active ? 16 : 12,
                Height = active ? 16 : 12,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            dot[!Border.BorderBrushProperty] = new DynamicResourceExtension(active ? "Volt" : "TextDim");
            if (active) dot[!Border.BackgroundProperty] = new DynamicResourceExtension("Volt");
            var button = Target(dot, $"{titles[index]}, shelf {index + 1} of {titles.Count}", () => select(index));
            AutomationProperties.SetItemStatus(button, active ? "Current shelf" : "");
            rail.Children.Add(button);
        }
        rail.Children.Add(Arrow(false, titles.Count > 0 && current < titles.Count - 1, () => select(current + 1)));
        Content = rail;
    }

    private static Button Arrow(bool up, bool enabled, Action action)
    {
        var path = new Path
        {
            Data = Geometry.Parse(up ? "M 0,10 L 12,0 L 24,10 L 22,12 L 12,4 L 2,12 Z"
                : "M 0,2 L 2,0 L 12,8 L 22,0 L 24,2 L 12,12 Z"),
            Width = 24, Height = 12, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        path[!Path.FillProperty] = new DynamicResourceExtension("TextDim");
        var button = Target(path, up ? "Previous shelf" : "Next shelf", action);
        button.IsEnabled = enabled;
        return button;
    }

    private static Button Target(Control content, string label, Action action)
    {
        var button = new Button
        {
            Content = content, Width = 44, Height = 44, Padding = new Thickness(0),
            Focusable = false, FocusAdorner = null,
            Template = new FuncControlTemplate<Button>((owner, _) =>
            {
                var presenter = new ContentPresenter
                {
                    Background = Brushes.Transparent,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Button.Content)) { Source = owner });
                return presenter;
            })
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        button.Click += (_, _) => action();
        return button;
    }
}
