namespace Winnow.Covers;

/// <summary>Bounds encoded HTTP bodies even when Content-Length is missing or false.</summary>
public static class CoverDownload
{
    public const int MaxBytes = 16 * 1024 * 1024;

    public static async Task<byte[]> ReadAsync(HttpContent content, CancellationToken ct = default)
    {
        if (content.Headers.ContentLength > MaxBytes)
        {
            throw new InvalidDataException("Cover response exceeds the 16 MiB limit.");
        }

        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            // Read at most one byte beyond the ceiling to establish overflow.
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaxBytes - (int)buffer.Length + 1)), ct)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaxBytes)
            {
                throw new InvalidDataException("Cover response exceeds the 16 MiB limit.");
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
