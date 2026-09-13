using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Type and grouping roles for readable fullscreen information surfaces.</summary>
internal static class FullscreenInformation
{
    public static TextBlock Text(string text, double size = 24, string brush = "Text", FontWeight? weight = null)
    {
        var block = FullscreenUi.Text(text, size, brush);
        block[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("BodyFont");
        block.FontWeight = weight ?? FontWeight.Normal;
        return block;
    }

    public static TextBlock Heading(string text)
    {
        var heading = Text(text.ToUpperInvariant(), 18, "TextDim");
        AutomationProperties.SetHeadingLevel(heading, 2);
        return heading;
    }

    public static TextBlock Title(string text) => Text(text, 32, weight: FontWeight.Bold);
    public static TextBlock Metadata(string text) => Text(text, 22, "TextDim");

    public static Border Rule()
    {
        var rule = new Border { Height = 1, Margin = new Thickness(0, 12) };
        rule[!Border.BackgroundProperty] = new DynamicResourceExtension("Line");
        return rule;
    }

    public static Button Link(string text, Action action)
    {
        var button = FullscreenUi.Button(text, action);
        button.FontSize = 24;
        button.Padding = new Thickness(0, 4);
        button.MinHeight = 36;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        var label = Text(text);
        label.MaxLines = 3;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Left };
        line.Children.Add(label);
        var arrow = Text("→", 26);
        Grid.SetColumn(arrow, 1);
        line.Children.Add(arrow);
        button.Content = line;
        return button;
    }

    public static StackPanel Column(string? name = null) => new InformationColumn
    {
        Name = name, Spacing = 14, MaxWidth = 1320, HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 20, 0, 0)
    };

    public static void AddSection(StackPanel content, string heading)
    {
        if (content.Children.Count > 0)
        {
            var rule = Rule();
            rule.Margin = new Thickness(0, 12);
            content.Children.Add(rule);
        }
        content.Children.Add(Heading(heading));
    }

    private sealed class InformationColumn : StackPanel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var size = base.MeasureOverride(availableSize);
            // Short entries keep the same measure and divider width as longer entries.
            return new Size(double.IsFinite(availableSize.Width) ? Math.Min(1320, availableSize.Width) : size.Width, size.Height);
        }
    }
}
