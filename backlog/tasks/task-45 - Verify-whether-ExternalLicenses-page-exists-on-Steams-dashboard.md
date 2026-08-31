---
id: TASK-45
title: Verify whether ExternalLicenses page exists on Steam's dashboard
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
updated_date: '2026-08-29 21:56'
labels:
  - docs
  - ingest
milestone: m-4
dependencies: []
priority: low
ordinal: 45000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The design doc section 4.7 rests its third-party-key story on an `ExternalLicenses` file whose existence is unverified. No page by that name appears in the 2022 SteamTracking index, and probing the URL anonymously is non-discriminating (every `/accountdata/` path returns a login redirect). Requires a live authenticated session on `help.steampowered.com`. Source: docs/spikes/steam-gdpr-export.md section 2 and "What is still blocked" item 2.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A live authenticated session confirms or refutes the page's existence
- [ ] #2 If it exists, its columns and content shape are documented
- [ ] #3 The design doc's section 4.7 is updated to reflect findings
<!-- AC:END -->
