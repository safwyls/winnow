# Fullscreen vector art

Original Winnow artwork: `activity.svg` draws a quiet mountain landscape and paths;
`settings.svg` draws concentric contours; `controller.svg` supplies the controller outline
used in the input map. These are decorative illustrations, not activity data.

`FullscreenVectorArt` reads only the paths in these bundled SVGs. Their `data-fill` and
`data-stroke` attributes name the shared theme brushes; SVG colour attributes make the
source files viewable outside the app. Keep both forms aligned when changing the artwork.
The controller's button glyphs are added separately from the Kenney assets in `../Controller`.
