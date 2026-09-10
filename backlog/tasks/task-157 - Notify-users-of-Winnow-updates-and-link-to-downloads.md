---
id: TASK-157
title: Notify users of Winnow updates and link to downloads
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 04:59'
updated_date: '2026-09-10 00:46'
labels:
  - app-updates
dependencies: []
references:
  - src/Winnow.App/ViewModels/ApplicationSettingsViewModel.cs
documentation:
  - docs/releases.md
  - design-system.md
priority: high
type: feature
ordinal: 189000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Level 1 of in-app updating: users can discover a newer published Winnow release and open its release notes and appropriate download without leaving the app to search for it. Covers Windows and Linux installer and portable distributions. Installation remains user-managed at this level. This is the highest-priority update deliverable.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Application settings provide a manual update check and show the running version, available version, release notes link, and appropriate official download link when a newer release exists.
- [x] #2 Version comparison handles prereleases correctly; stable and beta channel behavior is explicit, drafts are excluded, and development or CI builds do not produce misleading upgrade prompts.
- [x] #3 Optional background checks respect a persisted preference, avoid repeated notifications for the same release, and do not block startup or library use.
- [x] #4 Offline, rate-limited, malformed, and unavailable-release responses are handled without crashing; manual checks distinguish failure from being up to date.
- [x] #5 Automated coverage verifies release selection, version ordering, platform download selection, and failure states; accessible UI behavior and release documentation are verified.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Implement shared GitHub release checks, version ordering, persisted automatic and beta preferences, background staging, and desktop/fullscreen settings. Test trusted asset selection, failures and UI.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented shared desktop/fullscreen updates with automatic checks/downloads on and beta off by default. GitHub API selects newer semantic versions, excludes drafts/dev/CI, verifies exact asset URL/size/digest, and exposes manual links. 31 updater service/client tests pass; 3 headless UI cases and 5 installer contract cases passed in initial solution run. Full solution build passes; full tests had one unrelated HideBrowsingPosition case fail and all six cases passed on isolated rerun. Windows/Linux native CI and disposable upgrade smoke remain to run.

Final local verification also passes in Release: 4367 tests, zero failures, two Linux-only skips. Debug has the same passing count. Release CI 34421921570 passes both packages and actual Windows upgrade/failure scenarios. General Windows CI remains pending at handoff.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added shared desktop/fullscreen release checks, automatic background downloads, stable default and optional beta channel, semantic version filtering, trusted package selection, manual links and soft failures. Verification: 32 updater client/service tests, 3 headless updater UI cases and 5 installer contract cases pass; full local solution suite passes 4367 tests with two Linux-only skips. Latest Linux native and package CI passes; Windows installer upgrade smoke passes. No forced restart or portable/Linux binary replacement.
<!-- SECTION:FINAL_SUMMARY:END -->
