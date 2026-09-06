---
id: TASK-45
title: Verify whether ExternalLicenses page exists on Steam's dashboard
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:55'
updated_date: '2026-09-06 23:11'
labels:
  - docs
  - ingest
milestone: m-4
dependencies: []
priority: low
ordinal: 3300
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The design doc section 4.7 rests its third-party-key story on an `ExternalLicenses` file whose existence is unverified. No page by that name appears in the 2022 SteamTracking index, and probing the URL anonymously is non-discriminating (every `/accountdata/` path returns a login redirect). Requires a live authenticated session on `help.steampowered.com`. Source: docs/spikes/steam-gdpr-export.md section 2 and "What is still blocked" item 2.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A live authenticated session confirms or refutes the page's existence
- [x] #2 If it exists, its columns and content shape are documented
- [x] #3 The design doc's section 4.7 is updated to reflect findings
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Verify the current implementation and required live evidence, apply the scoped correction, update governing documentation with superseded text retained in decisions, and close only acceptance criteria supported by objective checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Authenticated browser verification on 2026-09-06: the dashboard has no ExternalLicenses link; opening that exact route renders the dashboard with the same 112 content links and no separate license table. Conditional column documentation is not applicable. The actual Licenses link opens store.steampowered.com/account/licenses with Date, Item and Acquisition Method columns and a Next paginator. Updated the spike and design section 4.7; no account HTML or credentials saved.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Verified that ExternalLicenses is not a distinct page for the authenticated account: it renders the dashboard. Documented the actual licenses route and the limit that support-request export contents remain unverified.
<!-- SECTION:FINAL_SUMMARY:END -->
