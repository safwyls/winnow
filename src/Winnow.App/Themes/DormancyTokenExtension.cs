using Avalonia.Markup.Xaml;
using Winnow.Covers;

namespace Winnow.App.Themes;

public enum DormancyComponent { Saturation, Brightness, Hue }

/// <summary>Exposes the renderer's endpoint as ordinary double-valued XAML resources.</summary>
public sealed class DormancyTokenExtension : MarkupExtension
{
    public DormancyComponent Component { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider) => Component switch
    {
        DormancyComponent.Saturation => DormancyStyle.SaturationFloor,
        DormancyComponent.Brightness => DormancyStyle.BrightnessFloor,
        DormancyComponent.Hue => DormancyStyle.HueDegrees,
        _ => throw new ArgumentOutOfRangeException(nameof(Component))
    };
}
