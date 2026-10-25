# Controller dispatch and fullscreen Details navigation

Desktop now receives the production controller input path. Directional input operates
ordinary controls, shoulders move within the current focus scope, Accept toggles checkboxes
or opens text entry, and Back closes the active layer. Right-stick scrolling moves one quarter
of the relevant viewport without moving focus. Menu enters fullscreen from desktop.

The shared filter preserves the original 12000/32768 stick deadzone, dominant axis,
30/255 trigger threshold, independent reconnect suppression, 400ms initial navigation repeat
and 110ms subsequent repeat. Delayed frames emit one step. Native mapping belongs to Chromium;
disconnected and nonstandard-mapped devices are ignored. Browser fixtures now identify their
devices explicitly. Menu and Back take precedence over simultaneous Accept.

Fullscreen Details has explicit action, tab, reading and screenshot focus rows. Left/Right
changes tab focus without selecting it; triggers select and focus sections. The selected tab
keeps its accent rule while the focused tab uses the raised surface. Desktop retains automatic
tab selection. Add to list is in fullscreen More, as in the original; its dialog restores More
focus after dismissal. About consumes directional scrolling from its initial Back control,
using the original 160px reading step.

The complete native run on `0b196c5f` found a disabled merge row whose decorative cover still
accepted pointer input. A held identity refresh reproduced the lost click before the fix.
Disabled rows now exclude all their contents from hit testing, alongside their existing
command guards. The unchanged radio-selection assertion and controlled refresh case pass.

## Verification

- Build and typecheck pass: `.tmp/controller-build-final.log`.
- All 3,296 component/live API cases pass across 160 files, without skips, in 50.66s:
  `.tmp/controller-integration-final.log`. The final CSS focus change is checked natively below.
- All 13 final native cases pass without retries or skips in 70.83s:
  `.tmp/controller-native-final.log` and `.tmp/controller-native-final/results.json`.
  They cover desktop keyboard/fullscreen transitions, flyout and prompt containment,
  checkbox activation, disabled-control skipping, exact scrolling, the six 0/1/2 screenshot
  and 100/140% text matrices, About, and both merge presentations.
- The fullscreen 1920x1080 capture at 140% text was inspected. Focus has a raised surface;
  the selected Overview tab retains its separate accent rule. Capture:
  `.tmp/controller-native-final/fullscreen-controller-details.png`.
- The complete preceding native suite passed 347 cases and failed one in 31.0 minutes:
  `.tmp/links-complete-native.log` and `.tmp/links-complete-native/results.json`.
  The targeted final run fixes that failure; it is not a new full-suite passing result.
- The old shared Details tests expected fullscreen tab autoactivation and a store link as
  More's first action. They now assert the original surface-specific selection behavior
  and fullscreen Add to list placement. The other content and focus assertions remain.

Seventeen original methods gain complete evidence: nine input contracts, six desktop
navigation contracts and two fullscreen Details contracts. The inventory is 1,239 ported,
625 retained backend, 23 framework-specific, 447 pending and 101 partial out of 2,435.
Counts describe test disposition, not product completion. Native tests inject browser device
samples; physical controllers and battery reporting remain unverified. The clock/status
header, remaining Details contracts and Electron release/CI cutover are still open.
No .NET source changed; retained Release evidence remains checkpoint 39.
