# Fullscreen vector art

Original Winnow artwork: `activity.svg` draws an open journal with layered page edges and a ribbon bookmark;
`settings.svg` draws concentric contours. `controller.svg` adapts the bundled Kenney CC0
Xbox Series controller silhouette into a themed outline for the input map (see
`../Controller/License.txt`). These are decorative illustrations, not activity data.

`FullscreenVectorArt` reads only the paths in these bundled SVGs. Their `data-fill` and
`data-stroke` attributes name the shared theme brushes; SVG colour attributes make the
source files viewable outside the app. Keep both forms aligned when changing the artwork.
The controller's button glyphs are added separately from the Kenney assets in `../Controller`.
