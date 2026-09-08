namespace Winnow.Covers;

/// <summary>
/// How many layers of the dormancy cross-fade a request needs decoded.
///
/// <para>The ramp is drawn as two stacked bitmaps (design-system.md §5.4), so a
/// wall tile, a list row, a feed card and a merge row all need both. Every other
/// surface — the detail modal, the screenshot strip, the lightbox, the IGDB
/// candidate rows, the metadata editor's previews — draws the art at full
/// saturation and would keep a second bitmap it never paints. Asking for
/// <see cref="Vivid"/> is what stops that second decode happening.</para>
///
/// <para>The value is part of the cache slot key, so the two shapes never
/// collide; a cached <see cref="VividAndFloor"/> entry still answers a
/// <see cref="Vivid"/> request, because extra pixels the caller will not draw
/// are not missing pixels.</para>
/// </summary>
public enum CoverLayers
{
    /// <summary>Vivid art alone. One bitmap.</summary>
    Vivid,

    /// <summary>Vivid art and the desaturated floor variant under it. Two bitmaps.</summary>
    VividAndFloor,
}
