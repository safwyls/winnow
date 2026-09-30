# Session journal prompts — 2026-09-29

Desktop now presents the original compact, bottom-left journal dock with title, duration,
single-line note and retractable rating dots. Fullscreen presents a separate large editor
with an on-screen keyboard, rating controls and controller hints. Its feedback and current
rating respect enlarged text. Both surfaces retain the default-off preference, exact sitting,
untouched timeout, dismissal without writes and drafts through a failed or pending save.
A sitting received during editing is discarded before asynchronous reads, so it cannot
reappear after the current save finishes. A passive offer leaves focus alone; the first
controller activation enters the fullscreen editor instead of acting on its covered page.

A journal PUT now completes after its frontend disconnects. A controlled HTTP test first
demonstrated that the old request-cancellation token abandoned an accepted write. The fixed
endpoint preserves the transaction and revision check, finishes the note and publishes the
committed change. Graceful backend shutdown drains an accepted request. A lost response
still requires reconciliation; an authoritative unchanged revision permits the same Save
button to retry, while a changed revision requires an explicit choice.

## Verification

- All 2,966 component and live API cases pass in 146 files without skips, in 51.32s:
  `.tmp/journal-full-integration.log`. The focused journal, activity and controller group
  passes all 77 cases in four files: `.tmp/journal-focused-final.log`.
- All 115 backend HTTP tests pass, including six new journal cases:
  `.tmp/journal-backend-green.log`. The preceding controlled disconnect failure is retained
  in `.tmp/journal-backend-red.log`; the other five new cases already passed there.
- Build passes with type checking: `.tmp/journal-build-final.log`. Release and Debug backend
  builds pass; the latter supplies sample data for the isolated Electron fixtures.
- All three native journal workflows pass in 26.8s:
  `.tmp/journal-native-complete.log` and the copied JSON and captures in
  `.tmp/journal-native-complete`. They cover desktop, 720p fullscreen and fullscreen at 140%
  text, opt-in, controller keyboard entry, clearing a rating, partial drafts, retrying the
  same exact session/revision, and disabled controls during a pending write.
- Final desktop and enlarged fullscreen captures were inspected. Earlier runs exposed a
  controlled-textarea label lookup problem, undersized fullscreen feedback and a delayed
  offer race. Explicit label association, scaled feedback and an arrival-time draft guard
  correct them. The native event fixture also waits for renderer delivery before releasing
  a pending request. Its first run used a Release backend without demo seeding; the empty
  library was corrected by using the verified Debug build.
- The preceding frozen `ca9b84f3` build passes all 225 native cases together in 19.6 minutes.
  A combined 228-case run with this package is not yet claimed.
- The complete .NET Release build/test run passes 6,869 cases across thirteen assemblies,
  including 896 Avalonia UI cases, with two Linux-only cases skipped on Windows:
  `.tmp/journal-regression.log` and the thirteen TRX files in `.tmp/journal-regression-results`.

All fourteen original JournalPrompt methods have named replacement evidence. The original
backend event gate remains in production; no frontend observes session process events
directly. Inventory: 1,082 ported, 559 retained backend, 17 framework-specific, 669 pending
and 108 partial, out of 2,435 frozen source methods. The overall migration remains incomplete.
Actual Windows notification display policies and physical-controller/TV checks remain
outside these automated fixtures.
