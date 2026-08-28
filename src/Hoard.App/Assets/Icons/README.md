# The mark

One drawing, three destinations, and they do not want the same treatment.

| File | Is | Read by |
|---|---|---|
| `dragon.svg` | the source drawing | nothing at runtime — it is the record |
| `dragon.ico` | 7 frames, 16→256 | `<ApplicationIcon>` (the exe) and `Window.Icon` (taskbar, alt-tab, window corner) |

**The caption mark is neither of these.** It is a `StreamGeometry` inlined in
`Views/MainWindow.axaml` as `DragonMark`, painted by a `Path` in `TextDim`, so it
recolours when the theme does and stays sharp at any DPI. See the comment on that
resource for why the fill rule is written out and why the ink is `TextDim`. The
geometry is scoped to that window on purpose: the mark lives in the caption, and
nothing else in the app draws it.

## Where dragon.ico came from

Generated with a throwaway SkiaSharp harness — `SKPath.ParseSvgPathData` over the
thirteen `d` attributes, `SKCanvas` to a bitmap per size, then the ICO container
written by hand. **No new package**: SkiaSharp is already in the tree under
Avalonia. To regenerate after editing the SVG, rebuild it from this description
rather than looking for a checked-in tool; it is thirty lines and keeping a
second build system in the repo for one binary is the worse trade.

- **Composition.** The artwork is scaled into a rounded tile (radius 0.1875 × size,
  the Windows 11 metric), `Ground #0F1C1E` behind, `Text #F0EDE7` in front. The
  tile is not decoration: a transparent icon in this palette is invisible on one
  of the two Windows taskbar themes, and which one is the user's choice.
- **Every frame is rendered from the vector at its own size.** None is a downscale
  of a larger one. This is the whole reason the file is 44KB rather than one PNG:
  the shell picks the frame that matches the surface, and a 256 stretched to 24 is
  mud. Verified with `PrivateExtractIconsW` against the built exe — Windows pulls
  16, 24, 32, 48 and 256 out at their native sizes.
- **16 and 24 carry a hairline dilation** (0.50px and 0.40px, stroked in the fill
  colour) because the mane and jaw details fall below one device pixel there.
  Measured rather than guessed: at 16px, 0.0 speckles into grey noise and 1.1
  merges the two horns into one lump; 0.4–0.55 keeps two distinct horns, the snout
  and the eye. 32 and up take no dilation and do not need it.
- **16px is the honest limit.** It reads as a horned head — you can tell it is a
  beast and you can pick it out of a taskbar — but the mane texture and the jaw
  are gone and it does not resolve as a *dragon*. A simplified small-size mark was
  tried and rejected: cropping to the skull loses the snout and the second horn,
  and the result is a pale blob, which is worse than a soft dragon.
- **DIB below 48, PNG at 64 and above.** Windows has read PNG frames since Vista,
  but the small sizes are where the widest range of shell surfaces look, and a DIB
  is what every one of them has always understood. `System.Drawing.Icon` cannot
  read the PNG 256 frame — that is GDI+ predating PNG-in-ICO, not a defect in the
  file; the shell reads it.

## The consent window

`Hoard.Auth.WebView` cannot reach `avares://Hoard/` — §5.1 keeps it off
`Hoard.App` — so its sign-in window takes the icon off the running application's
main window instead of off an asset. It therefore follows this file with no second
copy to keep in step. See `WebView2AuthPrompt.HostIcon`.

## Source & licence

`dragon.svg` was supplied by the project owner. viewBox `0 0 512 512`, thirteen
`<path>` elements, one flat fill, no gradients and no embedded rasters.
