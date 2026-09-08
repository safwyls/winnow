---
id: TASK-152.2
title: >-
  Halve the decoded-cover footprint: optional floor layer, smaller budget,
  disposal on eviction
status: Done
assignee:
  - '@claude'
created_date: '2026-09-07 22:21'
updated_date: '2026-09-08 00:27'
labels: []
dependencies: []
references:
  - src/Winnow.Covers/CoverCache.cs
  - src/Winnow.Covers/DecodedLru.cs
  - src/Winnow.Covers/CoverCacheOptions.cs
  - src/Winnow.Covers/CoverLease.cs
  - src/Winnow.App/ViewModels/MergeQueueViewModel.cs
documentation:
  - docs/spikes/memory-footprint.md
parent_task_id: TASK-152
priority: high
ordinal: 182000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Every decoded cover is kept as two Avalonia WriteableBitmaps (vivid plus dormancy floor) on the native heap, sized by width bucket: a 148-DIP tile at 100% DPI decodes at the 240 bucket, 691 KB per cover, and the 480 bucket at 200% DPI is 2.76 MB. DecodedLru caps the pairs at 128 MiB and does not dispose evicted bitmaps, so native memory returns only after a gen-2 GC runs the finalizers. The floor is decoded and cached even for consumers that draw only the vivid layer (details cover, screenshot thumbnails, the lightbox at 1280 px is 7.4 MB per shot, IGDB match rows, metadata editor previews) and even when the display ramp does not dim dormant covers. Several view models hold raw Bitmap references outside the lease pool, which is what blocks disposal on eviction. The merge queue requests covers for every row in a non-virtualized ItemsControl. The ranked list is in docs/spikes/memory-footprint.md (cover pipeline section); the original sizing note is in docs/spikes/avalonia-dormancy-rendering.md.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A cover request can ask for the vivid layer only; screenshots, the lightbox, details, IGDB rows and editor previews use it, and a wall with dimming off holds one bitmap per cover
- [x] #2 Evicted, unleased art is disposed immediately and no consumer can draw a disposed bitmap (raw Bitmap holders migrated to leases or cleared on close)
- [x] #3 MaxDecodedBytes is re-justified from the measured screenful size in CoverCacheOptions and its rationale comment, and decode concurrency is bounded
- [ ] #4 Scrolling the whole 1,039-game grid at 100% DPI adds no more than 60 MB private bytes over the post-startup figure, measured with docs/spikes/memory-footprint.ps1 -KeepRunning
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a layers dimension to the cover request and cache slot so vivid-only entries do not decode or cache the floor; consumers that draw only the vivid layer (details, screenshots, lightbox, IGDB rows, editor previews) and any wall with dimming off request vivid only. 2. Give the LRU ownership of disposal for unleased art and migrate the raw Bitmap holders to leases or clear-on-close; document the UI-thread release invariant. 3. Bound decode concurrency; stop the merge queue requesting covers eagerly; drop the redundant floor existence read. 4. Re-justify MaxDecodedBytes from the measured screenful; measure live WriteableBitmap count and private bytes before and after. Delegated to an Opus subagent in worktree C:\Temp\wt\152-2 (branch mem/152-2).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Subagent (Opus) finished on branch mem/152-2, commit 2c023dc (49 files). Winnow.Covers: new CoverLayers (Vivid | VividAndFloor) in the cache and lease slot key, a vivid-only request satisfied by a cached pair; CoverArt carries a hold count (LRU holds first, leases hold the rest) and frees layers on the UI thread when the last hold goes; DecodedLru reports evictions; MaxDecodedBytes 128 -> 32 MiB with the screenful argument in the comment; MaxConcurrentDecodes (half the cores, 2-6); no floor decode or floor file for vivid-only requests; HasFloor existence check replaces the duplicate full read. App: CoverPresenter asks for both layers only while the ramp dims and re-requests when dimming is switched on; new LeasedCover for one-shot surfaces; details, screenshots, lightbox, IGDB rows, editor previews and merge sides migrated to leases; GameDetailsViewModel is IDisposable; merge queue requests covers per visible section only once the pane is shown. Measured with a Win32-message scroll harness (965-title copy, 1280x820, 100% DPI, --no-sync): scrolled to the bottom 506.9 -> 328-368 MB private; live WriteableBitmaps after a full scroll 1,226 -> 286 (249 for 249 covers with dimming off); at rest 238 -> 60. Tests: 3,972 pass, 2 skip, 0 warnings; 23 new tests. AC 1-3 met; AC 4 not met as written (a full scroll adds 77-106 MB over post-startup vs the 60 MB asked; the residue is GPU texture uploads and GC committed scatter, not cache-held bytes). design-system.md 5.4 now states the floor is not decoded when the ramp cannot be seen; superseded sentences in docs/decisions.md. Integration pending.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Cover requests carry CoverLayers (Vivid | VividAndFloor); the floor is decoded and cached only while the display ramp dims and only for surfaces that draw it; CoverArt holds a reference count so the LRU frees evicted, unleased art on the UI thread; every raw Bitmap holder (details, screenshots, lightbox, IGDB rows, editor previews, merge sides) now goes through a lease; MaxDecodedBytes 128 -> 32 MiB with the screenful argument in its comment; decode concurrency bounded; merge queue requests covers per visible section only when shown; the duplicate floor-file read is gone. Verified with a Win32-message scroll harness on the real library copy: scrolled-to-bottom private bytes 506.9 -> 328-368 MB; live WriteableBitmaps after a full scroll 1,226 -> 286 (249 for 249 covers with dimming off); 23 new tests, full suite passes. AC 4 left unchecked: a full scroll still adds 77-106 MB over post-startup (target 60); spike section 10.4 attributes the residue to GPU texture uploads and GC committed scatter rather than cache-held bytes.
<!-- SECTION:FINAL_SUMMARY:END -->
