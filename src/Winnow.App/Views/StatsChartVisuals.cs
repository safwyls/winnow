using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>Native chart presentation shared by Gameplay and Spending on both surfaces.</summary>
internal sealed class StatsChartVisuals(bool fullscreen)
{
    public bool Fullscreen => fullscreen;
    private double Scale => Fullscreen ? 1.7 : 1;
    public TextBlock Text(string text, double size = 13, string ink = "TextDim", bool data = false)
    {
        var block = new TextBlock { Text = text, FontSize = Fullscreen ? Math.Max(24, size * Scale) : size * Scale, TextWrapping = TextWrapping.Wrap };
        if (!Fullscreen) ThemeTypographyResources.BindSize(block, size);
        block[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(ink);
        block[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension(data ? "DataFont" : "BodyFont");
        if (data) block.FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") };
        return block;
    }
    public TextBlock Heading(string text)
    {
        var block = Text(text, 20, "Text");
        block[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("DisplayFont");
        block.FontWeight = FontWeight.Bold;
        AutomationProperties.SetHeadingLevel(block, 2);
        return block;
    }
    public StackPanel Stack(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 12 * Scale };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }
    public Border Card(Control content)
    {
        var card = new Border { Padding = new Thickness(20 * Scale), Child = content };
        card[!Border.CornerRadiusProperty] = new DynamicResourceExtension("RadiusTile");
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("Surface");
        return card;
    }
    public Control Bars(IReadOnlyList<AccountChartItem> items)
    {
        if (items.Count == 0) return Text("No recorded values to chart yet.");
        var max = Math.Max(0, items.Max(x => x.Value));
        var negativeMax = Math.Max(0, -items.Min(x => x.Value));
        var body = Stack();
        foreach (var item in items)
        {
            var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            labels.Children.Add(Text(item.Label, 12, "Text", true));
            var amount = Text(item.ValueText, 12, "Text", true); Grid.SetColumn(amount, 1); labels.Children.Add(amount);
            var positive = Math.Max(0, item.Value);
            var negative = Math.Max(0, -item.Value);
            var track = new Grid { Height = 9 * Scale };
            track[!Panel.BackgroundProperty] = new DynamicResourceExtension("Well");
            foreach (var weight in new[] { negativeMax - negative, negative, positive, max - positive })
                track.ColumnDefinitions.Add(new ColumnDefinition(new GridLength((double)weight, GridUnitType.Star)));
            var bar = new Border { IsVisible = item.Value != 0 };
            Grid.SetColumn(bar, item.Value < 0 ? 1 : 2);
            bar[!Border.BackgroundProperty] = new DynamicResourceExtension(item.Ink); track.Children.Add(bar);
            if (negativeMax > 0 && max > 0)
            {
                var baseline = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left };
                baseline[!Border.BackgroundProperty] = new DynamicResourceExtension("TextDim");
                Grid.SetColumn(baseline, 2); track.Children.Add(baseline);
            }
            body.Children.Add(Stack(labels, track));
        }
        return body;
    }
}
