# Theme colors, contrast and persistence checkpoint — 2026-10-01

TASK-381.32 covers fourteen frozen source methods in the second task of the
authorized TASK-381.31–381.35 batch. Verification compares original bundled and
authored theme behavior with Electron's saved appearance and rendered controls.

## Source boundaries

The source covers catalogue availability without a local theme directory, Dawn
defaults and retained audit findings, inferred and explicit theme variants,
complete token round trips, and preparation without a configured theme store.
Appearance focus restoration preserves the scrolled position; keyboard focus
still reveals its destination. Warnings start collapsed.

Control checks use the original Dawn themes, action states, selected segments,
hovered chips, dark/light switching and fullscreen actions. Native checks must
measure the rendered colors and composite translucent fills, rather than infer
contrast from token names.

Electron reports whether a background-material request is supported and accepted.
It does not expose Avalonia's actual compositor-material property. Request
acceptance must not be reported as a measurement of the visible OS effect.

## Implementation and verification

All 31 original cases pass unchanged: 23 from the main test assembly and eight
from the UI assembly. The original source files and theme implementation match
the frozen revision. Source results are in `.tmp/task38132-source-results`;
eleven original rendered captures are in `.tmp/task38132-source-captures`.

The reusable exporter in `src/Winnow.Electron/scripts/theme-contracts` records
all 61 original color tokens across nine themes, both layouts, both content
transparency settings and every five-percent slider position. It first verifies
all 46,116 original JSON round-trip comparisons. Normal Electron tests read the
generated reference fixture and do not load .NET or Avalonia.

The golden comparison found premature HSV rounding in Bottle green's chained
derived colors. Electron now retains intermediate precision until a color is
materialized, or an authored byte-color override replaces it. All nine palettes
match all 61 original roles across 756 states in both JSON directions. The test
adapter names framework scrollbar roles explicitly; its arithmetic comparisons
do not claim that Chromium uses Fluent's scrollbar templates.

Bundled authored palettes now retain their audit findings without local files.
A local replacement overrides both the palette and its findings. File errors
remain visible and advisory warnings use the original collapsed disclosure.
Fullscreen actions now retain their underline in pressed and current states.
Current actions use bold text and a neutral underline; focus and pressed states
use the accent treatment. Review identified and corrected the initial conflation
of current and focused styling before accepting the native evidence.

All 270 focused cases across thirteen files pass, as do TypeScript and the
production build. Nine JSON runtime cases pass after the final accessible
markup adjustment, and 22 action/palette cases pass after the current-state fix.
All eleven distinct native cases pass: eight new contracts and three existing
regressions. The eight new cases use final bundle `index-PcypxzjY.js`.

Production control probes measure a minimum enabled action text contrast of
4.807:1, quiet boundary contrast of 4.321:1 and focus contrast of 6.072:1.
Electron's external outlines are measured against their adjacent parent surface;
the source measures inner borders against button fills. Both use the original
4.5:1 text and 3:1 focus thresholds. Selected text reaches 5.260:1 and hovered
chip glyphs reach 7.919:1. Disabled states are recorded without inventing a floor.
The same mounted controls follow dark/light/dark changes without stale colors.

Actual application journeys use an isolated data directory and the prebuilt
backend. Desktop and fullscreen preserve a 400-pixel scroll offset on real
native focus return, then Tab reveals the control (364 and 314.118 pixels,
respectively). Playwright focus emulation is disabled for this proof. Both
surfaces preserve authored choices across reload and apply Dawn's solid default.
Root and native review inspected the rendered action and appearance captures.

Initial failures exposed probe startup, focus emulation and outline-adjacency
measurement errors in the harness. Corrected checks pass; the native ledger
retains those attempts and their results. No task-owned processes remain.
Raw measurements, captures and per-case provenance are in
`.tmp/task38132-native-evidence.json`; source and renderer ledgers use the same
task prefix. Physical controller hardware was not tested.

Two methods concern an unavailable framework facility: Avalonia reports the
actual backdrop material, including substitution and loss. Electron exposes
only a material request. Its acceptance, refusal, accessibility fallback and
stale-response tests remain useful, but cannot replace those actual compositor
notifications. These methods receive narrow framework-specific dispositions;
successful native requests do not establish their original assertions.

The sparse Avalonia brush-dictionary method is also framework-specific. It
requires mutable brush identity and avoids creating absent resource keys;
Electron deliberately publishes a complete CSS palette. Replacement checks
retain the root style declaration and mounted controls, verify exact BoxArt
colors and exercise live theme switching. They do not claim sparse-dictionary
equivalence.

Eleven methods are ported and three have these narrow framework dispositions.
The inventory now contains 1,646 ported, 705 retained-backend and 40
framework-specific methods, with 37 pending and seven partial. The report
validates; the complete migration gate still fails for the 44 unresolved methods.
Continue with TASK-381.33 within the authorized batch ending at TASK-381.35.
