---
id: TASK-340
title: Document IGDB setup from Twitch registration to verified metadata
status: Done
assignee:
  - '@codex'
created_date: '2026-09-18 00:17'
updated_date: '2026-09-18 00:21'
labels: []
dependencies: []
type: docs
ordinal: 382000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
IGDB credential setup is a difficult onboarding step. The existing short instructions leave users to navigate Twitch registration and distinguish saved credentials from working metadata.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Player docs explain Twitch registration fields, credential entry, first sync, and common recovery paths using official sources.
- [x] #2 Desktop and fullscreen instructions match shipped controls and distinguish saving from authentication and matching.
- [x] #3 README links to the tutorial and the website docs build and link checks pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify official Twitch/IGDB requirements and current app flows. 2. Expand the existing website metadata guide into linked tutorial sections and link it from README and first-run setup. 3. Build the docs, check links and copy, commit and push a PR.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified registration requirements against official Twitch register-app and IGDB account-creation docs. Checked desktop EnrichmentSettingsView, fullscreen IGDB/settings/match pages, shared credential and metadata-sync view models, and credential source precedence. Expanded existing website configuration guide into six linked, searchable IGDB sections; linked from README and first-run walkthrough. npm run build:pages passed with prerendered pages and local link/anchor/asset verification; tsc --noEmit and git diff --check passed. No app code changed and no real account registration or credential submission was performed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added an in-depth IGDB tutorial covering Twitch registration fields, Windows desktop/fullscreen credential entry, first metadata sync, manual matching, common failures, secret replacement/removal, and Linux environment setup. Verified official requirements and shipped UI labels; static website build, links, and TypeScript checks pass.
<!-- SECTION:FINAL_SUMMARY:END -->
