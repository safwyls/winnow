---
id: TASK-317
title: Revamp promo site around clear product explanations
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 00:29'
updated_date: '2026-09-17 00:43'
labels: []
dependencies: []
ordinal: 359000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User wants a more distinctive and substantial promo site with direct copy, retaining the hero and interactive game demos. Reflect permanent free, open-source, donation-supported intent and remove the developer diagram nested scrolling experience.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Home preserves hero carousel and interactive demos while replacing contrived copy and expanding practical product explanations.
- [x] #2 Layout has distinct responsive sections and clear permanent-free donation-supported messaging.
- [x] #3 Developer architecture is visible without nested scrolling or embedded navigation; standalone tools remain available.
- [x] #4 Site build, type and asset checks pass and local preview is available.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Revise homepage sections and demo copy using shipped app behavior; restyle around larger real screenshots, readable interactive examples and a practical workflow; show the existing architecture as a full-width diagram with a separate interactive link; validate and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented the revised homepage with larger alternating interactive demos, a detailed import/recommend/play workflow, organization and fullscreen screenshots, practical FAQ, and permanent-free donation-supported copy. Preserved the seven-image hero carousel and closing headline. Developer architecture now uses a responsive SVG extracted from the existing snapshot, with a separate link to the interactive viewer. Updated site maintenance documentation. App desktop and fullscreen code are unaffected; the site explains both surfaces. Validation: Pages build prerendered all eight routes and passed local asset/anchor checks; TypeScript passed; generated SVG parsed as XML; local home and developer URLs returned HTTP 200; git diff --check passed. Browser visual and interaction QA was not run: Sites skill prohibits it unless explicitly requested. AC 1-3 remain unchecked pending that verification; implementation is ready for review.

User reviewed the local preview and explicitly approved the revised design (Much better, like this a lot more) before requesting commit, full-suite validation, and a PR. This supplies the manual design acceptance missing from the previous handoff; no automated browser interaction or responsive-device QA is claimed. Repeated Pages build and TypeScript checks passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Rebuilt the promo homepage around practical product explanations, larger interactive examples and real desktop/fullscreen views. Preserved the hero carousel and closing headline, clarified free/open-source donation funding, and placed the developer architecture directly in the page. Verified by Pages build and asset/anchor checks, TypeScript, SVG XML parsing, local HTTP checks, and user review of the preview.
<!-- SECTION:FINAL_SUMMARY:END -->
