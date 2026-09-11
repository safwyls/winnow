# Merge queue prototype

This folder preserves an HTML prototype of the desktop Merges screen. Its example games,
counts, copy and bundled tokens are illustrative snapshots, not current requirements.

The current screen and interaction contract are in [design-system.md §6](../../design-system.md#6-components).
The production implementation is `src/Winnow.App/Views/MergeQueueView.axaml` with shared
identity operations and a separate fullscreen presentation.

To inspect the prototype, serve this directory over HTTP and open `Merge Queue.dc.html`.
Keep `support.js`, `assets/` and `_ds/` beside it. These files support the prototype only;
use the app's token dictionary when changing Winnow.
