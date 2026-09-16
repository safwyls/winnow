#:package SkiaSharp@2.88.9

using System.Text;
using System.Xml.Linq;
using SkiaSharp;

// Run from the repository root: dotnet run --file scripts/Generate-AppIcon.cs -- [preview-directory]
var root = Directory.GetCurrentDirectory();
var source = XDocument.Load(Path.Combine(root, "src/Winnow.App/Assets/Icons/dragon.svg"));
using var dragon = new SKPath { FillType = SKPathFillType.EvenOdd };
foreach (var element in source.Descendants().Where(element => element.Name.LocalName == "path"))
{
    using var path = SKPath.ParseSvgPathData(element.Attribute("d")!.Value);
    dragon.AddPath(path);
}

int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var frames = new List<byte[]>();
var previewDirectory = args.FirstOrDefault();
if (previewDirectory is not null) Directory.CreateDirectory(previewDirectory);
foreach (var size in sizes)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
    using (var canvas = new SKCanvas(bitmap))
    {
        canvas.Clear(SKColors.Transparent);
        var inset = size * .055f;
        var scale = (size - 2 * inset) / 512;
        canvas.Translate(inset, inset);
        canvas.Scale(scale);
        using var outline = new SKPaint
        {
            Color = SKColor.Parse("#0F1C1E"), IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round,
            StrokeWidth = Math.Max(1.2f, size * .018f) / scale
        };
        using var fill = new SKPaint { Color = SKColor.Parse("#F0EDE7"), IsAntialias = true };
        // Small mane fragments need a little extra weight at their native size.
        var dilation = size == 16 ? .50f : size == 24 ? .40f : 0;
        outline.StrokeWidth += dilation / scale;
        canvas.DrawPath(dragon, outline);
        if (dilation > 0)
        {
            fill.Style = SKPaintStyle.StrokeAndFill;
            fill.StrokeWidth = dilation / scale;
            fill.StrokeJoin = SKStrokeJoin.Round;
        }
        canvas.DrawPath(dragon, fill);
    }

    if (bitmap.GetPixel(0, 0).Alpha != 0 || bitmap.GetPixel(size - 1, size - 1).Alpha != 0)
        throw new InvalidOperationException("Icon corners must remain transparent.");
    using var image = SKImage.FromBitmap(bitmap);
    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    frames.Add(size >= 64 ? png.ToArray() : Dib(bitmap));
    if (previewDirectory is not null)
    {
        File.WriteAllBytes(Path.Combine(previewDirectory, $"dragon-{size}.png"), png.ToArray());
        using var preview = new SKBitmap(size * 2, size);
        using var canvas = new SKCanvas(preview);
        canvas.Clear(SKColor.Parse("#F3F3F3"));
        using var dark = new SKPaint { Color = SKColor.Parse("#202020") };
        canvas.DrawRect(size, 0, size, size, dark);
        canvas.DrawBitmap(bitmap, 0, 0);
        canvas.DrawBitmap(bitmap, size, 0);
        using var previewImage = SKImage.FromBitmap(preview);
        using var previewPng = previewImage.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(previewDirectory, $"contrast-{size}.png"), previewPng.ToArray());
    }
}

var destination = Path.Combine(root, "src/Winnow.App/Assets/Icons/dragon.ico");
using (var writer = new BinaryWriter(File.Create(destination)))
{
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
    var offset = 6 + sizes.Length * 16;
    for (var i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        writer.Write((byte)0); writer.Write((byte)0);
        writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(frames[i].Length); writer.Write(offset);
        offset += frames[i].Length;
    }
    foreach (var frame in frames) writer.Write(frame);
}
Console.WriteLine($"Wrote {destination}: {string.Join(", ", sizes)}px transparent frames.");

static byte[] Dib(SKBitmap bitmap)
{
    var size = bitmap.Width;
    var maskStride = ((size + 31) / 32) * 4;
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
    writer.Write(40); writer.Write(size); writer.Write(size * 2);
    writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0);
    writer.Write(size * size * 4 + maskStride * size);
    writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
    // ICO DIB rows are bottom-up and store straight-alpha BGRA pixels.
    for (var y = size - 1; y >= 0; y--)
        for (var x = 0; x < size; x++)
        {
            var color = bitmap.GetPixel(x, y);
            writer.Write(color.Blue); writer.Write(color.Green);
            writer.Write(color.Red); writer.Write(color.Alpha);
        }
    for (var y = size - 1; y >= 0; y--)
    {
        var mask = new byte[maskStride];
        for (var x = 0; x < size; x++)
            if (bitmap.GetPixel(x, y).Alpha == 0) mask[x / 8] |= (byte)(0x80 >> (x % 8));
        writer.Write(mask);
    }
    return stream.ToArray();
}
