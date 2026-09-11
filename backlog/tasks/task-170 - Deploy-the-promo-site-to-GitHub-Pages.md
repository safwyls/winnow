---
id: TASK-170
title: Deploy the promo site to GitHub Pages
status: Done
assignee:
  - '@codex'
created_date: '2026-09-09 04:40'
updated_date: '2026-09-11 13:59'
labels: []
dependencies: []
references:
  - 'https://github.com/safwyls/winnow/actions/runs/34381767597'
  - 'https://github.com/safwyls/winnow/actions/runs/34382853044'
  - 'https://winnow.gg/'
documentation:
  - website/README.md
ordinal: 202000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Publish the player page, developer page and architecture artifact at https://winnow.gg/ using GitHub Actions Pages publishing. Pull requests validate static output; successful main builds deploy with custom-domain root links and assets. Repository-path builds remain an optional configuration, not the production target.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Static output serves the player and developer routes and architecture artifact with domain-root assets and links; optional repository-prefix builds remain documented.
- [x] #2 The Promo site workflow validates pull requests and successfully deploys main to GitHub Pages.
- [x] #3 Deployment configuration, local build and verification are documented in website/README.md.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Verify current custom-domain configuration, successful pull-request and main workflow jobs, public routes and deployment instructions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified September 11: GitHub Pages API reports workflow publishing, cname winnow.gg and enforced HTTPS. Pull-request run 34381767597 passed the static build; main run 34382853044 passed build and deploy. HTTP GET returned 200 for /, /developers/ and /architecture-diagram.html with the expected page titles. These verify the deployed September 9 commit, not unpublished branch changes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Deployment is complete: custom-domain Pages configuration, successful PR/main CI and all three live routes verified. Corrected production scope from a repository prefix to winnow.gg.
<!-- SECTION:FINAL_SUMMARY:END -->
