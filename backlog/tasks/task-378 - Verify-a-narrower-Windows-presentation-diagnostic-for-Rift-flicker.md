---
id: TASK-378
title: Verify a narrower Windows presentation diagnostic for Rift flicker
status: Done
assignee:
  - '@codex'
created_date: '2026-09-29 02:21'
updated_date: '2026-09-29 02:26'
labels: []
dependencies: []
type: spike
ordinal: 414000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user reports that disabling DirectComposition still makes Rift animations look bad after TASK-377. Find one supported process-local comparison that retains DirectComposition, based on the exact bundled Chromium source and graphics capabilities rather than guessing obsolete switches.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Record the unsuccessful DirectComposition-disabled comparison and verify the bundled Electron and Chromium versions.
- [x] #2 Verify one narrower candidate against matching Chromium source and local GPU capability diagnostics, or report that no justified candidate was found.
- [x] #3 Provide a reproducible reversible launch comparison, clearly separating verified support from the still-unconfirmed physical display outcome, without changing global settings or production defaults.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Trace current Chromium presentation switches and exclude controls unrelated to the Rift rendering path. 2. Inspect isolated Electron GPU capability reports for the default and candidate configurations. 3. Record the evidence and provide a single controlled hardware comparison using the existing build.

4. User confirms the latest build still flickers with normal DirectComposition. Prepare and smoke-check a graphics-only startup trace command for the existing package; keep rendering defaults and system settings unchanged.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Runtime verified: Electron 44.4.5, Chromium 152.0.7977.130. Isolated app.getGPUInfo complete probe using a temporary profile reports active RX 9070 XT, AMD driver 32.0.31041.1004, ANGLE D3D11, sandboxed GPU, accelerated compositing/WebGL/rasterization enabled, directComposition true. Source traces show ordinary Rift canvases are not low-latency and the default video-overlay processor only accepts video/low-latency quads. BufferQueue is gated on DCompDynamicTexture support, which this report does not establish; do not present it as a verified fix. Latest user confirms both accelerated flicker and poor disabled-DirectComposition animation remain.

No narrower rendering switch was sufficiently justified by the matching source and capability report. Verified Chromium startup-tracing flags by launching the same Electron runtime with an isolated temporary profile; generated valid JSON containing 117 GPU/runtime events. Documented a 45-second existing-package capture command and fullscreen reproduction sequence. No app implementation or renderer defaults changed; native flicker remains unresolved pending the user-generated trace.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Confirmed that the DirectComposition workaround remains unacceptable and the latest accelerated build still flickers. Verified exact runtime, local accelerated GPU capabilities and relevant Chromium paths. Prepared and smoke-tested a local startup graphics trace command instead of recommending unverified switches. The display issue remains unresolved; physical reproduction and trace analysis are still required.
<!-- SECTION:FINAL_SUMMARY:END -->
