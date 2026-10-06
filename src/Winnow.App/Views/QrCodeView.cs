using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Net.Codecrete.QrCodeGenerator;

namespace Winnow.App.Views;

/// <summary>
/// Draws a QR code for a phone camera to read. The colours are fixed black on white with
/// the standard four-module quiet zone: scanners need that contrast whatever the theme.
/// </summary>
public sealed class QrCodeView : Control
{
    private const int QuietZone = 4;

    public static readonly StyledProperty<string?> PayloadProperty =
        AvaloniaProperty.Register<QrCodeView, string?>(nameof(Payload));

    private QrCode? _code;

    static QrCodeView()
    {
        AffectsRender<QrCodeView>(PayloadProperty);
        PayloadProperty.Changed.AddClassHandler<QrCodeView>((view, _) => view.Encode());
    }

    public QrCodeView()
    {
        AutomationProperties.SetName(this, "Pairing QR code");
    }

    public string? Payload
    {
        get => GetValue(PayloadProperty);
        set => SetValue(PayloadProperty, value);
    }

    /// <summary>Modules per side, quiet zone excluded; zero when there is nothing to show.</summary>
    public int ModuleCount => _code?.Size ?? 0;

    private void Encode() => _code = string.IsNullOrEmpty(Payload) ? null : QrCode.EncodeText(Payload, QrCode.Ecc.Medium);

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = double.IsFinite(availableSize.Width) ? Math.Min(availableSize.Width, 280) : 280;
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        if (_code is null) return;
        var side = Math.Min(Bounds.Width, Bounds.Height);
        var cells = _code.Size + 2 * QuietZone;
        // Whole-unit modules keep edges crisp; leftover space joins the quiet zone.
        var module = Math.Max(1, Math.Floor(side / cells));
        var origin = Math.Floor((side - module * _code.Size) / 2);
        context.FillRectangle(Brushes.White, new Rect(0, 0, side, side));
        for (var y = 0; y < _code.Size; y++)
            for (var x = 0; x < _code.Size; x++)
                if (_code.GetModule(x, y))
                    context.FillRectangle(Brushes.Black, new Rect(origin + x * module, origin + y * module, module, module));
    }
}
