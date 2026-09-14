using System.Security.Cryptography;
using SkiaSharp;
using Winnow.Covers;

namespace Winnow.App.Services;

/// <summary>Persistent shell icons derived from the same artwork as library covers.</summary>
internal sealed class JumpListIcons(string directory, CoverPipeline pipeline)
{
    public async Task<string?> GetAsync(CoverKey? key, CancellationToken ct)
    {
        if (key is null) return null;
        try
        {
            using var art = await pipeline.GetAsync(key.Value, 128, CoverLayers.Vivid, ct).ConfigureAwait(false);
            if (art is null) return null;
            var bytes = Encode(art.Vivid);
            var path = Path.Combine(directory, "jump-list-icons", Convert.ToHexString(SHA256.HashData(bytes)) + ".ico");
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, ct).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    internal static byte[] Encode(SKBitmap source)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128];
        var frames = new List<byte[]>();
        var side = Math.Min(source.Width, source.Height);
        var crop = SKRect.Create((source.Width - side) / 2f, (source.Height - side) / 2f, side, side);
        foreach (var size in sizes)
        {
            using var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, crop, SKRect.Create(size, size), paint);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            frames.Add(data.ToArray());
        }
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);
        var offset = 6 + sizes.Length * 16;
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)sizes[i]); writer.Write((byte)sizes[i]);
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(frames[i].Length); writer.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        return output.ToArray();
    }
}
