using Winnow.Core.Ingest;

namespace Winnow.Ingest;

/// <summary>Shared source compiled into the local readers; Core remains free of IO.</summary>
internal static class StorefrontFile
{
    internal static byte[] Read(string path, StorefrontParserLimits limits)
    {
        limits.Validate();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > limits.MaxFileBytes)
            throw new IOException("Storefront file exceeds the configured byte limit.");
        using var result = new MemoryStream();
        var buffer = new byte[Math.Min(limits.MaxFileBytes, 81920)];
        while (true)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, limits.MaxFileBytes - result.Length));
            if (read == 0)
            {
                // Recheck the actual bytes, including files that grew after opening.
                if (result.Length == limits.MaxFileBytes && stream.ReadByte() != -1)
                    throw new IOException("Storefront file exceeds the configured byte limit.");
                return result.ToArray();
            }
            result.Write(buffer, 0, read);
        }
    }
}
