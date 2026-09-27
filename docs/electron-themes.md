# Electron themes

The Electron frontend has three levels of customization. Theme Studio changes appearance
and layout without editing code. Appearance profiles share those choices as JSON. Developer
themes replace React screens and the application shell using theme API 1.

Afterglow and Catalogue use the same `ThemeDefinition` interface as installed themes.
Afterglow is the spacious, artwork-led default. Catalogue uses an index and a reading desk.
Both retain the same library, recommendations, journals, and backend commands. These themes
belong to the independent Electron frontend; they do not alter Avalonia themes or backend
provider plugins.

## Change the appearance

Open **Theme Studio** from the palette button. Choose a composition, then adjust:

- Afterglow, Paper trail, or Blue hour colors, plus a custom accent. Expand **Customize every
  color** to set background, panels, raised surfaces, text, secondary text, borders, and the
  secondary accent individually, using a picker or a hex value.
- Editorial, sans serif, or monospace typography using bundled fonts.
- Interface size from 85% to 130%, spacing, corner radius, artwork shading, and reduced motion.
- Navigation position, library card style, and game-detail arrangement.
- Discover section order and visibility. At least one section remains visible.
- Additional controls declared by the selected developer theme.

Changes apply immediately and save automatically. **Reset** restores the default appearance.
The studio checks text and accent contrast against the current custom backgrounds and warns
when a pair has less than 4.5:1 contrast. Buttons filled with the accent use whichever of black
or white has greater contrast. Individual color resets return to the selected preset;
choosing a preset clears custom palette overrides.
System reduced-motion preferences still apply when the explicit preference is off.

**Export profile** writes a JSON file containing appearance, layout, selected theme ID, and
theme-specific settings. **Import profile** validates its version, known fields, choices,
and numeric limits before applying anything. Profiles contain no JavaScript, CSS, assets,
backend credentials, or library records. Importing a profile does not install its theme.
If the theme is unavailable, Winnow uses Afterglow and keeps the appearance choices.

Profiles are stored in the Electron user-data directory under
`libraries/<data-directory-hash>/preferences.json`. Different backend data directories have
separate profiles and installed themes. The frontend does not overwrite Avalonia appearance
preferences in the backend.
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
recovery control and **Ctrl+Shift+T** restore Afterglow. For a theme that prevents normal
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
host Details, Journal, and Settings, and declares two editable settings. It uses no build
tools: install that folder directly to try it.

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
Grid titles reserve two lines and keep their full title available to assistive technology
and in the hover tooltip.
Short windows use tighter navigation and library spacing. At less than 480 logical pixels
of available height, the grid uses compact thumbnails beside titles and keeps filters on
one row. These breakpoints follow the scaled content viewport, including at 130% size.

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
| `setPage(page)` | Navigate to Discover, Library, Journal, Settings, Studio, or Details. |
| `openGame(workId)` | Select a game and navigate to its details. |
| `toggleFullscreen()` | Change the window mode. |
| `children` | The active screen, supplied to the shell. |
| `renderScreen(page?)` | Render a host screen, bypassing theme overrides to avoid recursion. |
| `actions.launch(ownershipId)` | Invoke the host's supported play/install command for a copy. |
| `components.GameCard` | Host card with authenticated artwork; takes `game`, optional `reason`, and `onOpen`. |
| `components.Impression` | Visibility-aware recommendation exposure wrapper; takes `releaseId`, `shelfId`, and children. |
| `components.Artwork` | Authenticated artwork without a card layout; takes `workId`, optional `hero`, `className`, and `eager`. |

A definition can replace `Shell`, `Discover`, `Library`, `Details`, `Journal`, and `Settings`.
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

### Appearance and theme-specific settings

The host writes these CSS variables to the document root:

```css
--bg; --surface; --raised; --text; --muted; --line;
--accent; --accent-text; --cool;
--font-display; --font-body; --font-mono;
--radius; --density; --scrim; --interface-scale;
```

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
