---
id: TASK-292
title: Give desktop and fullscreen separate paced walkthroughs
status: Done
assignee: []
created_date: '2026-09-15 00:24'
updated_date: '2026-09-15 00:36'
labels: []
dependencies: []
ordinal: 334000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The combined one-minute demo moves too quickly. Each UI needs its own 1–2 minute walkthrough with time to read screens and follow interactions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop and fullscreen each have a standalone 60–120 second real-app video with readable pauses.
- [x] #2 Documentation embeds both videos with accurate captions and transcripts.
- [x] #3 Finished videos and the static docs build are verified.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Review desktop source footage for a slower edit; record additional fullscreen interactions in isolated sample data; encode separate walkthroughs, update docs and capture evidence, validate and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop is 104.00 seconds; fullscreen is 112.97 seconds. Reused unsped desktop footage and recorded new fullscreen browsing, overview, gallery, and Appearance in isolated sample data. Excluded a Library take with missing artwork. Reviewed contact sheets; both H.264 files fully decode without errors. Scoped oxlint and npm run build:pages pass, including media URL verification. Replaced combined player with shared standalone players and synchronized captions/transcripts; updated capture evidence.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered separate slower desktop (1:44) and fullscreen (1:53) walkthroughs with posters, captions and transcripts. Verified finished media and static documentation output. No app behavior changes or deployment.
<!-- SECTION:FINAL_SUMMARY:END -->
