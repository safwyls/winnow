---
id: TASK-323
title: Publish first-party plugins and install them from the website
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 04:47'
updated_date: '2026-09-17 05:11'
labels: []
dependencies: []
ordinal: 365000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
First-party plugins currently require a local build and manual file copying. Players need a website catalogue with release-backed ZIP downloads and a browser handoff that installs official plugins into Winnow. Release CI must publish the same packages the website and app consume.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The website lists SteamGridDB, Xbox and PlayStation with release-backed ZIP downloads and Install in Winnow links, including loading, unavailable and failure states.
- [x] #2 A bounded winnow URI request reaches a new or existing application instance and installs only verified official release assets; malformed requests, unavailable assets and repeated requests recover safely.
- [x] #3 Windows and Linux packages register the URI handler; desktop and fullscreen show installation progress, results and access to plugin settings without an extra install confirmation.
- [x] #4 Release CI builds and validates all first-party plugin ZIPs and a versioned catalogue, includes them and checksums in Winnow releases, and preserves existing release gates.
- [x] #5 Focused release/catalogue, download-validation, activation and desktop/fullscreen tests pass; website builds and relevant documentation is updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Define a shared first-party catalogue/release asset and URI contract. 2. Build release plugin archives/catalogue and add OS protocol registration. 3. Implement verified installation plus single-instance handoff and shared desktop/fullscreen feedback. 4. Add the website catalogue and navigation using GitHub release data. 5. Verify packaging, bounded network/URI paths, UI behavior and website builds; document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented the website catalogue with a bounded public GitHub Releases fetch, exact official asset URLs/digests, stable-first selection and explicit pre-release labeling. Six release-selection/network tests, TypeScript and both Sites and GitHub Pages builds pass; Sites build wrapper hit a local npm launcher path error, so the unchanged npm build script was run directly and passed. Preview was queued in Codex; no browser interaction QA was requested. Built and verified all three actual plugin ZIPs and catalogue. App installer focused tests: 58 passed, existing plugin host tests: 89 passed. Read-only review identified retry/activation/shutdown races; owners are fixing and testing these before final solution verification. Protocol registration smoke checks are configured for disposable CI runners; no local registry installation or public release/deployment performed.

Resolved review findings: all requests, including Retry, share a queue; plugin downloads no longer hold taskbar game activation; installation links host shutdown and drains before disposal; catalogue shutdown serializes with installation and late initializers cannot publish after disposal. Final focused validation: 62 installer tests, 89 existing plugin-host tests, 49 activation/settings/setup/queue unit tests, and 29 desktop/fullscreen UI tests passed. UI captures inspected for progress/failure/success; setup handoff tested on both surfaces without marking setup complete. Full solution Release build passed with zero warnings/errors; full suite running from isolated scratch output.

Final verification: full solution Release build passed with 0 warnings and 0 errors. Full solution tests passed: 6,518 passed, 2 Linux-only tests skipped on Windows, 0 failed; includes 819 headless UI tests and 4,916 core/application tests. Website release tests (6), TypeScript, framework/Sites build and GitHub Pages static build/link checks passed. All three final plugin packages and catalogue built and validated; valid catalogue plus 20 mutation rejection cases passed. Read-only concurrency findings fixed and covered. Windows/Linux native association installation smoke checks remain for disposable CI runners, and real public downloads await a newly published release. No public deployment/release, real library mutation, or local protocol registration performed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added the official plugin catalogue with release-backed ZIP downloads and browser install links. Winnow validates and installs new first-party packages, presents shared desktop/fullscreen progress and setup, safely handles repeated requests and shutdown, and packages register the URI scheme. Release CI builds all three ZIPs, a verified catalogue and checksums alongside existing app assets. Verified a clean full Release build, 6,518 passing tests, site builds and package mutation checks; native installer smoke execution remains in CI.
<!-- SECTION:FINAL_SUMMARY:END -->
