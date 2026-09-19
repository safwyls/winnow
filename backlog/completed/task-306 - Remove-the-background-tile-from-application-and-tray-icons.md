---
id: TASK-306
title: Remove the background tile from application and tray icons
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 04:42'
updated_date: '2026-09-16 04:45'
labels: []
dependencies: []
type: enhancement
ordinal: 348000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user wants a transparent dragon silhouette for the application and tray icons, replacing the rounded background square.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Application and tray share a transparent dragon icon with no background tile across all seven frame sizes.
- [x] #2 Small icon frames remain readable on light and dark backgrounds; application builds with the regenerated icon.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Regenerate the shared ICO directly from the original SVG, using a thin silhouette outline for contrast and preserving native frame sizes. Inspect light/dark previews, validate transparency and embedded frames, and build the application. Update icon documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Regenerated the shared dragon.ico from the original SVG with transparent background, warm-white fill and a thin dark contour for light taskbars. Preserved seven native sizes and small-frame dilation; DIB through 48px, PNG from 64px. Added scripts/Generate-AppIcon.cs for repeatable generation and optional contrast previews. Application, tray, window, installer and consent-window consumers retain the same shared asset. Fullscreen and loading vector marks remain unchanged. Inspected 24/48/256px light/dark previews. Application build passed with zero warnings/errors. PrivateExtractIconsW extracted all seven native sizes from the built Winnow.exe; each had transparent corners. Captures are in C:/Temp/winnow-306-icons. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed the background square from the shared application and tray icon. Verified light/dark readability, transparent native frames embedded in Winnow.exe, and a clean application build.
<!-- SECTION:FINAL_SUMMARY:END -->
