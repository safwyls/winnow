using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Reserves both label weights so selection cannot move adjacent tabs or triggers.</summary>
internal sealed class FullscreenNavigationLabel : TextBlock
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        foreach (var weight in new[] { FontWeight.Normal, FontWeight.Bold })
        {
            var text = new FormattedText(Text ?? "", CultureInfo.CurrentCulture, FlowDirection,
                new Typeface(FontFamily, FontStyle, weight, FontStretch), FontSize, Foreground);
            measured = new Size(Math.Max(measured.Width, text.WidthIncludingTrailingWhitespace),
                Math.Max(measured.Height, text.Height));
        }
        return measured;
    }
}
