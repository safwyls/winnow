---
id: TASK-170
title: Deploy the promo site to GitHub Pages
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-09 04:40'
updated_date: '2026-09-09 04:44'
labels: []
dependencies: []
ordinal: 202000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Build and deploy the promotional site as static pages under the repository GitHub Pages path.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Static output includes both pages with repository-prefixed assets and links
- [ ] #2 Actions validates changes and deploys main to Pages
- [x] #3 Deployment and verification are documented
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect export support; add Pages build and workflow; verify output and configure Pages.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Pages enabled through the GitHub API with workflow publishing and HTTPS. Local static builds passed for /winnow and domain root; both routes rendered and HTML/CSS asset checks passed. Workflow verification pending on the pull request.
<!-- SECTION:NOTES:END -->
