# Electron themes

Theme Studio changes appearance and layout without editing code. Appearance profiles share
those choices as JSON. Avalon also reads original authored Winnow palette JSON files.
Developer themes replace React screens and the application shell using theme API 1.

Avalon, Afterglow, Rift and Catalogue use the same `ThemeDefinition` interface as installed themes.
Avalon is the default and retains Winnow's original palette, typography, dormancy treatment
and separate desktop/fullscreen compositions. Afterglow uses a panoramic featured game and
editorial typography. Rift pairs floating covers with artwork portals, a focused Discover
deck and a dense Library gallery. Catalogue uses an index and a reading desk.
All retain the same library, recommendations, journals, and backend commands. These themes
belong to the independent Electron frontend. Authored Avalon palettes share the original
themes folder; developer themes and backend provider plugins remain separate.

## Change the appearance

Open **Theme Studio** from the palette button. Choose a composition, then adjust:

- Winnow, Afterglow, Moonstone, Paper trail, or Blue hour colors, plus a custom accent. Expand **Customize every
  color** to set background, panels, raised surfaces, text, secondary text, borders, and the
  secondary accent individually, using a picker or a hex value.
- Editorial, sans serif, or monospace typography using bundled fonts.
- Interface size from 85% to 130%, spacing, corner radius, artwork shading, and reduced motion.
- Navigation position, library card style, and game-detail arrangement for compositions
  that use those shared layout controls. Rift provides its own cover-size control.
- Artwork materials: Matte, Satin, Foil, or no surface finish; finish intensity; selective
  foil on bright areas in silver, gold, or holographic colors; brightness cutoff and foil
  strength; pointer tracking, floating depth, and maximum tilt from 0° to 12°.
- Afterglow Discover section order and visibility. At least one section remains visible.
- Additional controls declared by the selected developer theme.

Changes apply immediately and save automatically. **Reset** restores the default Avalon appearance.
Switching compositions saves each design's appearance and layout independently. Returning
restores those choices; the first Rift selection starts with Moonstone and modern typography.
Interface size and reduced motion remain shared across designs. The optional `designs`
profile map holds at most 32 inactive appearance/layout pairs, validated like the active pair.
Older profiles without this map remain valid.
Saved profiles keep their selected composition, palette and layout when the default changes.
First selection of Afterglow uses its original warm palette, editorial typography and top navigation.
The studio checks text and accent contrast against the current custom backgrounds and warns
when a pair has less than 4.5:1 contrast. Buttons filled with the accent use whichever of black
or white has greater contrast. Individual color resets return to the selected preset;
choosing a preset clears custom palette overrides.
System reduced-motion preferences still apply when the explicit preference is off.
Artwork settings apply to themes that opt into the shared effect components. Afterglow's
covers stay still and unlit, with captions over the artwork. It retains saved material
settings for other themes. Catalogue's Library continues to use materials and side previews.
Rift reuses the same card materials and exposes **Portal roundness**, **Portal edge shape**,
**Portal edge activity**, **Star field**, **Star brightness** and **Cover size** as theme
settings. Zero activity holds the rim still; 40 is the normal pace and 100 is about four
times faster. The star field draws only on creation or resize; brightness changes opacity.

Avalon's **Avalon palette** setting selects Winnow, Nightshift, Tungsten, Box art, Bottle
green, SilkCircuit, SilkCircuit Dawn, Rosé Pine or Rosé Pine Dawn. These are the original
bundled palettes, including the light variants and their derived colors. Fresh profiles and
missing palette IDs select the complete Winnow palette. Earlier Studio profiles with custom
colors or a different shared palette retain those choices. **Studio colors**
uses the shared color controls instead; an original palette takes precedence over those
controls until Studio colors is selected. The selected palette applies to the document
root, so portaled dialogs and native form controls follow it. Avalon keeps the original
Bricolage Grotesque, Plus Jakarta Sans and IBM Plex Mono typography. **Dim dormant covers**
controls its gradual desaturation; hover and focus restore the cover's color.

