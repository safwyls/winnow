# Electron parity checkpoint 36 — fullscreen Appearance and Controller

Fullscreen Settings restores the seven original sections and starts on Appearance.
Text size uses ten-point steps, interface scale uses five-point steps, and screen margins
use one-point steps. Large rows retain their values and directional cues. Switch tracks
show On/Off; Left and Right set a value without repeatedly toggling it. Saved values update
the same rows, with controller focus restored after persistence. Shared cover crop and
dormancy changes reach desktop and fullscreen. Confirmed reset touches only the five
fullscreen appearance preferences.

Appearance includes shared palette and font pickers, per-palette typography reset, and a
live cover preview. The heading stays visible while settings scroll. Controller help uses
the original proportional artwork, ten mappings and keyboard fallback. At smaller displays,
spacing and diagram width adapt without reducing the source-scaled text. Selected tabs
and focused rows use the original underline treatment. The reused CC0 artwork notice is
included in packaging.

Spending and recommendation history open from Library; operations open from Application.
Back returns to their originating action. Portal dialogs consume their own Escape without
also navigating the underlying Settings page. The Controller section retains navigation
focus when a trigger changes sections.

## Verification

- All 3,071 component/live API cases pass across 151 files without skips, in 53.14s:
  `.tmp/fullscreen-settings-layer-integration.log`. Twenty new component cases cover row
  behavior, shared state, failure/retry, reset, section selection, typography and return focus.
- Build/typecheck, the migration inventory audit and packaged notice generation pass:
  `.tmp/fullscreen-settings-focus-build.log`, `.tmp/fullscreen-settings-migration.log` and
  `.tmp/fullscreen-settings-notices.log`.
- The initial guide checks exposed an unbounded screen wrapper and artwork overflowing its
  available height. Follow-up checks cover the original four resolution/text/margin cases,
  source-sized labels, all text bounds, proportional geometry, no vertical scrolling and
  no overlap with keyboard help. Enlarged 720p and 1080p captures were inspected.
- The 45-case native regression passes 42 cases. It includes all eight new Settings checks,
  both desktop/fullscreen IGDB workflows and the existing Spending, plugin and core UI cases.
  Two Spending failures exposed portal Escape reaching the parent page; the fix preserves
  the open Spending page and its details opener. The remaining plugin restart check missed
  its transient pending state. A controlled IPC hold now verifies pending state before
  releasing the actual backend restart. Original report:
  `.tmp/fullscreen-settings-regression-native/results.json`.
- All 12 native follow-up cases pass in 2.2 minutes:
  `.tmp/fullscreen-settings-layer-native/results.json`. Both Spending layouts keep the
  page and restore the details opener. Desktop and fullscreen verify disabled restart
  controls during the controlled hold, then release and complete the real backend restart.
  All eight Appearance, typography and Controller cases pass, including section focus.
- Four final Appearance/typography geometry checks pass in 51.5s:
  `.tmp/fullscreen-settings-preview-native/results.json`. Preview artwork and switch tracks
  retain the source's proportional size at 720p and 1080p. The preview heading retains the
  original exclusion from fullscreen body-text scaling. Initial and maximum-text captures
  were inspected; the Appearance heading stays visible while rows scroll.
- Both final initial-focus underline checks pass in 29.1s:
  `.tmp/fullscreen-settings-focus-final/results.json`. Programmatic entry now has the
  same visible underline as controller input. The assertion waits for the ordinary color
  transition to finish rather than comparing two intermediate colors.

Six source methods now have complete replacement evidence: four formerly pending fullscreen
row/guide methods and the previously partial cover-crop and typography interaction methods.
Inventory: 1,113 ported, 559 retained backend, 17 framework-specific, 642 pending and 104 partial
out of 2,435 frozen methods. Library and Application settings still require their original
fullscreen row presentation; metadata/artwork hierarchy also remains incomplete. Overall
migration criteria remain unchecked.
