---
id: TASK-288
title: Add setup guides and plugin SDK documentation to promo site
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 04:21'
updated_date: '2026-09-14 04:35'
labels: []
dependencies: []
ordinal: 330000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Players need walkthroughs for installing and configuring Winnow, and plugin authors need a public, navigable SDK reference alongside the promo site.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Docs navigation and searchable guides cover setup, configuration, plugin installation and SDK authoring using verified current behavior.
- [x] #2 SDK includes buildable example, contracts, manifest, host services, lifecycle, limits and troubleshooting.
- [x] #3 Static Pages export includes all docs routes and passes link, build and responsive browser checks.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect site and source; write player guides and SDK reference in parallel; implement shared docs layout and navigation, integrate Pages export, compile SDK example, and validate desktop/mobile rendering and links.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added documentation landing page and four guides with shared reading layout, full-text browser-local search, keyboard links, mobile section disclosure, per-page metadata and source downloads. Content verified against actual desktop/fullscreen settings and plugin host/SDK; no application behavior changed. SDK example compiled against locally packed 1.0.0 with zero warnings/errors. Verified TypeScript, targeted oxlint, static Pages builds at root and /winnow, internal links/assets/fragment anchors, and rendered browser checks at 1440x1000, 1280x820 and 390x844. Search result, empty state, clearing, Tab navigation and cross-page anchor scrolling exercised; mobile tables remain within viewport. Added docs links to player/developer header/footer and corrected existing plugin guide shelf-count wording. No production deployment; normal Pages workflow publishes after merge to main.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added public setup/configuration/plugin guides and complete API 1 reference with a compiled downloadable example. Static builds, links, TypeScript/lint and desktop/mobile browser checks pass.
<!-- SECTION:FINAL_SUMMARY:END -->