Avalon's desktop recommendation shelves keep one row of five readable covers, from 180px
to 240px wide. Narrow windows scroll that row; arrow keys reveal the selected cover and
carry its column between shelves. Fullscreen Home retains adjacent shelves during a 220ms
slide, with immediate destination focus and inactive outgoing rows. Rapid reversal starts
at the current position. Reduced motion, viewport changes and distant jumps settle directly
at the destination. The cover row and shelf rail share a bottom anchor; Home sizes covers
from the unscaled canvas so a smaller interface adds room without enlarging its covers.
Each shelf retains its horizontal overflow page; moving between shelves carries the current
visible column and clamps shorter pages. Only the current page's covers are realized.
Fullscreen Library keeps two visible rows, retaining overlapping covers during each vertical
slide. Arrows, Page Up/Down and the wheel share its selection state; filtering follows the
selected game when it remains in the results. Returning from another section restores the
selected game and row window. Each fullscreen collection and saved list remembers its
own position. LT/RT cycles All games, Installed, Never played and Patched. Choosing one
clears the fullscreen search and filters; desktop collection changes retain their separate
search. Fullscreen filter drafts include collection and sort: Apply or controller Y commits
them together, while Cancel or B discards them. Clear filters keeps the draft sort and
clears the draft collection. Desktop keeps inline Library search. Fullscreen Ctrl+K,
the search button and controller View open a dedicated Search page over the complete
visible library. Its query is independent of Library filters. Enter search opens the
keyboard; Go to results enters the visible grid. Up from the first row reaches that
control, and Down returns to the same column. LT/RT and Page Up/Down move two rows;
the footer shows the visible row range. Details returns to the retained query and result,
and Back restores the page that opened Search.

Both Library presentations keep manual lists in their stored order, with bounded move
actions and removal that keeps the game in the library. List names stay alphabetical
after refresh and rename. Adding from a cover, Home or Details targets those games while
retaining the current collection and selection. Saving a live cut suggests a name from
its rules and opens the committed list once Library has received it. Reopening restores
the saved rules; leaving a live list removes its contributed rules, while leaving a
manual list keeps the user's filters. Details offers membership ticks for manual lists
and explains the empty state when only live lists exist.

Desktop presents manual and live lists in separate collapsible rail sections. Expansion
lasts for the session, and each row counts projected games rather than stored release
IDs. Clicking the active row leaves its list. A live list opens the inline filter panel;
leaving the list clears its contributed rules and keeps that panel open. Toolbar and
result controls reserve the panel's width. Fullscreen retains its own list selector and
returns to Browse without automatically opening the filter page.

The desktop rail scrolls its collections above a fixed footer. New list opens keyboard
accessible Static list and Live list choices; the Settings cog stays alongside it. The
live choice snapshots the current cut, suggests its first two rules and opens the saved
list. Static creation starts empty. Cancelling either naming prompt returns focus to
New list, and pending writes prevent cancellation or duplicate submission.

Avalon opens desktop game Details as a modal over the retained library, with an 82×123
cover and five sections: Overview, Activity, Updates, Journal and Library. The bounded
reading area retains each section's scroll position while its header stays fixed. Closing
returns focus and scroll to the originating game or identity-review member. Fullscreen
uses a separate cinematic composition with four sections, BodyFont throughout, a 28/72
Overview split and two complete-aspect screenshot previews. About and Play history open
reading pages; Escape returns one level and restores the action that opened them.
Both surfaces keep matching, metadata and artwork tools under More, together with refresh,
game links and install-folder access when available. Edits and library operations use the
same shared controls as other compositions. Afterglow, Rift and Catalogue retain the shared
Details screen and its existing section arrangement.

Avalon's Home, fullscreen Library and Details backdrops use the backend's ranked candidates, including saved
artwork, grouped store identities, landscape art, screenshots and portrait fallback.
Visible artwork remains while its replacement loads. Fullscreen completes each 180 ms
crossfade before showing the latest queued result; reduced motion replaces it immediately.
Steam heroes fit their whole composition from 21:9 upward, with independent geometry for
each transition layer. Desktop Details uses the palette's Surface color at 92% over art;
fullscreen uses Ground gradients around its reading areas. The renderer cancels obsolete
image requests and releases decoded images when replaced or detached.
Fullscreen Library retains one backdrop across cover selections and dims the complete
layer to 45%; filter and library-tool pages release it while they replace Browse.

