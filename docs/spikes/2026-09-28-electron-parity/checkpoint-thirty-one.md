# Packaged Windows activation — 2026-09-29

The unpacked Windows Electron distribution now has a separate native test command. Its
actual executable and Windows shortcuts exercise single-instance handoff using the bundled
self-contained backend. Explicit `--data-dir` runs no longer claim the global `winnow://`
association. The ordinary packaged profile retains automatic protocol registration;
profile-scoped Jump Lists remain available.

## Verification

- The complete .NET Release build/test run passes 6,863 tests across thirteen assemblies,
  including all 896 Avalonia headless tests. Two Linux-only process tests explicitly skip
  on Windows. Evidence: `.tmp/activation-regression.log` and all thirteen TRX files under
  `.tmp/activation-regression-results`. This is a fresh full run after the additive
  Primary action contract; earlier full-suite results are not substituted.
- `npm run package` succeeds, building the TypeScript app, publishing a self-contained
  Windows x64 backend, collecting notices for 75 production packages and creating
  `release/win-unpacked/Winnow.exe`. The executable is unsigned. Evidence:
  `.tmp/electron-package-activation.log`; executable, ASAR and backend hashes are retained
  in `.tmp/packaged-activation-native/artifact-hashes.json`.
- All nine packaged native checks pass in 18.5s:
  `.tmp/packaged-activation-native.log` and its copied `results.json`. The fixture asserts
  `app.isPackaged`, clears the backend-path override, and uses a temporary library path
  containing spaces. Direct executable launches and real `.lnk` shortcuts each deliver
  show, fullscreen, game and plugin activations. The original backend PID and epoch stay
  unchanged; fullscreen renders through the production page.
- Shortcuts are created, read back and opened with Electron's
  [documented native shell APIs](https://www.electronjs.org/docs/latest/api/shell).
  Every shortcut remains beneath the fixture directory. Game/provider messages are
  intercepted after main-process dispatch so the probe does not launch external software.
  Existing development native tests separately verify plugin presentation and invalid
  startup rejection before database creation.
- A read-only hash of the current user's protocol registry key is identical before and
  after the packaged launches. Only the temporary profile's Jump List is cleared during
  cleanup. Nothing is installed or published.
- The protocol-registration decision passes its focused check; final TypeScript checking
  passes. All 2,908 component and live API cases pass in 145 files without skips in 51.10s:
  `.tmp/packaged-activation-full-integration.log`.

The original process-launch matrix is now ported, including the packaged apphost and
Windows shortcut variants. Inventory: 1,056 ported, 559 retained backend,
17 framework-specific, 695 pending and 108 partial. The current 222-case development
native suite has not yet run together; the last complete development native pass remains
214 cases. Installer/update recovery and other desktop platforms remain separate validation
work. The overall frontend migration is incomplete.
