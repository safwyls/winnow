---
id: TASK-225
title: Bound and drain artwork work across consumer release and shutdown
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 08:27'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Covers/CoverLease.cs:125'
  - 'src/Winnow.Covers/CoverCache.cs:113'
  - 'src/Winnow.Covers/CoverCache.cs:212'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: low
type: enhancement
ordinal: 256000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R37. Evidence: Source-verified lifecycle risk; stress impact unmeasured. Cover leases deliberately pass CancellationToken.None for shared loads. Releasing the last lease does not cancel pending work; the decode semaphore bounds active decodes but not queued requests. CoverCache.Dispose clears memory and disposes the gate/pipeline without draining loads that can still admit art. ConcurrentDictionary.GetOrAdd also permits duplicate task factories. Fast navigation or shutdown can perform abandoned work, create duplicate loads or race disposal. Preserve shared bitmap ownership while establishing an explicit host lifetime and bounded admission policy.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Artwork loading has an explicit host lifetime and bounded pending admission; loads no longer needed by any consumer are cancelled or deprioritized safely.
- [x] #2 Shutdown prevents new admissions and drains/cancels existing work before disposing shared resources; same-slot requests have reliable single-flight behavior.
- [x] #3 Controlled stress tests cover rapid scroll/release, concurrent same-slot requests and disposal during fetch/decode with no bitmap leaks or publication after disposal on either surface.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Give shared artwork loads reference-counted cancellation, bounded admission and a host shutdown lifetime. Serialize per-slot task creation, drain before disposing resources, and cover release, contention and shutdown with controlled tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The cache serializes per-slot task creation, caps admitted running/queued slots at 128 by default, and cancels loads when their last consumer releases. Shutdown closes admission, cancels and drains workers, then disposes the cache and pipeline. Bitmap conversion failures clean up partially created layers and shutdown rejects late publication.

Independent final review reproduced callback exceptions bypassing shutdown cleanup and replacing final-waiter cancellation, conversion escaping MaxConcurrentDecodes, and a native vivid bitmap remaining allocated after floor decode failure. Callback notification now runs asynchronously outside the cache lock with faults logged; shutdown continues draining. The decode gate covers Avalonia conversion, and native partial results are released. Five new assertions failed before their fixes; a sixth confirmed existing partial Avalonia cleanup. All 159 cover tests and 24 headless cases pass with no skips; desktop/fullscreen consumers and unchanged dormancy tokens are covered. The build spec and architecture-fixes spike document the contracts and measurement. Source remains subject to the coordinator's full integration gate.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Controlled headless tests cover 100 direct callers, 100 shared leases, 80 rapidly released consumers against a four-slot admission limit, final-lease cancellation, shutdown during fetch and bitmap conversion, retained bitmap ownership and desktop/fullscreen detach recovery. Nine lifetime/retry cases and ten composition cases passed; 157 cover and 149 model regressions also passed. Custom sources must eventually complete after cancellation for draining shutdown to finish; built-in transports and the plugin host provide cancellation boundaries.
<!-- SECTION:FINAL_SUMMARY:END -->
