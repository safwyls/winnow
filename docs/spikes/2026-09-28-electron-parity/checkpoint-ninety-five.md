# Checkpoint 95 — attached displays and native materials

TASK-381.40 exercised the primary Windows package on the three attached monitors on
2026-10-02. The package was built during TASK-381.39 with its working changes; its
manifest records `b10d4d66`. Reports retain the actual executable and ASAR hashes rather
than relabeling it as a clean later commit.

## Native displays and input

Windows 11 build 26200 uses an AMD Radeon RX 9070 XT. The AW3423DWF primary display is
3440 × 1440 at 165 Hz. The Odyssey G5 and LG ULTRAGEAR are 1440 × 2560 portrait displays,
at 165 Hz and 144 Hz respectively. All three report scale factor and device pixel ratio 1.

The packaged application used a disposable library containing three manual fake games.
On each display, F11 entered the exact native display bounds and restored the original
window bounds. The primary display also preserved maximized state and normal bounds.
Desktop and fullscreen Library, Details, Escape and Settings paths passed. Each surface
retained its selected game, and all six Details returns restored the exact originating
tile with visible keyboard focus without additional input. All three minimize/restore
trials retained the already focused tile. The actual renderer reported no gamepad; the
disconnected status, bumper hints and keyboard hints remained visible.

Ultrawide Library and portrait Details/Settings captures were inspected for readable
content, reachable controls and clipping. These are attached monitor observations, not
TV-distance, overscan or handheld-device proof. Keyboard and pointer input was automated
through the actual packaged window; physical controller input was not exercised.

## Composed materials

A separate probe drove Theme Studio through solid, Acrylic 30%, Acrylic 100%, Mica 100%,
fullscreen solid and restored desktop Mica states. A probe-owned striped window behind
Winnow supplied two distinct patterns per state. Guarded screen copies captured native
DWM composition, with target identity, foreground, bounds, z-order and a physical
ownership grid checked before and after each capture. No wallpaper or OS preference was
changed. These guards require a quiet desktop; they are not an atomic capture transaction.

The final twelve captures were inspected. Acrylic visibly responded to the underlay;
the sampled opaque cover region remained unchanged. Mean absolute RGB differences in
the content field were 0 for solid, 12.15 for Acrylic 30%, and 40.01 for Acrylic 100%.
Mica and restored Mica did not respond to the underlay, as expected for wallpaper-based
composition. Fullscreen's sampled central region remained solid. These are captured
8-bit pixel measurements, not calibrated display luminance. DWM attribute 38 reported
solid 1, Acrylic 3, Mica 2, fullscreen 1, and restored Mica 2; that attribute alone does
not prove visible materials. Native window opacity stayed 1 throughout.

The current Windows environment reported high contrast, forced colors and reduced
transparency disabled. Their physical fallback behavior, remote sessions and other OS
versions were not exercised. An earlier fullscreen capture containing an unrelated
overlay was discarded; final fullscreen captures use a guarded central client region.

## Evidence and limits

Both probes returned backend shutdown 202, closed their owned processes without forced
cleanup, released the portable lease and left the package hashes unchanged. Source is
under `src/Winnow.Electron/tests/packaged/`: `device-probe.mjs`, `material-probe.mjs`,
`windows-material-probe.ps1` and `windows-material-probe.cs`.

Local evidence is `.tmp/task38140-device-focus/device-report.json`,
`.tmp/task38140-device-evidence.json`, `.tmp/task38140-material-final/material-report.json`
and `.tmp/task38140-material-final/pixel-observations.json`, alongside their captures.
Earlier probe errors remain identified as harness diagnostics in the device ledger.

The user accepted physical controller button mapping, held-button suppression, reconnect
and battery reporting as unverified exceptions. Controller-owned cursor recovery is also
unmeasured; the passing focus/cursor checks use keyboard and pointer input. TV viewing and
overscan, a physical small-screen device and a native Linux desktop/compositor remain
pending explicit exception agreement. No synthetic test or Ubuntu Xvfb package smoke is
presented as physical-device proof. This evidence does not establish the final combined
regression gate, which belongs to TASK-381.41.
