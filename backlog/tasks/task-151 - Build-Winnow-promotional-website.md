---
id: TASK-151
title: Build Winnow promotional website
status: Done
assignee:
  - '@codex'
created_date: '2026-09-07 21:14'
updated_date: '2026-09-07 21:36'
labels: []
dependencies: []
references:
  - design-system.md
  - game-library-design.md
  - README.md
type: feature
ordinal: 178000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Create a polished promotional website for Winnow with a consumer landing page and a developer-focused page that explains the local-first architecture through an embedded Archify diagram.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The consumer page explains Winnow's value, privacy model, supported libraries, and primary action in consumer language
- [x] #2 A distinct developer page documents the implementation stack and embeds a validated Winnow architecture diagram
- [x] #3 Both pages are responsive, keyboard accessible, and visually aligned with Winnow's design system
- [x] #4 The production website build succeeds
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Read the product, architecture, and visual specifications and establish a web-specific Winnow visual thesis. 2. Scaffold a dedicated static site in the repository and implement the consumer route. 3. Reuse the repository's already validated Archify runtime diagram and embed it in a distinct developer route. 4. Verify compiled content, accessibility hooks, responsive breakpoints, brand tokens, route availability, and the production build, then publish privately.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Built a dedicated Sites project under website/ with consumer and developer routes, reused Winnow fonts, palette, dragon mark, and a cropped real-library visual, embedded the validated Archify runtime diagram, and produced a successful static production build. Both routes and all required assets return HTTP 200 in local preview.

Final verification: npm run build exited 0 and prerendered / and /developers; local HTTP checks returned 200 for both pages, the Archify viewer, screenshot, and dragon mark; compiled-artifact assertions passed for consumer copy, platform coverage, CTA, developer stack, diagram embed, focus-visible styling, mobile breakpoint, Winnow palette, and all three brand typefaces. Sites version 1 deployed successfully to the owner-private production URL.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Built and privately published a two-route Winnow promotional site. The consumer route turns the living-library dormancy and unread-patch language into the hero experience; the developer route documents the local-first module boundaries and embeds the validated interactive Archify runtime diagram. Verified the static production build, all local routes/assets, and compiled accessibility, responsive, brand, content, and diagram checks.
<!-- SECTION:FINAL_SUMMARY:END -->
