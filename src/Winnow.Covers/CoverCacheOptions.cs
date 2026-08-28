namespace Winnow.Covers;

/// <summary>
/// Knobs for the cover pipeline. Defaults are the shipped values; tests override
/// the directory and the CDN base so nothing touches the real cache or network.
/// </summary>
public sealed class CoverCacheOptions
{
    /// <summary><c>%LOCALAPPDATA%\Winnow\covers\</c>.</summary>
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Winnow",
        "covers");

    /// <summary>
    /// In-flight network fetches. A handful, not 616: the CDN is a shared
    /// resource and §5.1 forbids a user-facing path ever waiting on it.
    /// </summary>
    public int MaxConcurrentFetches { get; set; } = 6;

    /// <summary>
    /// Ceiling on decoded pixel bytes held in memory (vivid + floor counted
    /// together). See <see cref="CoverCache"/> for the sizing argument.
    /// </summary>
    public long MaxDecodedBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>
    /// Width the floor variant is stored at on disk. 640 is ≥ the largest
    /// display bucket, so no display size ever needs the full 1200px source
    /// re-processed, and the colour-matrix pass runs once on a small bitmap.
    /// </summary>
    public int DiskVariantWidth { get; set; } = 640;

    /// <summary>How long a "this app has no capsule" result stays believed.</summary>
    public TimeSpan NegativeTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>§5.1 dormancy floor. Kept as options so a retune is a one-line change, not a rewrite.</summary>
    public float FloorSaturation { get; set; } = 0.22f;

    /// <summary>
    /// Raised from 0.60 once the ramp was seen on real cover art — 0.60 was
    /// calibrated against procedural gradients and compounds with Steam
    /// capsules that are already dark. Mirrors Winnow.App's Dormancy.BrightFloor.
    /// </summary>
    public float FloorBrightness { get; set; } = 0.68f;

    /// <summary>
    /// The §1 cool shift, so dormant art reads as cool rather than merely grey.
    /// Mirrors <see cref="CoverImaging.DefaultHueDegrees"/>.
    /// </summary>
    public float FloorHueDegrees { get; set; } = CoverImaging.DefaultHueDegrees;

    /// <summary>JPEG quality for the stored floor variant.</summary>
    public int DiskVariantQuality { get; set; } = 90;

    /// <summary>
    /// Steam's public CDN. The portrait capsule needs no authentication —
    /// verified 2026-08-23: <c>library_600x900_2x.jpg</c> returns 200 for real
    /// apps and a clean 404 for tools/redistributables.
    /// </summary>
    public string SteamCdnBaseUrl { get; set; } = "https://cdn.cloudflare.steamstatic.com/steam/apps";
}
