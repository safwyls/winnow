# Electron details facts and gallery — 2026-09-29

The shared details leaves now preserve recognized licence vocabulary, acquisition dates,
attributed reception figures and the longitudinal playtime record. Acquisition output
does not carry paid prices; account statistics remains the place for spending. Invalid
patch-note URLs retain their update rows without an open-page button.

`parity-details-facts.test.tsx` passes 61 cases. The cases derive from
`DetailsModalAdditionsTests.cs`, `GameDetailsViewModelTests.cs`, `ScreenshotLightboxTests.cs`
and `ScreenshotScrollInteractionTests.cs`. Reception and refetch helpers are tested, but
their source mappings remain partial until the Avalon composition uses them and its tests
pass. The existing details component suite also passed alongside the fact tests before
the new visual composition began.

The gallery retains the selected mark after closing, wraps in both directions, omits
navigation for a single shot, and returns focus to the opening thumbnail rather than the
last displayed shot. Close is initially focused. The changing position is a polite live
caption. Wheel, Shift-wheel and horizontal input stay inside the strip at both edges.
Fullscreen composition can request two previews while keeping the complete gallery.

Three native Electron tests passed together in 11.7 seconds. The test-owned entry point
delegates to production main, preload and renderer, replacing only screenshot facts and
their image response. A deterministic 1280×720 PNG with colored edge marks makes cropping
and upscaling measurable. The tests verify both modes at 1280×720 and 1920×1080 client sizes,
the image's native dimensions and contained geometry, focus cycling, navigation, unchanged
outer scrolling, Escape and restored thumbnail focus. The desktop screenshot was also
visually inspected: the complete image and four edge marks remain visible, with overlaid
controls and a centered caption.

Commands, from `src/Winnow.Electron`, with `WINNOW_BACKEND_PATH` set to the isolated Debug
backend:

```powershell
npm test -- tests/parity-details-facts.test.tsx tests/parity-details.test.tsx
npm run test:rendered -- tests/electron/gallery.spec.ts
```

The main backend and Chromium state use a fresh `--data-dir` beneath ignored `.tmp`, with
sample seeding and `--no-sync`. These checks do not establish the pending complete Avalon
details modal/fullscreen composition or provider image quality.
