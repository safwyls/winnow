# Fullscreen clock and controller status — 2026-09-30

TASK-381.2 restores the local short-time clock and controller status on all four fullscreen
root pages and Details. The clock uses the configured data font and refreshes every
15 seconds, matching the original view. Equal side regions preserve the navigation's
center; long status text truncates. Desktop retains its existing header.

## Native boundary

Chromium remains the input and device-selection authority. Its
[Gamepad interface](https://www.w3.org/TR/gamepad/) has no battery field and its browser
index does not identify a Windows XInput slot. A static, read-only PowerShell/C# helper
loads system-directory XInput in the main process. Battery reads cache for 30 seconds;
disconnect clears that slot's cache. Renderer samples pass through a named, validated
preload method; no sample enters executable code or a command line.

The helper normalizes controls using Chromium's
[XInput mapping](https://raw.githubusercontent.com/chromium/chromium/main/device/gamepad/xinput_data_fetcher_win.cc).
Only a unique matching native state may supply a label. Identical simultaneous devices,
unsupported devices/platforms and unknown readings leave the battery absent. This is a
deliberate limit: their generic connection status remains available without attributing
one device's battery to another. Known labels preserve the original wired and four-level
wireless wording, consistent with Microsoft's
[battery information fields](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_battery_information).

The helper closes when the component detaches, the selected controller disconnects,
the window hides, or native fullscreen exits. Late replies cannot overwrite a new device
or reconnect state. Time refreshes on focus/visibility return and its timer stops on detach.

## Verification

- Build and typecheck pass: `.tmp/controller-status-build.log`.
- All **3,351 component/live API cases in 164 files** pass without skips in 59.03 seconds:
  `.tmp/controller-status-integration.log`. The 27 new cases include all five original
  battery rows, all known levels, native response validation, ambiguous/active device
  matching, helper failures/backoff/cleanup, clock timing and stale replies.
- Windows tests compile and execute the real helper against system XInput without
  assuming connected hardware. A second run replaces only its native-call and time
  boundaries to execute the production cache, disconnect reset and control normalization.
- **23 native Electron cases** pass without retries or skips in 1.7 minutes:
  `.tmp/controller-status-native-final.log` and
  `.tmp/controller-status-native-final/results.json`. They cover the new status path,
  existing desktop/controller input, Details focus rows and both surfaces' layout bounds.
- The seven status cases also run with explicit waits for saved scale preferences to
  reach the renderer: `.tmp/controller-status-native-scales.log` and
  `.tmp/controller-status-native-scales/results.json`. Geometry replays the original
  empty/disconnected/long status and `1:01`/`11:59 PM` clock fixtures at 1280×720,
  adds 1920×1080, and covers text/interface scales 1/1 and 1.4/1.2. Assertions preserve
  the original 0.5px navigation-center and 1px clock-right tolerances.
- Native fullscreen restoration shows desktop chrome and the retained desktop page,
  removes the fullscreen status, and leaves no probe process owned by the fixture.
  Screenshots of fullscreen Library, Details and restored desktop were inspected.
  Images and the machine-readable results remain under the native evidence directories.

Two existing Details tests used an unsupported Testing Library `exact` option. Removing
it restores typechecking; string role names already require an exact match.

## Remaining validation

Three source methods gain complete evidence. The inventory now has **1,267 ported,
625 retained backend, 30 framework-specific, 417 pending and 96 partial** methods.
`npm run migration:report` succeeds; the complete migration gate correctly still fails
for the 513 unresolved methods (`.tmp/controller-status-migration-gate.log`).

Native boundary fixtures simulate battery/device states. They do not establish physical
controller behavior, which remains TASK-381.40. This checkpoint does not rerun the full
native aggregate or .NET suite; the final migration/release gates remain queued. Work
stops here for review before TASK-381.3 begins.
