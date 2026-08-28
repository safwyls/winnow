using SkiaSharp;

namespace Winnow.Covers;

/// <summary>
/// The pixel work: scaled decode and the dormancy floor colour matrix. Pure
/// SkiaSharp — no Avalonia, no IO — so it runs on any thread and is testable
/// without a rendering platform.
/// </summary>
public static class CoverImaging
{
    /// <summary>
    /// Display widths we actually decode at. Snapping means the density slider
    /// and DPI changes cannot start an endless re-decode treadmill, and the
    /// memory cache stays keyed by a small, finite set. 160 covers a 148 DIP
    /// tile at 1x, 320 at 2x, 640 at 4x.
    /// </summary>
    public static readonly int[] WidthBuckets = [160, 240, 320, 480, 640];

    public static int SnapWidth(double requestedPixels)
    {
        foreach (var bucket in WidthBuckets)
        {
            if (requestedPixels <= bucket)
            {
                return bucket;
            }
        }

        return WidthBuckets[^1];
    }

    /// <summary>
    /// Decodes <paramref name="encoded"/> to at most <paramref name="targetWidth"/>
    /// pixels wide. Uses the codec's own sub-sampling to get as close as the
    /// format allows before resampling, so a 1200x1800 capsule never fully
    /// materialises: §5.4's "never decode 600x900 sources eagerly" applies to
    /// the transient decode too, not just what we keep.
    /// </summary>
    public static SKBitmap? DecodeToWidth(byte[] encoded, int targetWidth)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length == 0 || targetWidth <= 0)
        {
            return null;
        }

        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return null;
        }

        var source = codec.Info;
        if (source.Width <= 0 || source.Height <= 0)
        {
            return null;
        }

        var width = Math.Min(targetWidth, source.Width);
        var height = Math.Max(1, (int)Math.Round(width * (double)source.Height / source.Width));

        // JPEG only decodes at n/8 scales; take the smallest supported size that
        // is still at or above the target, then resample down to exact.
        var decodeSize = new SKSizeI(source.Width, source.Height);
        for (var n = 1; n <= 8; n++)
        {
            var candidate = codec.GetScaledDimensions(n / 8f);
            if (candidate.Width >= width)
            {
                decodeSize = candidate;
                break;
            }
        }

        var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decoded.Dispose();
            return null;
        }

        if (decoded.Width == width && decoded.Height == height)
        {
            return decoded;
        }

        var resized = decoded.Resize(
            new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
            SKFilterQuality.High);
        decoded.Dispose();
        return resized;
    }

    /// <summary>
    /// The §5.1 floor matrix: Rec.709 luma desaturation to
    /// <paramref name="saturation"/>, a hue rotation of
    /// <paramref name="hueDegrees"/>, then a uniform brightness scale — the
    /// composition of mock-library.html's
    /// <c>saturate() brightness() hue-rotate(-6deg)</c>, in that order.
    ///
    /// <para>The hue term is what makes dormant art read as <em>cool</em> and
    /// not merely grey (§1). It is small on purpose: Steam capsules are mostly
    /// warm and dark, so without it the floor lands on a neutral-warm mud that
    /// looks like a rendering fault rather than an encoding.</para>
    ///
    /// <para>Brightness is a scalar and commutes with the other two, so it is
    /// folded in last. This is exactly the per-channel math in Winnow.App's
    /// PlaceholderArt.ToFloor — the placeholder and the real cover must fade to
    /// the same endpoint, or a tile would visibly jump when its cover arrives.</para>
    /// </summary>
    public static float[] FloorMatrix(float saturation, float brightness, float hueDegrees = DefaultHueDegrees)
    {
        var sat = SaturationMatrix(saturation);
        var hue = HueRotationMatrix(hueDegrees);
        var m = Multiply3x3(hue, sat);

        return
        [
            m[0] * brightness, m[1] * brightness, m[2] * brightness, 0, 0,
            m[3] * brightness, m[4] * brightness, m[5] * brightness, 0, 0,
            m[6] * brightness, m[7] * brightness, m[8] * brightness, 0, 0,
            0,                 0,                 0,                 1, 0,
        ];
    }

    /// <summary>The §5.1 cool shift, in degrees. Negative rotates toward blue.</summary>
    public const float DefaultHueDegrees = -6f;

    private const float LumaR = 0.2126f;
    private const float LumaG = 0.7152f;
    private const float LumaB = 0.0722f;

    /// <summary>Row-major 3x3 Rec.709 luma desaturation.</summary>
    private static float[] SaturationMatrix(float s)
    {
        var inv = 1f - s;
        float r = LumaR * inv, g = LumaG * inv, b = LumaB * inv;
        return
        [
            r + s, g,     b,
            r,     g + s, b,
            r,     g,     b + s,
        ];
    }

    /// <summary>
    /// Row-major 3x3 hue rotation, the SVG <c>feColorMatrix type="hueRotate"</c>
    /// construction CSS <c>hue-rotate()</c> is defined by.
    /// </summary>
    private static float[] HueRotationMatrix(float degrees)
    {
        var radians = degrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return
        [
            LumaR + cos * (1 - LumaR) - sin * LumaR,
            LumaG - cos * LumaG       - sin * LumaG,
            LumaB - cos * LumaB       + sin * (1 - LumaB),

            LumaR - cos * LumaR       + sin * 0.143f,
            LumaG + cos * (1 - LumaG) + sin * 0.140f,
            LumaB - cos * LumaB       - sin * 0.283f,

            LumaR - cos * LumaR       - sin * (1 - LumaR),
            LumaG - cos * LumaG       + sin * LumaG,
            LumaB + cos * (1 - LumaB) + sin * LumaB,
        ];
    }

    private static float[] Multiply3x3(float[] a, float[] b)
    {
        var result = new float[9];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                result[(row * 3) + col] =
                    (a[row * 3] * b[col])
                    + (a[(row * 3) + 1] * b[3 + col])
                    + (a[(row * 3) + 2] * b[6 + col]);
            }
        }

        return result;
    }

    /// <summary>Produces the dormancy floor variant of a decoded cover.</summary>
    public static SKBitmap ApplyFloor(
        SKBitmap source,
        float saturation,
        float brightness,
        float hueDegrees = DefaultHueDegrees)
    {
        ArgumentNullException.ThrowIfNull(source);

        var target = new SKBitmap(new SKImageInfo(source.Width, source.Height, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(FloorMatrix(saturation, brightness, hueDegrees)),
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false,
        };
        canvas.DrawBitmap(source, 0, 0, paint);
        return target;
    }

    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }
}