Avalon's **Theme typography** controls choose heading, interface and data fonts separately.
The bundled families are always listed. **Find installed fonts** queries local family names;
it does not read font files. You can also enter an installed family name. Missing families
fall back to the bundled font for that role. **Theme text size** ranges from 80% to 120%,
independent of interface zoom, cover dimensions and icon sizes. Each original palette keeps
its own fonts and size. **Reset theme typography** removes that palette's overrides and
restores its authored fonts and size, or the bundled defaults when none are authored.
Fullscreen reserves chrome and Home hero space for the supported 120% theme size, so live
font changes leave cover geometry stable. The description still occupies two lines at the
chosen text size; unused height remains between the hero and its bottom-anchored shelf.

**Authored palettes** reads schema-1 JSON files from the library's existing `themes` folder,
including installs that still use the legacy data location. **Open themes folder** opens
that directory; **Export palette as JSON** writes a new template without overwriting any
file. The export includes the effective role typography, eight seed colors, fitted
proportions, residual color overrides, and any authored opening preferences. Change the
copy's ID before using it as another palette. The same files work in Avalonia.

The catalog reads up to 64 top-level JSON files, each at most 256 KB. It supports comments
and trailing commas, validates schema, IDs, colors, proportions, fonts and defaults, and
reports file-specific errors without disabling other palettes. Contrast and color-role
warnings are advisory. File changes reload after a short debounce; **Reload authored
palettes** is available if file watching is unavailable. Reloading preserves selection by
ID and explicit font overrides. Removing the selected custom palette restores Winnow.
A local copy may replace an authored bundled palette with the same ID; the four calibrated
house IDs are reserved. These palette files contain data only and never execute code.

Avalon's **Window appearance** controls use the backend's existing transparency, backdrop,
pane reach and floating/flush layout preferences. Desktop requests Acrylic or Mica on
Windows 11 22H2 or later. High contrast, reduced transparency, remote sessions, unsupported
platforms and failed native requests keep solid surfaces. Fullscreen stays solid. The
original two-tier alpha ramp paints the shell once and each pane once; covers and popup
surfaces stay opaque, and whole-window opacity is unchanged. The slider reports the original
4.5:1 metadata contrast limit against a light desktop. Stored values win on load; selecting
a palette applies any explicit opening defaults authored by that palette.
Electron reports whether a material request was supported and issued, but exposes no getter
for the compositor's actual material. A successful request is not proof of the visible OS
effect. Winnow keeps its normal native window frame.

Rift's Discover arrows, neighboring covers and left/right keys shuffle the deck in the
chosen direction. This movement respects the shared reduced-motion preference. Its
outer-card animation stays separate from the inner artwork's pointer tilt and materials.
Keyboard focus, reduced motion, and disabling **Follow the pointer** use a steady light
and level card.
**Movement and depth** explains when the profile or system preference pauses motion and
disables the affected controls without discarding their saved values. The notice can turn
off the profile's **Reduce motion** setting when the system allows motion; it never
overrides the system preference. Accent changes preserve all motion and artwork settings.
Setting maximum tilt to zero keeps the lift; switching off **Floating artwork** removes
the lift and shadow without turning off the material finish. Surface finish and highlight
foil can be disabled independently.

**Export profile** writes a JSON file containing appearance, layout, selected theme ID, and
theme-specific settings. **Import profile** validates its version, known fields, choices,
and numeric limits before applying anything. Profiles contain no JavaScript, CSS, assets,
backend credentials, or library records. Importing a profile does not install its theme.
If the theme is unavailable, Winnow uses Avalon. The unavailable theme's appearance remains
in the saved design map for a later return after installation.

Profiles are stored in the Electron user-data directory under
`libraries/<data-directory-hash>/preferences.json`. Different backend data directories have
separate profiles and installed themes. Palette and typography edits stay in this profile.
Window appearance controls share the original backend preferences with Avalonia.
When no Electron profile exists, the first launch reads the backend's `appearance.theme`
after loading authored palettes, and selects its matching Avalon palette. The legacy
default identifier maps to Winnow only when no authored palette claims that ID. Valid palette entries
in `appearance.typography` import at the same time; one invalid entry does not discard the
other palettes' fonts. Existing Electron profiles take precedence, and later palette
changes stay in the Electron profile. If the preference
cannot be read, the app keeps the initial profile unsaved so restarting can retry; an
explicit appearance edit or reset may still be saved.

