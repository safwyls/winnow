# Avalon backdrop migration

Measured on Windows on 2026-09-29 against the Electron production renderer and an isolated
backend. The tests use temporary data directories and generated local PNGs; no provider
network access is required.

The new `works/{workId}/backdrop` endpoint delegates candidate ordering to the existing
`BackdropSelection` policy. Four real HTTP/backend tests passed, covering grouped saved
choices, source order, metadata failure, cancellation, invalid aspect ratios and the
3840-pixel image bucket. They ran with `BaseOutputPath=C:\Temp\winnow-backdrop-verify\` so
the development app's loaded backend assemblies were not replaced.

The focused Electron controller, image lease, component and transport tests passed all
36 cases. Controlled frames verify the original four reduced-motion/fallback combinations,
superseded metadata and decoded images, per-source decode sizes, failed resolution upgrades,
artwork invalidation and disposal. Image transport cancellation is scoped to the requesting
renderer, and late bytes do not reach the renderer after cancellation.

The four cases in `tests/electron/backdrop.spec.ts` passed in 13.0 seconds after a production
build. Real Chromium measurements covered a 2520×1080 Home surface with a 2520×813.75 fitted
hero, a partial crossfade into a 2520×1080 landscape, and resizing to 3840×1080 and 1920×1080.
The first native run caught an unbound browser animation callback that the original test
mock accepted. A receiver-validating component test reproduced the failure before the fix.
The final run also verified request abortion on navigation, object-URL counts returning to
zero on detach, and desktop/fullscreen Details reading veils.

Captures in `.tmp/electron-backdrop-results` were inspected:

- `backdrop-fullscreen-Home-c-9da6d--and-releases-replaced-URLs/home-wide-hero.png`
- `backdrop-desktop-Details-u-eb4be-eases-its-backdrop-on-close/details-desktop-backdrop.png`
- `backdrop-fullscreen-Detail-7b1f5-eases-its-backdrop-on-close/details-fullscreen-backdrop.png`

These images establish geometry and the veil treatment over deterministic gradients.
They do not establish representative game-art quality or the OS compositor's native material.
The new backdrop API is independently tested through real HTTP; the native image fixture
intercepts only the candidate/image responses and retains production main/preload/rendering.

The ten original `FullscreenBackdropTests` methods map to eight ported and two partial
contracts in `tests/migration-backdrop.json`. Remaining assertions concern the shared portrait
component's pending-image cancellation and the full original raw-pixel mask/color matrix.
The original tests remain intact. The separate Details Overview fixture now supplies the
new candidate endpoint and awaits its ready state, retaining the existing screenshot
aspect-ratio and native-size assertions.
