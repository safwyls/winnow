using Winnow.Core.Ingest;

namespace Winnow.Ingest.Epic;

/// <summary>Reads a change token for committed manifests. A failed read is not an uninstall.</summary>
public sealed class EpicManifestStateReader(string? dataRoot = null, StorefrontParserLimits? limits = null)
{
    private readonly EpicManifestReader _reader = new(limits: limits);

    public string? ReadFingerprint()
    {
        var root = dataRoot ?? EpicPaths.FindDataRoot();
        if (root is null) return null;
        return _reader.ScanDirectory(EpicPaths.ManifestsDirectory(root)).Fingerprint;
    }
}