Unpackaged development runs also accept the original capture switches:
`--theme=<palette>`, `--transparency=<0-100>`, `--transparent`,
`--backdrop=acrylic|mica`, `--wall=on|off` and `--layout=floating|flush`.
Any of these starts a session-only appearance override. Authored palettes are loaded
before resolving the requested ID. Existing profile colors/fonts are not imported,
and subsequent appearance edits remain live in memory without writing either the
Electron profile or backend appearance preferences. Unrelated library preferences
still persist. Packaged builds ignore these capture switches.

An explicit `--data-dir` also redirects Electron's user-data and Chromium state beneath
`<data-dir>/electron-userdata` before the window starts.

## Install a developer theme

1. Obtain a theme folder from an author you trust. Unpack it if it arrived as an archive.
2. Choose **Install a theme** in Theme Studio and select the folder containing `theme.json`.
3. Review the native trust prompt. Choosing **Trust and install** copies the package into
   this frontend's theme directory and selects it.

Developer themes run JavaScript in the renderer. They can read library information and use
the frontend's supported commands, including editing library data and launching games. They
are trusted frontend extensions, not passive color files. They receive no backend bearer
token, Node.js access, arbitrary filesystem bridge, or general shell-command bridge.
The renderer's content security policy restricts network and executable content.

A package is limited to 512 files and 32 MB. Symbolic links, unsupported files, unsafe
paths, reserved identifiers, and incompatible API versions are refused. Installing the same
ID replaces the previous package after the replacement is validated. Increment the package
version when distributing changes; the entry module is cached by its versioned URL for the
lifetime of the renderer. Reload the window after reinstalling changes to helper modules,
whose relative import URLs may remain the same.

If loading or rendering fails, the built-in frontend remains available. The persistent
recovery control and **Ctrl+Shift+T** restore Avalon. For a theme that prevents normal
interaction, use **View → Recover bundled appearance** (**Ctrl+Shift+R**) or start the app
with `--safe-theme`. That startup mode ignores the saved profile. A rendering error boundary
cannot interrupt an infinite JavaScript loop; restarting with `--safe-theme` is the recovery
path for that case.

## Author a theme

The smallest package has a manifest and an ES module:

```text
reading-room/
  theme.json
  index.mjs
  style.css
```

```json
{
  "id": "reading-room",
  "name": "Reading room",
  "version": "1.0.0",
  "apiVersion": 1,
  "description": "A quiet reading desk for your games.",
  "entry": "index.mjs",
  "css": "style.css"
}
```

`entry` is required; `css` and `description` are optional. Asset paths are relative to the
package folder and cannot traverse outside it. IDs use lowercase letters and digits with
periods or hyphens between segments. Versions use `major.minor.patch`, optionally with a
prerelease suffix. `apiVersion` describes the **theme contract**, separately from the backend
HTTP API version and the package's own version.

The module exports a default `ThemeDefinition`. Winnow installs a frozen
`window.WinnowThemeSDK` object before loading theme code. It supplies the host React instance,
`createElement`, `defineTheme`, and the `settings` helper. Do not bundle a second React.
For a TypeScript/JSX authoring project, compile to browser ES modules and configure React as
an external that resolves to this SDK. There is no Node.js runtime in the theme.

```js
const { createElement: h, defineTheme } = window.WinnowThemeSDK;

function Shell(context) {
  return h('div', { className: 'my-theme' },
    h('nav', { 'aria-label': 'Main navigation' },
      h('button', { onClick: () => context.setPage('library') }, 'Library'),
      h('button', { onClick: () => context.setPage('studio') }, 'Appearance')),
    h('main', { id: 'main-content', tabIndex: -1 }, context.children));
}

export default defineTheme({
  apiVersion: 1,
  id: 'my-theme',
  name: 'My theme',
  Shell
});
```

