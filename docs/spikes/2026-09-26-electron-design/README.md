# Winnow Electron design study: Afterglow

September 26, 2026. Design and API review for TASK-349. This is a proposal, not the
current application's visual specification or a working Electron client. The user asked
to see the mockup before application implementation.

Open [the interactive mockup](mockup.html) directly in a browser. Its assets are local and
it needs no build, dependency installation, backend or network connection. It contains
eight illustrative games. Ownership, recommendations, play history and notes are fictional.
Reloading resets the in-memory preview. Theme export/import is the only file interaction.

## The design

Afterglow treats the library as a personal reading room for games. Large landscapes and
serif titles make discovery feel editorial; fine rules, restrained labels and compact
controls keep the collection useful. Horizontal navigation separates discovering something
to play, finding something specific, and looking back at time spent playing.

This is an original visual system for an independent frontend. It does not reuse the
Avalonia layout, components, palette, typography or cover dormancy treatment. The product
name remains Winnow. The flame in the study is a temporary mark, not a replacement for
Winnow's dragon identity; final identity artwork belongs in the implementation design pass.

| Surface | Composition and main action |
|---|---|
| Discover | One landscape recommendation, its reason and an immediate Play action. A narrow return column and a small unplayed selection offer other starting points. Actual shelf membership and reasons come from the backend. |
| Library | A searchable collection with horizontal artwork, bucket filters, an optional list index and a compact record view. Advanced facets, sorting, manual games, live lists, hidden games and identity review belong here. |
| Game details | A landscape opening followed by the game's story, personal facts and history. A separate ownership panel makes the selected store and launch/install action explicit. Artwork and metadata editing are nearby. |
| Journal | Dated session entries with optional notes, a quiet weekly summary, and approximate Steam-reported activity kept separate from recorded totals. |
| Theme Studio | Live changes to the whole interface: three palettes, custom accent, heading style, shape, density, artwork atmosphere and reduced motion. A sample screen sits beside the controls. |
| Connections/settings | Store connections, provider plugins, metadata/artwork, library preferences and application tools. Sensitive forms use dedicated backend commands and redacted status. |
| Fullscreen | A separate composition: panoramic background, large title/reason, two primary actions and a focused four-game strip. Arrow keys select a game, Enter opens its details, Escape exits. |

The mock includes desktop versions of the first six surfaces and a fullscreen discovery
composition. Fullscreen library, details, journal, settings, dialogs and gamepad navigation
remain design/implementation work. Opening those destinations from the fullscreen preview
currently returns to the desktop composition. It is not evidence of complete fullscreen
parity. The finished frontend must keep local position/filter/focus independently per
surface, while applying shared library changes consistently to both.

## Try the study

1. Open a game from Discover, or search with Ctrl/Cmd+K.
2. In Library, filter to Never played, search for TUNIC, clear the query, then switch to
   the record view. Lists and source filters narrow the sample collection.
3. Open Theme Studio with the palette icon. Try Paper trail or Blue hour, change the
   heading voice and corners, and browse again. Export a theme to retain those choices.
4. Open Journal and edit the sample Hades note.
5. Open fullscreen with the expand icon. Use the arrow keys to select another world.
6. Use the study's top-right Preview control to inspect an empty library, lost backend
   connection, an edit conflict or a constrained window.

Launch, installation, authentication, metadata/artwork changes and identity tools show
explanatory dialogs. They do not send commands. The conflict and disconnected variants
are visual states, not a transport simulation. The primary recommendation's Not now
and Add to Next up controls illustrate feedback with local receipts, rather than a complete
recommendation or list mutation model.

## Visual system and customization

| Role | Afterglow | Paper trail | Blue hour |
|---|---|---|---|
| Background | `#18191B` | `#F2ECE0` | `#111D33` |
| Surface | `#202124` | `#E9E1D3` | `#19283F` |
| Primary text | `#F1ECE4` | `#272927` | `#E7EDFA` |
| Secondary text | `#B2AFA9` | `#656058` | `#ADBCD0` |
| Accent | `#EFAD80` | `#8D462B` | `#AEC7FF` |
| Secondary signal | `#B5C9E4` | `#345975` | `#BDCBC0` |

The study uses Georgia for display, Segoe UI for controls and Consolas for small data.
These are system-font stacks with fallbacks, not redistributable font assets. The eventual
client should bundle appropriately licensed faces for repeatable Windows/Linux typography.
The production token schema should separate display, interface and data roles, type scale,
line height, interface scale, spacing, radius, foreground/background and semantic colors.

Customization is feasible without changing the domain API:

- Store versioned theme JSON and layout profiles under Electron's own user-data directory.
  Keep selection, drafts, scroll and screen composition local too.
- Use validated tokens to style every surface, including dialogs and fullscreen. Default to
  contrast-safe presets; custom palettes need foreground/background contrast validation and
  an accessible reset. The study only automatically chooses black/white text for accent buttons.
- Allow discovery section visibility/order and library view density. Give reordering
  explicit move-up/down controls in addition to drag and drop.
