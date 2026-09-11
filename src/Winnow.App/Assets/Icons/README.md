# The mark

Winnow's dragon head is the shared application mark.

| Representation | Use |
|---|---|
| `dragon.svg` | Source drawing; read by `FullscreenGlyphs` for the fullscreen mark |
| `dragon.ico` | Seven frames, 16–256px; executable, taskbar, Alt-Tab and window icon |
| `DragonMark` in `Views/MainWindow.axaml` | Desktop caption geometry, painted with `TextDim` |

`FullscreenGlyphs` reads the SVG paths with EvenOdd fill and applies the theme's `Text`
brush. The desktop caption embeds the same geometry to keep the mark sharp at any DPI and
recolour it with the theme. Keep both representations aligned when editing the drawing.
The asset name `dragon` describes the subject; the mascot's name is Winnow.

## Regenerating dragon.ico

Render the thirteen SVG paths with SkiaSharp (`SKPath.ParseSvgPathData`, then `SKCanvas`)
at each frame size and package the bitmaps in an ICO container. SkiaSharp is already an
Avalonia dependency. There is no checked-in icon-generation tool.

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
- **At 16px the mark reads as a horned head.** The mane texture and jaw detail are not
  distinguishable at that size.
- **DIB below 48, PNG at 64 and above.** Windows has read PNG frames since Vista,
  but the small sizes are where the widest range of shell surfaces look, and a DIB
  is what every one of them has always understood. `System.Drawing.Icon` cannot
  read the PNG 256 frame — that is GDI+ predating PNG-in-ICO, not a defect in the
  file; the shell reads it.

## The consent window

`Winnow.Auth.WebView` cannot reach `avares://Winnow/` — §5.1 keeps it off
`Winnow.App` — so its sign-in window takes the icon off the running application's
main window instead of off an asset. It therefore follows this file with no second
copy to keep in step. See `WebView2AuthPrompt.HostIcon`.

## Source & licence

`dragon.svg` was supplied by the project owner. viewBox `0 0 512 512`, thirteen
`<path>` elements, one flat fill, no gradients and no embedded rasters.