The complete working example is
[`src/Winnow.Electron/examples/themes/reading-room`](../src/Winnow.Electron/examples/themes/reading-room).
It replaces the shell, Discover, and Library using a different visual structure. It keeps
host Details, Journal, and Settings, and declares two editable settings. Its recommendation
covers reuse the host's material effects and game previews inside its own buttons, with
gold highlight foil. It uses no build tools: install that folder directly to try it.

### Viewport and scrolling

The host supplies a bounded viewport that accounts for interface scaling and status banners.
Shells should fill `height: 100%`, keep navigation and footer outside scrolling content,
and give the content pane `min-height: 0; overflow: auto`. Use `minmax(0, 1fr)` for the
scrollable row in a grid, or `flex: 1; min-height: 0` in a column flex layout. The host's
viewport also scrolls as a fallback for packages that have not adopted this layout.

The built-in Library fills its content pane and scrolls its results and index separately.
Library tools fill the same available width across all tabs on desktop and fullscreen.
Actions that navigate to game details use `View game`; launching remains a separate action.
Shared artwork fills the size assigned by its parent; set a height or aspect ratio on the
`Artwork` frame. Loading, missing artwork, and the decoded image all occupy that same frame.
The details hero uses a responsive height from 220 to 420 pixels, independent of image ratio.
Afterglow defaults to filled 2:3 portrait cards with no materials, lift or tilt. Hover or
keyboard focus reveals a shaded caption inside the cover with the title, storefront,
recommendation reason or bucket, playtime and installation status. The caption does not
change the card's height; long titles use up to four lines and the button retains the full
accessible name. Missing or failed artwork keeps its caption visible, as do touch devices.
Existing saved landscape and record preferences remain intact. The Library fits more
portrait columns into short windows so a focused caption stays inside the reading area.
Short windows use tighter navigation and library spacing. At less than 480 logical pixels
of available height, inline host cards use compact thumbnails beside titles and filters
stay on one row. These breakpoints follow the scaled content viewport, including at 130% size.

Afterglow rotates up to six featured recommendations every nine seconds while at least half
the hero is visible in the focused window. Interaction and pending feedback pause it;
manual selection pauses until Resume. System or profile reduced motion disables automatic
rotation and keeps manual navigation. Catalogue and developer themes retain their own
Discover compositions.

### Public screen contract

The TypeScript source of truth is
[`src/Winnow.Electron/src/shared/theme.ts`](../src/Winnow.Electron/src/shared/theme.ts).
Each optional screen is a React component receiving the same `ThemeContext`:

| Field | Purpose |
| --- | --- |
| `mode` | `desktop` or `fullscreen`; themes must make both usable. |
| `page`, `selectedWorkId` | Current destination and selected game, if any. |
| `games` | Current `LibraryGame[]` from the public backend. IDs are numbers. |
| `feed`, `loading` | Recommendation snapshot, if available, and library loading state. |
| `profile` | Validated appearance, layout, and theme-specific settings. |
| `setPage(page)` | Navigate to Discover, Library, Search, Journal, Settings, Studio, or Details. |
| `openGame(workId)` | Select a game and navigate to its details. |
| `closeGame?()`, `previousPage?` | Return from details and inspect the preceding destination; optional additions for older API-1 hosts. |
| `openSearch?()`, `closeSearch?()` | Open the theme's fullscreen Search or the host's inline Library search, and return to the exact originating page. |
| `editText?(input)` | Open the host's controller keyboard for a mounted text field. |
| `toggleFullscreen()` | Change the window mode. |
| `children` | The active screen, supplied to the shell. |
| `renderScreen(page?)` | Render a host screen, bypassing theme overrides to avoid recursion. |
| `actions.launch(ownershipId)` | Invoke the host's supported play/install command for a copy. |
| `components.GameCard` | Host card with authenticated artwork; takes `game`, optional `reason`, `onOpen`, `presentation`, `effects`, and `preview`. |
| `components.Impression` | Visibility-aware recommendation exposure wrapper; takes `releaseId`, `shelfId`, and children. |
| `components.Artwork` | Authenticated artwork without a card layout; takes `workId`, optional `hero`, `className`, and `eager`. |
| `components.ArtworkEffects` | Reusable material and depth surface around artwork; takes `children`, optional `className`, `effects`, and `interactionRef`. |
| `components.GamePreview` | Game information flyout around a theme-owned trigger; takes `game`, `children`, optional `reason`, `disabled`, and `className`. |
| `components.PortalSurface?` | Reusable fixed-plane reveal with optional cached artwork, shape/activity options, cursor origin and full-view expansion. Older API-1 hosts may omit it. |

