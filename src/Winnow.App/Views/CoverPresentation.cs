using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Winnow.App.Views;

/// <summary>Shares cover fitting through the presentation tree without changing artwork backgrounds.</summary>
public sealed class CoverPresentation : AvaloniaObject
{
    public static readonly AttachedProperty<bool> FitProperty =
        AvaloniaProperty.RegisterAttached<CoverPresentation, Control, bool>("Fit", true, inherits: true);
    public static readonly AttachedProperty<bool> IsCoverProperty =
        AvaloniaProperty.RegisterAttached<CoverPresentation, Image, bool>("IsCover");

    static CoverPresentation()
    {
        FitProperty.Changed.AddClassHandler<Image>((image, _) => Apply(image));
        IsCoverProperty.Changed.AddClassHandler<Image>((image, _) => Apply(image));
    }

    public static bool GetFit(Control control) => control.GetValue(FitProperty);
    public static void SetFit(Control control, bool value) => control.SetValue(FitProperty, value);
    public static bool GetIsCover(Image image) => image.GetValue(IsCoverProperty);
    public static void SetIsCover(Image image, bool value) => image.SetValue(IsCoverProperty, value);

    private static void Apply(Image image)
    {
        if (GetIsCover(image)) image.SetCurrentValue(Image.StretchProperty, GetFit(image) ? Stretch.Uniform : Stretch.UniformToFill);
    }
}
