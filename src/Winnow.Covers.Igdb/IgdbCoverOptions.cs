namespace Winnow.Covers.Igdb;

/// <summary>
/// Knobs for the IGDB gap-filling cover source. Defaults are the shipped values;
/// tests override the linger and the pre-warm so nothing depends on wall-clock
/// timing or on what happens to be in a real cache directory.
/// </summary>
public sealed class IgdbCoverOptions
{
    /// <summary>
    /// The IGDB image size token substituted into the cover URL path.
    ///
    /// <para><c>t_cover_big</c> — what <c>IIgdbClient</c> hands back — is
    /// 264x352 (verified live 2026-08-23). A grid tile is 148 DIP, which is
    /// 296px at 2x DPI and 592px at 4x, so <c>t_cover_big</c> is already soft on
    /// a retina display before the decoder starts. <c>t_cover_big_2x</c> is
    /// 528x704 from the same asset, which covers every
    /// <see cref="CoverImaging.WidthBuckets"/> entry up to 480 outright and the
    /// 640 bucket closely enough — and it is the same order of source
    /// resolution as Steam's 1200x1800 <c>library_600x900_2x.jpg</c>, so the two
    /// sources do not produce visibly different sharpness in one grid.</para>
    ///
    /// <para>IGDB covers are 3:4 where Steam capsules are 2:3. The tile renders
    /// <c>Stretch="UniformToFill"</c>, so the difference costs a small
    /// top-and-bottom crop, not a letterbox.</para>
    /// </summary>
    public string ImageSizeToken { get; set; } = "t_cover_big_2x";

    /// <summary>
    /// The IGDB image size token for screenshot assets.
    ///
    /// <para><c>t_screenshot_huge</c> is 1280x720 (verified live
    /// 2026-09-05 by unauthenticated GET against <c>images.igdb.com</c>
    /// using <c>co6m51</c>: <c>t_screenshot_med</c> 555x312 18,647 B;
    /// <c>t_screenshot_big</c> 940x529 38,580 B;
    /// <c>t_screenshot_huge</c> 1280x720 58,654 B; a fabricated
    /// <c>t_not_a_real_token</c> 404, so the CDN discriminates between
    /// tokens rather than serving anything for any path). A separate
    /// rendition from <see cref="ImageSizeToken"/> because covers are
    /// 3:4 portrait and screenshots are 16:9 landscape.</para>
    /// </summary>
    public string ScreenshotSizeToken { get; set; } = "t_screenshot_huge";

    /// <summary>
    /// Fullscreen landscapes use IGDB's documented 1080p size with its 2x suffix
    /// (up to 3840x2160). Source quality still limits detail; decoding never upscales.
    /// </summary>
    public string BackdropSizeToken { get; set; } = "t_1080p_2x";

    /// <summary>Image CDN root. Only the size token is rewritten; the host comes from IGDB's own URL.</summary>
    public string ImageHostPrefix { get; set; } = "https://images.igdb.com/";

    /// <summary>
    /// Appids per <c>ResolveBySteamAppIdsAsync</c> call. Matches
    /// <c>IgdbOptions.BatchSize</c>, which is what actually decides how many
    /// Apicalypse requests result.
    /// </summary>
    public int MaxBatchSize { get; set; } = 400;

    /// <summary>
    /// How long a forming batch waits for company before it goes.
    /// <see cref="CoverPipeline"/> admits six fetches at a time, so without a
    /// linger the batcher would see one appid per call and this source would
    /// issue one lookup per cover — the exact cost the batch endpoint exists to
    /// avoid. 250ms is below the threshold at which a user notices a tile
    /// arriving late and far above the cost of the gate handing over.
    /// </summary>
    public TimeSpan BatchLinger { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Resolve the previous run's negative markers in one batch the first time
    /// this source is asked anything.
    ///
    /// <para>Those markers are not a random sample: a <c>.none</c> in the cover
    /// cache is precisely a key that every previously registered source
    /// declined, which on a real library is the ~96 appids with no Steam
    /// capsule. Reading them costs one directory enumeration and turns the
    /// gap-fill from "one batch per six tiles as they scroll into view" into a
    /// single lookup that has already answered every question the grid is about
    /// to ask.</para>
    /// </summary>
    public bool PrewarmFromNegativeMarkers { get; set; } = true;
}