A definition can replace `Shell`, `Discover`, `Library`, `Search`, `Details`, `Journal`, and `Settings`.
`Search` is optional within API 1. Themes that omit it keep the existing Library search route.
Optional `defaults` supplies partial `appearance` and `layout` maps for the first explicit
selection. They pass the same validation as profiles. Saved design choices, startup hydration,
profile imports and changes made while an external theme is loading take precedence.
Omitted screens use host implementations. Theme Studio and the recovery controls belong to
the host so every theme has an exit path. `renderScreen('details')` uses the currently selected
game; call `openGame` to select one first.

Render the backend's recommendation reasons faithfully. Wrap displayed recommendations in
`components.Impression`; do not report unseen reserve items. The wrapper reports only when
the item is sufficiently visible in the active window. Treat absent artwork or metadata as
absent, rather than inventing values. Launch reports a handoff; it does not establish that a
game is running or that an installation has completed.

For more specialized screens, the existing narrow `window.winnow.request` bridge exposes
allowlisted API operations. Its route names and public request/result types live in
`src/Winnow.Electron/src/shared/bridge.ts` and `src/Winnow.Electron/src/main/routes.ts`.
Prefer host feature screens and named commands where possible. Backend mutations still
require the revisions, uncertainty handling, and event reconciliation described in
[`frontend-api.md`](frontend-api.md). A theme does not gain permission to call arbitrary URLs.

### Reuse artwork materials and previews

`PortalSurface` is independent of Rift's layout and navigation. Give it a fixed-size parent,
`children`, optional `artwork` (normally `Artwork` with `hero: true`), and `options` containing
roundness, waviness and activity from 0 to 100. Optional `origin: {x, y}` starts the aperture
at a local cursor position. `expansion: {x, y, width, height}` starts with a source rectangle
and reveals the whole destination without scaling or reflowing its content. All coordinates
are local CSS pixels; callers account for interface zoom when converting viewport bounds.
`onExpanded` fires when the fixed content is fully exposed and the entrance renderer has
been released. `active: false` hides and disposes the effect; `reducedMotion` and the system
preference reveal the content immediately. Normal portals pause offscreen, on window blur,
and in hidden documents; ambient drawing is capped at 30 Hz. The DOM artwork remains the
cached source, and graphics failure leaves the reading surface usable.

Full-view expansion reveals artwork and content through one shared mask. Temporary
`translate3d`, backface visibility and `will-change: clip-path` hints isolate the reading
plane while the contour changes; they are removed when the entrance ends or is interrupted.
The content retains its final dimensions and is never scaled. The rim renderer retires
as soon as the entire pane is safely inside the contour and its fading halo, or at the
entrance duration limit. Small preview portals retain their separate image mask so the
text does not move or get clipped by the ambient edge after opening.

Rift's adapter owns cover-relative placement, preview intent, focus and the route transition.
Its Library preview is a passive tooltip with a fixed reading plane, up to 420 pixels high
on desktop and 440 in fullscreen. It ignores pointer events and contains no controls.
The cover owns hover, focus and activation; clicking it or pressing Enter opens details.
Escape dismisses the preview, and Tab follows the ordinary cover order.
Other themes can reuse the surface without adopting those interactions. Check that
`context.components.PortalSurface` exists before using it on older hosts. The inherited
`--portal-rim-a` and `--portal-rim-b` variables hold three RGB components from 0 to 1 and
follow the profile's accent and secondary accent.

The shared effects do not depend on Afterglow's screen layout. For a complete host card,
set `presentation` to `poster`, `landscape`, or `record`, and `preview` to `flyout`, `inline`,
`overlay`, or `none`. `overlay` reveals a caption inside the artwork on hover or keyboard
focus; Afterglow pairs it with `effects: false`. Omitting these props keeps the existing inline host-card presentation for
older theme packages. `effects` takes partial material overrides, or `false` to disable
all decoration for that card.

