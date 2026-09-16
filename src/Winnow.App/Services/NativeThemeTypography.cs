using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;

namespace Winnow.App.Services;

/// <summary>Theme typography for native controls in fullscreen browser and consent windows.</summary>
internal sealed class NativeThemeTypography : AvaloniaObject, IDisposable
{
    private static readonly AttachedProperty<double> ScaleProperty =
        AvaloniaProperty.RegisterAttached<NativeThemeTypography, Control, double>("Scale", 1);
    private readonly Control _root;
    private readonly Control? _consent;
    private readonly ConditionalWeakTable<Control, TypeSize> _sizes = new();
    private sealed record TypeSize(double Font, double LineHeight);
    private bool _disposed;

    public NativeThemeTypography(Control root, Control? consent = null)
    {
        _root = root;
        _consent = consent;
        // Shared AXAML styles and imperative fullscreen controls enter this scope at their authored
        // sizes. Only this scope multiplies them, so opening a window at 120% cannot scale twice.
        ThemeTypographyResources.UseUnscaledSizes(root.Resources);
        root[!TextElement.FontFamilyProperty] = new DynamicResourceExtension("BodyFont");
        root.PropertyChanged += Changed;
        root.LayoutUpdated += LayoutUpdated;
        root[!ScaleProperty] = new DynamicResourceExtension("ThemeTextScale");
    }

    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Wait for styles/templates to settle before remembering their unscaled sizes.
        if (e.Property == ScaleProperty) _root.InvalidateMeasure();
    }

    private void LayoutUpdated(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        if (_disposed) return;
        var scale = _root.GetValue(ScaleProperty);
        var consentControls = _consent?.GetVisualDescendants().OfType<Control>().Append(_consent).ToHashSet();
        foreach (var control in _root.GetVisualDescendants().OfType<Control>())
        {
            var minimum = consentControls?.Contains(control) == true ? 28 : 0;
            if (control is TextBlock text && (text.IsSet(TextBlock.FontSizeProperty) || minimum > 0))
            {
                // Template labels inherit an already scaled button/input. Giving them another local
                // size would multiply twice, including templates materialized after the first layout.
                if (!text.IsSet(TextBlock.FontSizeProperty) && text.GetVisualAncestors().OfType<Control>()
                    .Any(parent => _sizes.TryGetValue(parent, out _))) continue;
                var size = _sizes.GetValue(text, _ => new TypeSize(Math.Max(minimum, text.FontSize),
                    double.IsFinite(text.LineHeight) && text.LineHeight > 0
                        ? text.LineHeight * Math.Max(minimum, text.FontSize) / text.FontSize : double.NaN));
                text.FontSize = size.Font * scale;
                if (double.IsFinite(size.LineHeight)) text.LineHeight = size.LineHeight * scale;
            }
            else if (control is TemplatedControl templated && templated.IsSet(TemplatedControl.FontSizeProperty))
            {
                var size = _sizes.GetValue(templated, _ => new TypeSize(Math.Max(minimum, templated.FontSize), double.NaN));
                templated.FontSize = size.Font * scale;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _root.PropertyChanged -= Changed;
        _root.LayoutUpdated -= LayoutUpdated;
        _root.ClearValue(ScaleProperty);
    }
}
