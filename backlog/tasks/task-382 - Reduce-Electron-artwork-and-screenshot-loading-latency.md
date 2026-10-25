---
id: TASK-382
title: Reduce Electron artwork and screenshot loading latency
status: Done
assignee:
  - '@codex'
created_date: '2026-10-03 16:38'
updated_date: '2026-10-03 16:55'
labels: []
dependencies: []
documentation:
  - docs/spikes/electron-artwork-performance.md
  - src/Winnow.Electron/README.md
priority: high
type: enhancement
ordinal: 460000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user reports noticeably slower covers and screenshot loading in Electron than in the retained Avalonia frontend. Investigate the actual delivery and decode paths, improve confirmed avoidable costs, and preserve image lifetime, bounded resource use and recovery behavior.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Controlled before/after measurements identify the loading costs and show an improvement for covers and screenshots, with methodology and limits recorded.
- [x] #2 Desktop and fullscreen retain correct images, responsive navigation, cancellation, bounded concurrency and cache/retry behavior.
- [x] #3 Targeted component, transport and native image tests pass, and current architecture documentation explains the delivered path.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Compare reference frontend and backend transport with Electron decode, cache and screenshot scheduling. Measure representative cover batches and large screenshots using isolated deterministic fixtures. Implement the strongest supported improvements without exposing backend credentials or weakening image validation. Verify recovery, cancellation and both presentation surfaces; record repeatable before/after results and commit locally.

Measured renderer conversion before changes: 24 textured 240x360 covers took 255.9ms median, one 1280x720 screenshot 104.6ms, and three 1920x1080 backdrops 808.4ms (five trials after warmup, fetch excluded). Replace per-byte callback conversion with native decoding and add a binary main/preload image path while retaining string compatibility. Add same-asset thumbnail fallback to the lightbox and verify cancellation/cache bounds. Keep backend encoded-rendition and persistent-cache work outside this focused pass until separate evidence warrants it.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented a Uint8Array image channel through validated main/preload, retained the string bridge with native base64 conversion and an indexed fallback, and updated shared images and Avalon backdrops. Desktop screenshot lightboxes borrow a decoded same-asset thumbnail until their 1280px replacement arrives; fullscreen previews directly share that rendition. Existing ownership, cancellation, freshness, retry and memory/concurrency limits remain.

Measured before/after in native Electron 44.4.5 with deterministic textured PNGs, six decode permits and median of five trials after warmup. Initial binary results: 24 covers 255.9 to 29.1 ms; screenshot 104.6 to 17.3 ms; three backdrops 808.4 to 100.1 ms. Reproducible benchmark tool independently confirmed the improvement. Results exclude downloads, backend conversion, IPC and final paint; no whole-app Avalonia comparison is claimed. Methodology, repeat results and remaining cache differences are documented in docs/spikes/electron-artwork-performance.md.

Validation: 365 focused tests passed across 19 component/transport files; 30 native cases passed covering both surfaces, idle recovery, backdrop selection/replacement, gallery navigation and held binary image upgrade. The initial artwork-state setup failed because its fixture path was unset; all five cases passed after configuring the existing fixture executable. TypeScript, production build, migration completeness (2435 methods; zero pending/partial) and diff whitespace checks passed. Build retains the existing Zod Rollup annotation warnings. All native launches used explicit disposable data directories.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed avoidable per-byte renderer conversion and base64 IPC copies, and show existing screenshot thumbnails during full-image loading. Controlled measurements demonstrate a large reduction in renderer conversion/decode time. Verified with 365 focused tests, 30 native desktop/fullscreen cases, type/build checks and complete migration coverage. Downloads and backend rendition caching remain outside this measured renderer improvement.
<!-- SECTION:FINAL_SUMMARY:END -->