```js
h(context.components.GameCard, {
  game,
  reason,
  onOpen: () => context.openGame(game.workId),
  presentation: 'poster',
  preview: 'flyout',
  effects: { foilMetal: 'gold', tilt: 4 }
});
```

For a custom layout, compose the primitives. `ArtworkEffects` wraps one decoded image;
use `Artwork` for authenticated, cached cover loading. Give the untransformed button a ref
and pass it as `interactionRef` so pointer coordinates remain stable as the visual tilts.
Without this ref, the effect wrapper itself supplies the fixed interaction area. Keep
interactive controls outside the tilted surface; that surface ignores pointer events.

```js
function Cover({ context, game, reason }) {
  const trigger = React.useRef(null);
  const { Artwork, ArtworkEffects, GamePreview } = context.components;
  return h(GamePreview, { game, reason },
    h('button', {
      ref: trigger,
      className: 'my-cover',
      'aria-label': `View ${game.title}`,
      onClick: () => context.openGame(game.workId)
    },
      h(ArtworkEffects, { interactionRef: trigger, effects: { foilMetal: 'gold' } },
        h(Artwork, { workId: game.workId }))));
}
```

Size the button and effect frame in the theme's CSS. Both wrapper spans inherit the corner
radius. The child image must use centered `object-fit: cover` so the material aligns with
the visible crop. Leave space around covers for their lift and shadow.
Resting artwork surfaces use no transform; the active floating card supplies its own
perspective. Avoid forcing every cover into a permanent 3D layer. Shared `Artwork` covers
use blank, static skeletons while loading, including in desktop and fullscreen. Hero
artwork retains its loading indicator. Missing and failed artwork use their existing
fallback once loading finishes.

```css
.my-theme .my-cover {
  width: 180px;
  aspect-ratio: 2 / 3;
  border: 0;
  padding: 0;
  border-radius: 10px;
}
.my-theme .my-cover > .winnow-artwork-effects,
.my-theme .my-cover .artwork { width: 100%; height: 100%; }
```

The stable CSS hooks are `.winnow-artwork-effects` (the frame), its direct child
`.winnow-artwork-surface` (the moving plane), and presence attributes `data-artwork-active`
and `data-artwork-floating`. `data-artwork-input` is `pointer` or `keyboard`, and
`data-artwork-motion` is `follow` or `still`. Effect rules are in the `components` cascade
layer. Scope overrides under the theme's own shell; do not transform the interaction frame.

`GamePreview` keeps game information separate from the image and uses the supplied backend
record, including `summary` when available. It does not launch the game. The theme supplies
the trigger's accessible name, focus styling, and activation behavior. `disabled` suppresses
the preview without removing its children. Wrapping a host card that already has a flyout
would create two previews; use one or the other.

The flyout is portaled to the document body to escape scroll and card clipping. Style it
with `[data-theme='your-theme'] .winnow-game-preview`, not a shell descendant selector.
Its `--preview-background`, `--preview-border` and `--preview-radius` variables fall back to
the usual surface, line and radius tokens. Keep the wrapper around one named focusable
trigger. Hover allows time to cross to the panel; keyboard focus opens immediately, Escape
dismisses it before shell navigation, and Page Up/Down scroll long descriptions. Side
placement flips at the viewport edge and docks above the footer when neither side fits.

The host supplies saved material defaults to every wrapper. Partial `effects` override only
the named values; omitted values follow the user's profile. The effect service shares one
active canvas and reuses the decoded child image, so themes do not fetch secondary artwork
or bundle Pixi. If graphics initialization fails, the original image and preview remain
available. The service releases active work when its registered components unmount.

These components are additive to theme API 1. Themes targeting earlier API 1 hosts should
check for `components.ArtworkEffects` and `components.GamePreview` before using them, as the
Reading room example does. Existing packages need no changes.

### Appearance and theme-specific settings

The host writes these CSS variables to the document root:

```css
--bg; --surface; --raised; --text; --muted; --line;
--accent; --accent-text; --cool;
--font-display; --font-body; --font-mono;
--radius; --density; --scrim; --interface-scale;
```

