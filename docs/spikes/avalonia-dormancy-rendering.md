# Spike: Dormancy rendering in Avalonia 11.x

**Settles:** design-system.md §5.4 [VERIFY]. Researched 2026-08-23 against Avalonia 11.3.x (current stable line).

## Findings on the four candidate approaches

1. **Built-in Effect API — NOT AVAILABLE.** Avalonia 11.x ships exactly three effects: `BlurEffect`,
   `DropShadowEffect`, `DropShadowDirectionEffect` (+ immutable variants). No color-matrix/saturation
   effect exists, and there is **no public API for authoring custom effects** — effects are hardcoded in
   the Skia backend. A `ColorFilterEffect` feature request ([#19786](https://github.com/AvaloniaUI/Avalonia/issues/19786))
   is open/unscheduled; the SKSL shader-effect PR ([#17981](https://github.com/AvaloniaUI/Avalonia/pull/17981))
   was **closed unmerged in June 2026** (team wants a renderer-agnostic design, none exists yet).
   §5.4's option 1 as written is therefore unavailable → per the doc's own rule, use option 2.
2. **`ICustomDrawOperation` + `SKColorFilter.CreateColorMatrix` — viable, but second choice.** Public,
   works on all three desktop platforms (Skia is the only backend). Caveats: `Render()` runs on the
   **render thread** (op must carry only immutable state; no UI-thread objects), it bypasses scene-graph
   caching, each `ISkiaSharpApiLeaseFeature.Lease()` flushes Avalonia's batched drawing, and you own
   `Equals`/`Dispose`/recycling correctness per tile. Fine for one canvas; 100+ leased ops per scroll
   frame is measurable overhead and boilerplate risk.
3. **`CompositionCustomVisual` — overkill.** Designed for render-thread animation loops (video, ink).
   Same Skia-level code as (2) plus channel/message plumbing per tile. No benefit here.
4. **Pre-computed variants — YES, but upgraded:** two layers cross-faded by Opacity give a *continuous*
   ramp, not the stepped one §5.4 assumed. See below.

## RECOMMENDATION

**Primary — two-layer continuous cross-fade (only stable, public APIs):**

- At cover decode time (already off-thread, at display resolution), additionally produce one
  **floor variant**: saturation 0.22, brightness 0.60, via a single SkiaSharp color-matrix pass
  (or `Avalonia.Media.Imaging` render into a `RenderTargetBitmap` — prefer SkiaSharp in the cache
  pipeline). Keep it in the in-memory cache next to the vivid bitmap; disk-caching optional.
- Tile = floor image with the **vivid image stacked on top**; the vivid layer's `Opacity = α`.
  Per-pixel source-over blending makes this an exact linear interpolation between the two endpoints,
  so α is continuous per game: **α = (S − 0.22) / 0.78** from the §5.1 saturation column. Brightness
  then tracks within ≤ 0.04 of the §5.1 table at every row — visually indistinguishable.
- **Hover restore** = animate vivid layer's Opacity to 1.0 with a 140ms `DoubleTransition`
  (GPU-composited, identical on Windows/Linux/macOS). Reduced motion: clear `Transitions`.
  "Disable ramp" setting: force α = 1.
- Cost: one extra decoded bitmap per *visible* tile (~130 KB at 148×222 @1x → roughly 13–30 MB for
  100 visible tiles incl. 2x DPI) and one cheap CPU pass per cover at decode. No render-thread code,
  no custom-op lifetime bugs, plays cleanly with virtualization recycling.

**Fallback/escalation trigger:** move to approach (2) (`ICustomDrawOperation` per tile) only if
profiling shows the doubled bitmap memory is unacceptable at max density, or design later demands a
matrix path the two-endpoint lerp can't express (e.g. hue cool-shift independent of saturation).

### Code sketch

```xml
<!-- GameTile.axaml (inside the tile) -->
<Panel>
  <Image Source="{Binding FloorCover}" Stretch="UniformToFill"/>
  <Image Source="{Binding VividCover}" Stretch="UniformToFill"
         Opacity="{Binding VividAlpha}">   <!-- α = (S-0.22)/0.78, 1.0 when hovered -->
    <Image.Transitions>
      <Transitions>
        <DoubleTransition Property="Opacity" Duration="0:0:0.140" Easing="CubicEaseOut"/>
      </Transitions>
    </Image.Transitions>
  </Image>
</Panel>
```

```csharp
// Cover cache pipeline: produce the floor variant once per decode (off UI thread).
static SKBitmap MakeFloorVariant(SKBitmap vivid, float sat = 0.22f, float bright = 0.60f)
{
    // Standard Rec.709 luma saturation matrix, then uniform brightness scale.
    float inv = 1 - sat;
    float r = 0.2126f * inv, g = 0.7152f * inv, b = 0.0722f * inv;
    var m = new float[] {
        (r + sat) * bright, g * bright,         b * bright,         0, 0,
        r * bright,         (g + sat) * bright, b * bright,         0, 0,
        r * bright,         g * bright,         (b + sat) * bright, 0, 0,
        0,                  0,                  0,                  1, 0 };
    var outBmp = new SKBitmap(vivid.Width, vivid.Height, vivid.ColorType, vivid.AlphaType);
    using var canvas = new SKCanvas(outBmp);
    using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(m) };
    canvas.DrawBitmap(vivid, 0, 0, paint);
    return outBmp; // convert both to Avalonia Bitmap via WriteableBitmap/encode as the cache already does
}
```

Hover: set an `IsPointerOver`-driven style (or code-behind) that overrides `VividAlpha` to 1;
the transition animates both directions. Never mutate pixels on the UI thread (§5.4 rule holds).

## Token-file verifications

| Question | Answer |
|---|---|
| `TextBlock.FontFeatures` | Yes — **introduced in 11.1.0** (`TextElement.FontFeaturesProperty`, `FontFeatureCollection`; absent in 11.0.0, present in 11.1.0 source). Syntax: `FontFeatures="+tnum"` (HarfBuzz tags, comma-separated). Any 11.1+ works for `tnum`. |
| `TextBlock.LetterSpacing` | Yes — **since 11.0.0** (inherited attached `double`, device pixels — convert the `+0.06em` tokens to px at the token's font size). |
| `ItemsRepeater` in-box? | **No — separate NuGet package `Avalonia.Controls.ItemsRepeater`** since 11.0 ([PR #10112](https://github.com/AvaloniaUI/Avalonia/pull/10112)). |
| Virtualization guidance | Maintainers plan to **stop supporting ItemsRepeater after 12.0** and consolidate on `ItemsControl`, with a `VirtualizedUniformPanel` planned *before* obsoleting it ([discussion #16829](https://github.com/AvaloniaUI/Avalonia/discussions/16829)). In 11.x today, in-box virtualization is `ItemsControl`/`ListBox` + `VirtualizingStackPanel` — **single-axis only; no in-box virtualizing wrap/uniform grid yet**. For the cover grid: use **ItemsRepeater + UniformGridLayout** for v1 (it's the only virtualizing grid available), isolate it behind one view so a swap to `ItemsControl` + `VirtualizedUniformPanel` (or a row-chunked `VirtualizingStackPanel`, one row VM = N tiles) is cheap. Known ItemsRepeater issues to avoid: don't nest repeaters ([#9427](https://github.com/AvaloniaUI/Avalonia/issues/9427)), don't wrap in `LayoutTransformControl` ([#13875](https://github.com/AvaloniaUI/Avalonia/issues/13875)). |

## Sources

- Effects surface / no custom effects: [#19786 ColorFilterEffect request](https://github.com/AvaloniaUI/Avalonia/issues/19786) · [#18657 custom-effects discussion](https://github.com/AvaloniaUI/Avalonia/discussions/18657) · [#17981 SKSLEffect PR, closed June 2026](https://github.com/AvaloniaUI/Avalonia/pull/17981)
- Custom rendering & threading: [Avalonia docs — custom rendering](https://docs.avaloniaui.net/docs/graphics-animation/custom-rendering) · [native interop / ISkiaSharpApiLeaseFeature](https://docs.avaloniaui.net/docs/app-development/native-interop) · [wieslawsoltes/CustomDrawingAvaloniaExamples](https://github.com/wieslawsoltes/CustomDrawingAvaloniaExamples)
- ItemsRepeater: [package split PR #10112](https://github.com/AvaloniaUI/Avalonia/pull/10112) · [future of ItemsRepeater #16829](https://github.com/AvaloniaUI/Avalonia/discussions/16829) · [ItemsControl how-to](https://docs.avaloniaui.net/docs/how-to/itemscontrol-how-to)
- FontFeatures/LetterSpacing: verified directly against tagged source — [TextBlock.cs @ 11.0.0](https://github.com/AvaloniaUI/Avalonia/blob/11.0.0/src/Avalonia.Controls/TextBlock.cs) (LetterSpacing yes, FontFeatures no) vs [TextBlock.cs @ 11.1.0](https://github.com/AvaloniaUI/Avalonia/blob/11.1.0/src/Avalonia.Controls/TextBlock.cs) (FontFeatures yes) · [Typography docs](https://docs.avaloniaui.net/docs/styling/typography)
