---
id: TASK-316
title: Rotate supplied app screenshots in website hero
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 00:13'
updated_date: '2026-09-17 00:17'
labels: []
dependencies: []
ordinal: 358000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Replace the single library-grid hero image with the eight supplied desktop and fullscreen screenshots.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Hero rotates through all eight supplied screenshots without cropping and has manual navigation and pause controls.
- [x] #2 Carousel respects reduced motion, keyboard focus and responsive layout; website build and asset checks pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Copy original screenshots into website assets, compose the existing carousel primitive with autoplay and accessible controls, preserve hero styling, build the site and provide a local preview.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced the static hero with the installed carousel primitive and all eight supplied screenshots, copied unchanged to descriptive asset names. Full-frame object-fit containment preserves desktop/fullscreen aspect ratios inside a stable stage. Added captions, eight direct selectors, previous/next, pause/resume, 5.5-second rotation, hover/focus/visibility pause and reduced-motion opt-out. Manual choice or drag stops autoplay. Website-only change; desktop and fullscreen app source unchanged, both represented in screenshots. Validation: production build:pages passed for all routes and link/asset checks; TypeScript noEmit passed; rendered HTML includes all eight image paths and exported assets match source hashes. Local HTTP returned200 and preview handoff queued at http://localhost:3000/. Browser interaction/visual QA not run under Sites skill default. Sites build wrapper failed locating npm on Windows; used the existing authoritative Pages build successfully. No deployment requested or performed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Hero now presents all eight supplied app screenshots in an accessible rotating carousel with manual navigation, pause, uncropped images and reduced-motion support. Production Pages build, TypeScript and eight-image export checks pass; local preview is available. No deployment performed.
<!-- SECTION:FINAL_SUMMARY:END -->