- Export/import appearance values without games, account data, tokens, secrets, executable
  code or remote URLs. Start with a versioned token format; arbitrary scripts are unnecessary.
- Keep backend library visibility, lists, artwork, metadata, credentials and journals shared.
  Do not put Electron theme names or theme JSON into Avalonia's shared appearance settings.

Effects should explain interaction: a short artwork-to-detail transition, gentle cover lift,
animated filter reflow, focus movement, restrained dialog blur and an adjustable artwork
scrim. Avoid continuous particles, autoplay video and a permanently running graphics loop.
The mock uses simple CSS transitions and respects system reduced motion; the production
client can use Motion for coordinated transitions and measured large-list behavior.

Keyboard focus stays visible, labels remain available without hover, contrast and font
scaling need automated and visual checks, and color must never be the only update signal.
The journal's chart has a textual accessible description. Native dialogs demonstrate focus
containment. A full screen-reader audit and physical controller/TV-distance tests remain open.

## API review

The API is sufficiently broad for the proposed frontend. Reviewed
[`frontend-api.md`](../../frontend-api.md), the backend endpoint modules, API contracts and
relevant tests. A reviewer also started a throwaway `--no-sync` host and inspected its
authenticated OpenAPI response. No real library was opened or changed.

All routes below are relative to `/api/v1`.

| Design capability | Existing contract/routes | Boundary to preserve |
|---|---|---|
| Recommendations | `GET /feed`, `/feed/supplement`, `/feed/history`; impression and feedback commands | Use backend reasons verbatim, respect `supportsFeedback`, and record only visible impressions. Feedback is Snoozed or NotInterested, not a positive like. |
| Collection, search and facets | `GET /library`, `GET/POST /library/workspace` | The workspace supplies authoritative groups, buckets and facets. Frontend filtering must preserve those identities and facts. No SQLite access or local recommendation engine. |
| Lists | `/lists`, `/lists/live`, list membership/order/filter routes | Membership is release-based. Keep revisions and distinguish fixed from live membership. |
| Personal details | `GET /games/{workId}/details`, metadata and IGDB routes | Details include sessions, events, acknowledgements, ownership, ratings, artwork, journal and achievement summaries. Missing evidence is not a zero or completion percentage. |
| Activity and statistics | `POST /activity/query`, `/activity/steam`, `/statistics/gameplay` | Sessions, updates and journal can be paged. Steam counter evidence is approximate and is not added to recorded session totals. |
| Session notes | `GET/PUT/DELETE /sessions/{sessionId}/journal` | Notes and optional ratings belong to sessions. Preserve revision and local draft on conflict. |
| Artwork | `/works/{workId}/artwork/{slot}` and browse/upload/reset routes; `/artwork/image` | Images require authentication. Use backend image bytes and expiring provider offers rather than exposing discovery credentials to the renderer. |
| Play/install | `POST /entries/{ownershipId}/actions` | Send a supported action and operation UUID. Handoff is not proof a game is running or installation has finished. |
| Maintenance | Manual games, hidden games, identity review, connections, plugins, setup and operations | These are dedicated commands; there is no generic credential/settings key-value API. |
| Preferences | Typed library, journal and presentation routes | Presentation keys are a closed, shared set. There is no theme catalogue/CRUD or independent frontend preference namespace. |

### Finding: incomplete OpenAPI schemas (P2)

Some routes are callable but cannot be faithfully generated as a TypeScript client from
the published schema. The inspected response described these routes as a bare success:

- `GET /games/{workId}` omits its game response schema and missing-resource response.
  See `src/Winnow.Backend/LibraryEndpoints.cs:26`.
- `POST /feed/feedback` and `/feed/feedback/revoke` omit response schemas.
  See `src/Winnow.Backend/FeedEndpoints.cs:23`.
- `POST /works/{workId}/artwork/{slot}/image` omits the upload body and result schemas;
  `/artwork/image` lacks the image response contract. See
  `src/Winnow.Backend/ArtworkEndpoints.cs:26` and `:40`.

These handlers return untyped results or read `HttpRequest` directly. The guide promises
route and JSON schemas, but `BackendHttpTests.cs:35` only checks that OpenAPI returns 200.
Before relying on code generation, add typed results or explicit response/body metadata and
assert the critical schemas. Alternatively, use a small hand-maintained TypeScript adapter
with runtime validation against the C# wire DTOs. This review does not change the backend.

### Other limits that shape the design

- There is no completion-time estimate, time-budget recommendation, match percentage or
  recommendation score in the public feed DTO. The mock makes none of these promises.
- There is no game-download progress endpoint. Launch/install status is dispatch/operation
  state, not invented game progress.
- Built-in OpenStore is not a universal action command; the action application rejects it
  for non-plugin ownerships. Manual-game records can contain executable paths, but the
  current action API does not dispatch those manual executables. A manual entry's Play
  control must remain unavailable until the capability is implemented.
- Achievement summaries are platform evidence, not a full per-achievement unlock browser
  or whole-game completion estimate.
