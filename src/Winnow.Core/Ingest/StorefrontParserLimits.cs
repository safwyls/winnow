namespace Winnow.Core.Ingest;

/// <summary>Resource limits for launcher-owned VDF and JSON files.</summary>
public sealed record StorefrontParserLimits
{
    public int MaxFileBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxDepth { get; init; } = 64;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDepth);
        // A preflight must not permit a caller to re-enable recursive stack exhaustion.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxDepth, 256);
    }
}