`appearance.palette` accepts `winnow`, `afterglow`, `paper`, `bluehour` and `rift`.
New profiles use `winnow` with the Avalon composition. Existing version-one palette values
and saved profiles remain valid.

It also sets `data-theme`, `data-palette`, `data-font`, `data-density`, `data-navigation`,
`data-card-style`, and `data-detail-arrangement`, plus `.reduced-motion` when selected.
Use these preferences when they fit the composition. Scope theme CSS under a unique shell
class. The runtime removes the package stylesheet on theme switches; avoid injecting
persistent global styles or retaining global listeners without effect cleanup.

`appearance.colors` optionally overrides the semantic color keys `background`, `surface`,
`raised`, `text`, `muted`, `line`, and `cool` with six-digit hex values. `appearance.scale` is
an optional percentage from 85 to 130; the host applies it to the whole interface and exposes
`--interface-scale` as a ratio. Older version-one profiles without these fields keep their
preset colors and 100% scale.

`appearance.artwork` optionally stores a complete `ArtworkEffectOptions` object. Older
version-one profiles without it use the defaults below; their saved card style is preserved.
New profiles default to portrait cards. Imports reject missing, extra, or invalid artwork
fields when the object is supplied. Theme component overrides are partial and are bounded
by `normalizeArtworkEffects`; they do not change the saved profile.

| Field | Choices or limits | Default |
| --- | --- | --- |
| `finish` | `off`, `matte`, `satin`, `foil` | `satin` |
| `intensity` | 0–100 | 55 |
| `highlightFoil` | Boolean | `true` |
| `foilMetal` | `silver`, `gold`, `holographic` | `silver` |
| `foilStrength` | 0–100 | 65 |
| `foilThreshold` | 40–95; higher values select brighter areas | 72 |
| `followPointer` | Boolean | `true` |
| `floating` | Boolean | `true` |
| `tilt` | 0–12 degrees | 7 |

Highlight selection follows pixel brightness. It can affect bright illustrations as well as
lettering; it does not identify text. The reusable types, defaults, and resolver live in
[`artworkEffects.ts`](../src/Winnow.Electron/src/shared/artworkEffects.ts).

Declare a `settings` array to add controls to Theme Studio. Supported fields are `toggle`,
`select`, and `range`, each with an `id`, label, and default. Range fields require `min` and
`max`; `step` is optional. Select fields supply `{value,label}` options and a matching default.
An optional description explains the setting to the user.

```js
const definition = defineTheme({
  apiVersion: 1, id: 'my-theme', name: 'My theme', Shell,
  settings: [
    { id: 'showYear', label: 'Show release years', type: 'toggle', default: true },
    { id: 'columns', label: 'Columns', type: 'range', min: 1, max: 4, step: 1, default: 2 }
  ]
});

// Inside a component, only declared fields with valid values are returned.
const preferences = window.WinnowThemeSDK.settings(definition, context.profile);
```

Settings remain data: strings are short plain values, numbers are bounded, and nested objects
or executable URLs are rejected. Unknown fields are not returned by the settings helper;
invalid saved choices fall back to the schema default.

## Development and compatibility checks

Start the frontend using the commands in
[`src/Winnow.Electron/README.md`](../src/Winnow.Electron/README.md), always with a throwaway
backend data directory while developing. Install a local theme through Theme Studio, edit its
source folder, increment its version, and reinstall to preview changes. There is no live
editor or theme marketplace in this first version.

Check desktop and fullscreen, a constrained window, keyboard focus, high text zoom, reduced
motion, a disconnected backend, and an empty library. Preserve `main-content` for the host's
skip link and navigation focus. Include a visible route back to Theme Studio. Use effect
cleanup for observers and listeners, and do not start intervals or mutate data during render.

The frontend tests cover profile validation, package bounds and asset paths, API compatibility,
the independent example, stale module loads, stylesheet cleanup, and rendering recovery.
Run them from `src/Winnow.Electron` with `npm test`. Theme API changes that break these public
props, components, or commands require a new API version; incompatible packages are rejected
before their entry module is evaluated.
