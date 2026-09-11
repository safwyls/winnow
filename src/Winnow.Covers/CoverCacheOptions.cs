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
    /// Ceiling on decoded pixel bytes held in memory, counting every layer a
    /// slot asked for.
    ///
    /// <para><b>32 MiB, sized from a measured screenful.</b> A 148-DIP tile at
    /// 100 % DPI decodes in the 160 bucket, so a two-layer cover is
    /// 160×240×4×2 = 300 KB; a 1280×820 window realizes about 24 tiles plus a
    /// buffer row, which is 9 MB. 32 MiB is therefore roughly three screenfuls
    /// of scrollback there, and about a third of one on a 4K wall at 200 % DPI
    /// where the same tile lands in the 320 bucket.</para>
    ///
    /// <para><b>Why a scrollback figure is the right one.</b> This is not a
    /// floor for what can be on screen: a lease keeps art alive whether or not
    /// the LRU still indexes it (<see cref="CoverLeasePool"/>), so an evicted
    /// cover that a tile is still drawing is not disposed and not re-decoded.
    /// The budget only decides how much art that nothing is drawing stays
    /// decoded for an immediate scroll back. The previous 128 MiB was not
    /// derived from anything and held 455 covers on the author's library —
    /// measured, docs/spikes/memory-footprint.md — which is fifteen screenfuls
    /// of art nobody was looking at.</para>
    /// </summary>
    public long MaxDecodedBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>
    /// In-flight decodes. A decode is a JPEG read plus, for a two-layer
    /// request, a colour-matrix pass — CPU-bound work with a transient bitmap
    /// each — and a fast scroll can ask for one per realized tile. Half the
    /// cores, at least two and at most six: enough that the visible rows fill
    /// without waiting, few enough that a burst cannot hold a dozen transient
    /// decodes at once or starve the thread pool the UI shares.
    /// </summary>
    public int MaxConcurrentDecodes { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

    /// <summary>Maximum admitted slots, including running decodes. Excess requests remain retryable placeholders.</summary>
    public int MaxPendingLoads { get; set; } = 128;

    /// <summary>
    /// Width the floor variant is stored at on disk. 640 is ≥ the largest
    /// display bucket, so no display size ever needs the full 1200px source
    /// re-processed, and the colour-matrix pass runs once on a small bitmap.
    /// </summary>
    public int DiskVariantWidth { get; set; } = 640;

    /// <summary>How long a "this app has no capsule" result stays believed.</summary>
    public TimeSpan NegativeTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Shared §5.1 endpoint; a cache-only override would disagree with procedural art.</summary>
    public float FloorSaturation => (float)DormancyStyle.SaturationFloor;

    /// <summary>
    /// Uses the same source as the ramp, placeholders and XAML tokens.
    /// </summary>
    public float FloorBrightness => (float)DormancyStyle.BrightnessFloor;

    /// <summary>
    /// The §1 cool shift, so dormant art reads as cool rather than merely grey.
    /// Mirrors <see cref="CoverImaging.DefaultHueDegrees"/>.
    /// </summary>
    public float FloorHueDegrees => (float)DormancyStyle.HueDegrees;

    /// <summary>JPEG quality for the stored floor variant.</summary>
    public int DiskVariantQuality { get; set; } = 90;

    /// <summary>
    /// Steam's public CDN. The portrait capsule needs no authentication —
    /// verified 2026-08-23: <c>library_600x900_2x.jpg</c> returns 200 for real
    /// apps and a clean 404 for tools/redistributables.
    /// </summary>
    public string SteamCdnBaseUrl { get; set; } = "https://cdn.cloudflare.steamstatic.com/steam/apps";
}