- Cold IGDB matching can hold a write transaction while fetching uncached metadata, as the
  API guide already notes. The frontend needs a pending operation state, not an instant-save promise.
- Fullscreen layout, controller input, navigation, window controls, dialogs, theme files
  and sign-in browser presentation are frontend responsibilities.

### Electron boundary

The main process owns discovery, bearer credentials, HTTP, image retrieval and SSE. It
validates the exact loopback origin, refuses redirects and unsupported versions, and passes
only typed results through a narrow preload bridge. The renderer uses context isolation,
sandboxing and no Node integration. External sign-in content gets no application bridge.
See [Electron's security guidance](https://www.electronjs.org/docs/latest/tutorial/security).

Subscribe before loading snapshots; coalesce invalidation bursts without dropping changes
that arrive during reads. Reconnect with the cursor, reread discovery on restart and fully
resync on replay gaps. Preserve user drafts when snapshots refresh. Show 409 conflicts with
the saved version beside the draft. Do not automatically retry mutations after a dropped
response; reconcile state first. Reuse the same UUID for one uncertain launch attempt.

## Proposed implementation libraries

These are implementation choices for review, not dependencies installed by this study.
Pin compatible versions and their licenses when scaffolding the application.

| Library | Purpose |
|---|---|
| [Electron Forge with Vite/TypeScript](https://js.electronforge.io/modules/_electron_forge_template_vite_typescript.html), [React](https://react.dev/reference/react) | Desktop shell, build tooling and typed reusable view composition. |
| [Motion for React](https://motion.dev/docs/react-animation) | Coordinated artwork/detail transitions, filter reflow and focus movement. Set [reduced-motion policy](https://motion.dev/docs/react-use-reduced-motion) explicitly. |
| [Radix Primitives](https://www.radix-ui.com/primitives/docs/overview/introduction) | Unstyled dialog, menu, tooltip and selection behavior with accessible keyboard/focus foundations; no imposed visual theme. |
| [Lucide](https://lucide.dev/) | Consistent fine-line action icons. The mock bundles Lucide 0.468.0 and its license. |
| [TanStack Virtual](https://tanstack.com/virtual/latest/docs/introduction) | Virtualize large library lists/grids without giving up custom artwork composition. |
| [TanStack Query](https://tanstack.com/query/latest/docs/framework/react/overview) | Query cache and snapshot invalidation, with explicit mutation/retry policy. SSE remains in the main-process adapter. |
| [dnd kit](https://dndkit.com/legacy/guides/accessibility/) | Optional customization reordering with keyboard alternatives. Verify the current package/API choice before adopting it. |

No charting or WebGL framework is necessary for the initial design. The small weekly chart
can use accessible SVG. Adopt a larger visual library only for an interaction that earns it.

## Verification and delivery boundary

API reviewer verification used scratch output at `C:\Temp\winnow-api-review-20260926\`:
17 backend tests and 15 API-client tests passed. The throwaway backend used for OpenAPI
inspection was shut down. This is targeted evidence, not an exhaustive API or security audit.

Browser checks covered the 1440px desktop composition and a 390px constrained viewport.
Discover, Library, Journal, Theme Studio and Settings had no measured horizontal overflow
at 390px. Inspected all primary compositions and the separate fullscreen screen. Exercised
the five-result unplayed filter, search narrowing to TUNIC, clearing back to eight games,
opening details, switching palettes/corners/reduced motion, editing a note and selecting
empty/disconnected/conflict previews. Fullscreen ArrowRight changed selection to TUNIC and
Enter opened its details. No browser warnings or script errors were reported during these
checks. Export/import logic was source-reviewed; an actual file round trip remains untested.

Rendered review captures: [Discover](01-discover.png), [Theme Studio](02-theme-studio.png),
[Library](03-library.png), [Details](04-details.png), [Journal](05-journal.png), and
[Fullscreen](06-fullscreen.png). These are screenshot evidence for this study only.

The mock is a design artifact: plain HTML/CSS/JavaScript and local images. Application code,
solution projects, backend behavior and the existing Avalonia frontend are unchanged.
Production implementation begins after the user reviews this direction. That work needs
real API integration and error handling, desktop and fullscreen completion, accessibility
and performance verification, packaging and multi-client integration tests.

## Asset provenance

Game artwork is downloaded from Valve's public Steam asset CDN for this local review only.
Each image remains the property of its respective game owner; it is not an application
asset license. Production uses the user's authenticated backend artwork service.

Source pattern: `https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg`.
App IDs: Outer Wilds 753640, Hollow Knight 367520, Sable 757310, Hades 1145360,
Disco Elysium 632470, TUNIC 553420, Celeste 504230 and Citizen Sleeper 1578650.
The Outer Wilds panorama uses `753640/library_hero.jpg` at the same origin.
Lucide is from `https://unpkg.com/lucide@0.468.0/dist/umd/lucide.min.js`, with
[its license](assets/lucide-LICENSE) preserved locally.
