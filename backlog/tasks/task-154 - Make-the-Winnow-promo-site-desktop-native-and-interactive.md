---
id: TASK-154
title: Make the Winnow promo site desktop-native and interactive
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 01:51'
updated_date: '2026-09-08 02:44'
labels: []
dependencies:
  - TASK-151
references:
  - website/app/page.tsx
  - website/app/globals.css
  - design-system.md
type: enhancement
ordinal: 186000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Refine the promotional site so wide desktop viewports use the available canvas and the consumer explanation section demonstrates Winnow with realistic, interactive game tiles populated from real game metadata.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 At desktop widths above 1280px, the hero and main sections use substantially more of the viewport without introducing horizontal overflow
- [x] #2 The A backlog that can speak section presents at least three realistic Winnow game tiles with real cover art and game metadata
- [x] #3 Pointer hover and keyboard focus demonstrate dormancy restoration, unread-patch state, and explanation details
- [x] #4 The consumer and developer routes remain responsive and the production build succeeds
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Preserve the wider desktop layout and original three-part story. 2. Use focused interactive game tiles that match Winnow’s dormancy, unread, and hover behavior. 3. Apply the annotated platform language, straight hero image, IGDB metadata input, blocked external path, and closing spacing. 4. Verify both responsive routes and the production build. 5. Stage the public-site revision and deploy only after explicit approval.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
User clarified that the interaction belongs inside the original three information cards; the full CSS mini-window is being removed.

Replaced the misunderstood full mini-window with three focused interactive story cards. The original dormancy, unread update, and recommendation explanation copy is preserved. Production build passed and both local routes returned HTTP 200. Version 3 is staged for deployment; the live Site is public, so deployment awaits explicit approval.

Matched the interactive examples to the supplied Winnow tile reference: compact idle labels, larger ringed/glowing Flare badge, vivid patch/reason covers, and the real hover/focus overlay structure with title, data line, store chip, action, and Details fold. Production build passed; both local routes returned HTTP 200.

Applied all six browser annotations: added IGDB as a one-way metadata source, drew a faded crossed-out route to someone else’s computer, increased the beta-note spacing, changed launchers to platforms, removed the hero screenshot rotation, and expanded the no-write-back copy. A clean production build completed after restarting the retained preview process; root returned 200 and the developer route redirected canonically as expected.

User manually reviewed the wide desktop layout and interactive feature tiles in the browser, supplied targeted annotations, and approved the corrected preview. Production build completed with exit code 0 and prerendered both / and /developers. Public Sites deployment version 4 succeeded.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Published the wider Winnow consumer and developer promo site with focused, app-faithful interactive game tiles, real game art and metadata, the updated library screenshot, the Archify developer diagram, and the annotated privacy-flow refinements. Verified through the user's browser review, a clean production build, successful route prerendering, and a successful public deployment.
<!-- SECTION:FINAL_SUMMARY:END -->
