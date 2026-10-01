# Controller keyboard and accessible modal routes — 2026-09-30

TASK-381.5 restores the five-row controller keyboard, controller-driven file selection
and accessible routes through settings and dialogs. Desktop and fullscreen retain their
separate layouts. This checkpoint closes its ten assigned source methods and stops for
review before TASK-381.6.

The keyboard restores all 57 keys, shifted letters and symbols, a wide Space key and the
inverted-T caret cluster. Directional movement follows the original weighted rows. Back
closes once, stops edits and restores the original field, even if its owner retains the
component. Fullscreen sizing now overrides the generic dialog width cap.
Fullscreen also retains the original bundled Kenney D-pad, A, X, RT and B silhouettes
beside Move, Type, Backspace, Enter and Close. The icons use the active text color and
keep a complete accessible description; desktop retains its text hints.

Fullscreen file selection stays inside Winnow for artwork, executables, appearance
profiles, theme folders and acquisition CSV export. The main process owns directory
reads, opaque entry identities, extension checks and overwrite confirmation. Choosing
resolves a path; the caller owns subsequent reads and writes. Cancel is initially focused
when replacing a file. Cancellation and navigation invalidate pending filesystem checks,
and late results cannot reopen or disable a newer chooser. Temporarily disabled openers
regain focus when ready. Desktop retains native file dialogs.

Settings preserve focus when an asynchronous save temporarily disables a field. Controller
navigation includes disclosure summaries, excludes their closed content and skips controls
reserved for pointer input. Aligning the fullscreen age-limit row makes its actions
reachable from the surrounding preferences. Activity and Details journal editors now
offer the keyboard action, restore their exact invoking controls and use the original
28px fullscreen note text and 240px minimum field height. Empty validation and busy/error
states remain operable and named.

## Source equivalence

`tests/migration-controller-accessibility.json` records the seven accessibility/file-picker
methods; `tests/migration-native.json` completes the three keyboard methods. Each entry
names executed replacement tests and the preserved assertion scope.

- The keyboard runs the original Case/Q/Space/backspace sequence through the production
  Gamepad API poller. All keys have exposed Chromium accessibility names. Five-row bounds,
  the wide Space key, inverted-T arrows and return focus pass on desktop 1200×640 and
  fullscreen 1280×720 and 1920×1080.
- All ten source settings/filter screens run at 1920×1080, plus nine desktop counterparts.
  The Controller guide exists only in fullscreen. Native accessibility nodes expose names
  and enabled states; a breadth-first traversal of actual D-pad moves reaches every
  initially enabled focusable button. It keeps the required target set across saves.
- Both surfaces exercise the combined list prompt with Existing list, an empty disabled
  new-list action, a populated name, a gated save and a definite server error. Busy controls
  reject Back; the error exposes its message and restores all directional routes.
- Both Activity and Details open an empty manual-session journal editor on both surfaces.
  Tests verify field names, validation, keyboard return and exact opener focus. Fullscreen
  adds 1280×720 at 140% text size, bringing every control into the viewport.
- The dynamic modal uses the original disabled Unavailable/initially focused Cancel fixture
  inside the retained Controller settings shell. A test-owned probe supplies action data;
  production rendering, focus isolation and dispatch handle the modal. Actual journal
  openers independently verify desktop and fullscreen return focus.
- Disposable `cover.PNG`, `unrelated.exe` and `report.csv` fixtures preserve the source
  extension, cancellation and overwrite cases. Component checks prove selection never
  writes; a native export check proves the caller writes only after explicit confirmation.
  Deferred inspections also verify cancellation and navigation during pending operations.

## Verification

- Build and typecheck pass: `.tmp/task3815-final-build.log`.
- All **3,388 component/live API cases across 166 files** pass without skips in
  **49.41 seconds** with `npm run test:integration -- --maxWorkers=8`:
  `.tmp/task3815-components-bounded-final.log`. The initial unrestricted run passed 3,387
  cases but exceeded the five-second timeout in an existing integrated Search test.
  That case passed alone, then the complete suite passed with bounded concurrency; no
  assertion or timeout was changed. Initial and focused logs remain in
  `.tmp/task3815-components-final.log` and `.tmp/task3815-search-check.log`.
- **65 distinct native cases pass**, combined from serialized runs. The final result for
  each case is recorded in `.tmp/task3815-native-summary.json`; none is skipped or retried.

  | Suite | Cases | Evidence |
  |---|---:|---|
  | Keyboard | 9 | `.tmp/task3815-keyboard.log`, `.tmp/task3815-keyboard-results/` |
  | File selection | 4 | `.tmp/task3815-file-picker.log`, `.tmp/task3815-file-picker-results/` |
  | Settings accessibility | 19 | `.tmp/task3815-final-native.log`, `.tmp/task3815-final-native-results/` |
  | Modal accessibility | 7 | Same final native batch |
  | Action panels and Avalon modals | 13 | Same final native batch |
  | Existing controller and post-session journal routes | 13 | `.tmp/task3815-regressions-final.log`, `.tmp/task3815-regressions-final-results/` |

  The combined batch first stopped on a Details underline assertion that assumed unscaled
  CSS pixels. Chromium snaps borders after the restored 85% interface zoom. The assertion
  now compares with a 2px reference under the same zoom; all 13 affected controller/journal
  cases pass together in 1.2 minutes. The earlier failures remain in the batch evidence.
- Inspected desktop and fullscreen keyboard geometry, fullscreen journal validation at
  reference and constrained sizes, list prompts, file browsing and Cancel-first overwrite
  confirmation. A final bounded review found no remaining picker race or preference-focus
  issue after their fixes.
- The keyboard hint correction passes all **nine native keyboard cases** again in
  **46.1 seconds**, including exact glyph order, visible vector widths, theme color,
  accessible description, desktop text-only hints and bounds at both fullscreen sizes.
  Evidence: `.tmp/keyboard-hints-native.log`, `.tmp/keyboard-hints-results/` and the
  inspected `keyboard-fullscreen-1920.png` and `keyboard-fullscreen-1280.png` screenshots
  under that result directory. Build/typecheck and all **19 focused component cases**
  pass: `.tmp/keyboard-hints-build.log` and `.tmp/keyboard-hints-components.log`.
- Changed Electron files pass Prettier and `git diff --check` passes. The migration report regenerates successfully.
  `.tmp/task3815-migration-gate.log` records the expected failing complete-migration gate.

## Remaining validation

The inventory has **1,313 ported, 625 retained backend, 30 framework-specific, 384 pending
and 83 partial** methods. **467 methods remain unresolved**, ten fewer than checkpoint 59.
These counts measure source-method evidence, not overall project completion.

Controller input is simulated through the real Gamepad API polling path; physical devices
and TV-distance readability remain assigned to TASK-381.40. Desktop native file dialogs
are stubbed at the Electron boundary to verify options and cancellation, not automated
through the operating-system shell. Saved Steam HTML multi-file selection and developer
theme trust confirmation retain their separate adapters.
The shared .NET implementation is unchanged; this checkpoint does not rerun the complete
.NET or packaged-installer suites.

Fullscreen Filters passes accessibility names and directional reachability, but its shared
form layout and smaller labels still require comparison with the original fullscreen
presentation. That finding is recorded in the existing TASK-381.7; this checkpoint does
not establish its visual parity. TASK-381.6, library loading and live refresh, remains
unstarted until the user prompts continuation.
