---
id: TASK-291
title: Record Winnow walkthrough and illustrate documentation
status: Done
assignee: []
created_date: '2026-09-14 23:31'
updated_date: '2026-09-14 23:55'
labels: []
dependencies: []
ordinal: 333000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Documentation needs real app visuals and a one-minute walkthrough demonstrating current desktop and fullscreen behavior.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A verified one-minute demo video is recorded from an isolated local Winnow session with ffmpeg.
- [x] #2 Relevant docs include clear screenshots captured from the real app, with descriptive captions and alt text.
- [x] #3 Media and docs build are verified; capture setup and any limitations are recorded.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Build isolated demo app, prepare nonpersonal sample library and public artwork, drive UI with Computer Use, capture ffmpeg footage and screenshots, integrate optimized visuals into docs, validate media and site build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Captured real desktop and fullscreen interactions from isolated sample data. Added a 60-second H.264 walkthrough with chapter captions and transcript, eight WebP screenshots with full-size links, and media URL validation in the Pages build. Verified native UI states, video chapter contact sheets, clean stills, full ffmpeg decode, build:pages, and scoped oxlint. Full website lint has unrelated existing components/ui errors. Capture provenance and limitations: docs/spikes/2026-09-14-documentation-media.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Recorded and integrated a one-minute real-app demo and eight documentation screenshots. Static site build, scoped lint, full video decode, and visual media review passed. Real user library was not modified; site not published.
<!-- SECTION:FINAL_SUMMARY:END -->
