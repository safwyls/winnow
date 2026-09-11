---
id: TASK-46
title: Evaluate whether Steam support exports add usable library history
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
updated_date: '2026-09-11 13:58'
labels:
  - docs
dependencies: []
documentation:
  - docs/spikes/steam-gdpr-export.md
  - game-library-design.md
priority: low
type: spike
ordinal: 96000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Determine whether a user-supplied or explicitly authorized Steam support response provides useful data beyond Winnow's existing account-page import and API history backfill. The August 2026 study verified the dashboard and request form, but did not establish downloadable files, turnaround or contents. This is optional research, not a prerequisite for current imports or a general export importer.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Use an existing response or an explicitly user-authorized support request; record request/response dates and distinguish observed turnaround from a service guarantee.
- [ ] #2 If files are available, document the container, data fields, account provenance and history coverage without retaining personal data in the repository.
- [ ] #3 Compare available data with current account-page and API sources and record whether any additional integration is justified; an unavailable response remains unknown.
- [ ] #4 Record dated evidence in docs/spikes/steam-gdpr-export.md and update current scope only if the finding supports it.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: docs/spikes/steam-gdpr-export.md section 7 and Evidence limits still identify the support response as unknown. Current dashboard imports and backfill are specified in game-library-design.md sections 4.7 and 5.4. No support request was submitted during this audit.
<!-- SECTION:NOTES:END -->
