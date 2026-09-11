---
id: TASK-225
title: Bound and drain artwork work across consumer release and shutdown
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:07'
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
- [ ] #1 Artwork loading has an explicit host lifetime and bounded pending admission; loads no longer needed by any consumer are cancelled or deprioritized safely.
- [ ] #2 Shutdown prevents new admissions and drains/cancels existing work before disposing shared resources; same-slot requests have reliable single-flight behavior.
- [ ] #3 Controlled stress tests cover rapid scroll/release, concurrent same-slot requests and disposal during fetch/decode with no bitmap leaks or publication after disposal on either surface.
<!-- AC:END -->
