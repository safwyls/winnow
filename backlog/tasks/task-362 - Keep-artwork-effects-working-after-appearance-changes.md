---
id: TASK-362
title: Explain reduced-motion overrides beside artwork controls
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 19:22'
updated_date: '2026-09-27 19:32'
labels: []
dependencies: []
type: bug
ordinal: 398000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user reported cards still lifting but no longer tilting or following the cursor after an accent change. Their active Afterglow profile has reducedMotion enabled, which intentionally produces this behavior. Make the effective motion override clear beside the artwork controls while preserving the accessibility preference.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Theme Studio explains when the profile or system reduced-motion preference pauses tilt and cursor-following light, on desktop and fullscreen.
- [x] #2 Unavailable motion controls reflect the override without discarding artwork choices; disabling profile reduced motion restores them when the system allows motion.
- [x] #3 Regression coverage verifies accent changes preserve artwork and motion preferences, and motion controls respect profile and system preferences.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Show the effective motion override beside artwork controls; keep settings intact and provide an explicit profile reduced-motion action; verify focused regression coverage and desktop/fullscreen fixture behavior; update theme documentation and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Confirmed the active LocalAppData Afterglow profile has reducedMotion=true with followPointer=true, floating=true, tilt=7. No user preferences changed. Isolated browser fixture continues to tilt after an accent change. Renderer warnings observed during investigation were not causal and are outside this correction.

Implemented a shared live system-motion hook for artwork and Theme Studio. Movement and depth now identifies the active reduced-motion override, disables pointer tracking and tilt while preserving saved values, and offers an explicit action to turn off profile reduced motion only when the system permits it. Floating artwork remains available as a stationary lift. No real user preferences were modified.

Verification: full Electron suite 191 passed, 8 opt-in backend tests skipped; focused motion suite 6 passed again after a test typing correction; npm run build passed with the two existing Zod annotation warnings. Safe browser fixture: desktop and fullscreen notices render correctly; turning off reduced motion restores enabled controls and pointer-follow with nonzero tilt on both surfaces. Notice wraps cleanly at 900x800 and the viewport override was reset. Accent edits preserve saved artwork and reduced-motion values in both modes. No .NET changes; packaged executable was not regenerated.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Explained the confirmed reduced-motion override beside artwork controls without changing the user preference. Shared system-motion observation keeps the notice and renderer consistent; regression tests and desktop/fullscreen fixture checks verify preference preservation and restored tilt. Electron tests and production build pass.
<!-- SECTION:FINAL_SUMMARY:END -->
