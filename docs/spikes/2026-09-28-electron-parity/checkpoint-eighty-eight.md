# Typography and floating layout checkpoint — 2026-10-01

TASK-381.33 covers thirteen frozen source methods in the third task of the
authorized TASK-381.31–381.35 batch. It checks live font resources, authored
typography controls, floating layout preferences and rounded selector containment.

## Source boundaries

The floating palette contract compares every color at all 101 integer slider
positions, both wall settings and all original themes. Only the ground and
caption may vary by layout. Solid fields remain one elevation step above panes.
Stored and session-only layout choices retain their separate persistence rules.

Typography coverage includes selected and missing fonts, installed choices,
80–120% theme sizing, stable control geometry and reading line height. Fullscreen
combines page and theme text scales from stable baselines, including detached
consent, controller hints and the on-screen keyboard. Both fullscreen Theme Studio
and Settings Appearance need their own rendered assessment.

## Verification

All thirty original cases pass unchanged: twenty-one main cases and nine UI
cases. The tests and their theme, typography, appearance and fullscreen
implementations match the frozen revision. Twenty source captures and commands,
results and assembly hashes are recorded in `.tmp/task38133-source-evidence.json`.

All 212 focused renderer, runtime, main and preload cases across sixteen files
pass. TypeScript also passes. The initial preload test imported esbuild inside a
jsdom environment; separating that check into its Node environment corrected the
harness without changing production behavior.

Fullscreen Theme Studio now uses TV control and information sizes; its Settings
Appearance counterpart remains separately tested. Keyboard hint icons no longer
multiply theme text scale. Segmented groups clip their end fills to the rounded
boundary and retain an inset focus outline.

A bounded, validated typography message updates only Winnow's local browser
toolbar and account composer. It preserves the document, draft, selection and
focus. Known bundled font data supplies each role's fallback. Existing primary
window sender validation remains unchanged; child frames and provider pages
cannot publish these updates. Listeners are removed on close and owner teardown.
Review found that fixed provider insets could cover larger hints. Insets now
follow the measured, font-ready local toolbar height and respond to font, status
and window-size changes.

The real fullscreen consent dialog uses the original theme-only 28px text, 42px
line height and 24px hints. Fullscreen page text remains independently scalable.
The native matrix distinguishes current Home's 24px body/action roles from the
original 28px fixture, and measures both alongside the real clock and stable
icons.

All sixteen distinct native cases pass: eleven unaffected passes from the first
build and five affected cases against final bundle `index-BESndGNg.js`. The final
five cover both segmented palettes, trusted popouts, fullscreen font/cover
geometry and separate Studio/Settings reachability. Source pixels, rendered
font glyphs, actual controls and saved settings provide the evidence; CSS names
alone do not establish those contracts.

The first native run found that Avalon's focus rule overrode the generic segment
inset, and that larger fonts grew the header enough to cross a cover-sizing
threshold. A diagnostic run after fonts and layout settled reproduced the
geometry failure. The corrected header reserves the maximum status/utility
stack as well as navigation; it remains 87 logical pixels at both sizes. Covers
remain exactly 180.203125 by 270.296875 pixels and the shelf row stays 338 logical
pixels. The cover-sizing model is unchanged.

Screenshot review also found a horizontally clipped Close action in the narrow
reference toolbar. The final toolbar wraps actions and its address; all four
actions remain within the 760-pixel viewport with no horizontal overflow. Native
pixel checks now verify both segment ends and their inset focus rings. Root
review inspected desktop typography, fullscreen Studio and consent, the account
keyboard, corrected toolbar and Dawn focus captures.

The final native ledger is `.tmp/task38133-native-evidence.json`; focused and
corrective renderer logs use the same task prefix. All owned processes close
after verification. Controller checks use simulated input; physical hardware
and live provider sign-in were not tested in this task.

All thirteen methods are ported. The inventory validates with 1,659 ported,
705 retained-backend and 40 framework-specific methods, leaving 31 pending and
none partial. The complete migration gate still fails for the remaining work.
Continue to TASK-381.34 within the authorized batch ending at TASK-381.35.
