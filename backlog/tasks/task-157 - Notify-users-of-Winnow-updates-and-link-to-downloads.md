---
id: TASK-157
title: Notify users of Winnow updates and link to downloads
status: To Do
assignee: []
created_date: '2026-09-08 04:59'
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
- [ ] #1 Application settings provide a manual update check and show the running version, available version, release notes link, and appropriate official download link when a newer release exists.
- [ ] #2 Version comparison handles prereleases correctly; stable and beta channel behavior is explicit, drafts are excluded, and development or CI builds do not produce misleading upgrade prompts.
- [ ] #3 Optional background checks respect a persisted preference, avoid repeated notifications for the same release, and do not block startup or library use.
- [ ] #4 Offline, rate-limited, malformed, and unavailable-release responses are handled without crashing; manual checks distinguish failure from being up to date.
- [ ] #5 Automated coverage verifies release selection, version ordering, platform download selection, and failure states; accessible UI behavior and release documentation are verified.
<!-- AC:END -->
