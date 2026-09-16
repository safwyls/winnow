# The mark

Winnow's dragon head is the shared application mark.

| Representation | Use |
|---|---|
| `dragon.svg` | Source drawing; read by `FullscreenGlyphs` for the fullscreen mark and `LoadingDragon` for both loading screens |
| `dragon.ico` | Seven transparent frames, 16–256px; executable, taskbar, Alt-Tab, window, tray and Windows journal notification icon |
| `DragonMark` in `Views/MainWindow.axaml` | Desktop caption geometry, painted with `TextDim` |

`FullscreenGlyphs` reads the SVG paths with EvenOdd fill and applies the theme's `Text`
brush. The desktop caption embeds the same geometry to keep the mark sharp at any DPI and
recolour it with the theme. Keep both representations aligned when editing the drawing.
`LoadingDragon` traces each closed figure separately so detached pieces and inner details
receive a complete glow circuit. Keep the contour coverage test aligned with vector edits.
The asset name `dragon` describes the subject; the mascot's name is Winnow.

## Regenerating dragon.ico

From the repository root, run:

```powershell
dotnet run --file scripts/Generate-AppIcon.cs -- C:/Temp/winnow-icon-preview
```

The .NET 10 file-based tool renders the thirteen SVG paths with SkiaSharp at each frame
size and packages them in an ICO container. The optional output directory receives
transparent PNGs and light/dark contrast previews.

- **Composition.** The background is transparent, with no tile. `Text #F0EDE7` fills
  the dragon and a thin `Ground #0F1C1E` outline follows its contours so it remains
  readable on a light taskbar. A 5.5% inset leaves space for the outline.
- **Every frame is rendered from the vector at its own size.** None is a downscale
  of a larger one. The file contains 16, 24, 32, 48, 64, 128 and 256px frames:
  the shell picks the frame that matches the surface, and a 256 stretched to 24 is
  mud. Check the executable frames with `PrivateExtractIconsW` after changing them.
- **16 and 24 carry a hairline dilation** (0.50px and 0.40px, stroked in the fill
  colour) because the mane and jaw details fall below one device pixel there.
  32 and up take no dilation. Inspect both light and dark previews when changing
  the outline or small-frame weight.
- **At 16px the mark reads as a horned head.** The mane texture and jaw detail are not
  distinguishable at that size.
- **DIB through 48, PNG at 64 and above.** Windows has read PNG frames since Vista,
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
