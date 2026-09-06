using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Winnow.Ingest.Epic;

/// <summary>Reads a change token for committed manifests. A failed read is not an uninstall.</summary>
public sealed class EpicManifestStateReader(string? dataRoot = null)
{
    public string? ReadFingerprint()
    {
        var root = dataRoot ?? EpicPaths.FindDataRoot();
        if (root is null) return null;
        var directory = EpicPaths.ManifestsDirectory(root);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in Directory.GetFiles(directory, "*.item", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".item", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase))
            {
                var bytes = File.ReadAllBytes(path);
                using var document = JsonDocument.Parse(bytes);
                var item = document.RootElement;
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("CatalogItemId", out var id)
                    || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString())
                    || !item.TryGetProperty("bIsIncompleteInstall", out var incomplete)
                    || incomplete.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path)));
                hash.AppendData(bytes);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
