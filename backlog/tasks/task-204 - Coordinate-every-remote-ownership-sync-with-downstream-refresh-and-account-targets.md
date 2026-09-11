---
id: TASK-204
title: >-
  Coordinate every remote ownership sync with downstream refresh and account
  targets
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/Services/RemoteOwnershipSchedulerService.cs:118'
  - 'src/Winnow.App/Program.cs:305'
  - 'src/Winnow.App/Program.cs:517'
  - 'src/Winnow.App/Services/LibrarySyncService.cs:489'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 235000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R16. Evidence: Source verified. The six-hour RemoteOwnershipSchedulerService resolves ownership and logs completion but does not run the library reload/enrichment chain used at startup. New acquisitions can stay invisible until an unrelated reload and incompletely enriched until another trigger. Steam targets are also drawn from local candidate account refs, so a known signed-in account with no local candidates may receive no remote ownership fetch. Background ownership sync does not fulfill its end-to-end application contract. The copied startup pipeline and scheduler have drifted.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Startup, scheduled and explicit ownership sync use one application coordinator that schedules dependent metadata work and publishes committed library changes.
- [ ] #2 Authorized known account identities remain eligible sync targets even without local installations; account changes and cancellation are handled conservatively.
- [ ] #3 Tests add a remote acquisition while the app remains open and cover remote-only Steam startup, partial failures and both presentation surfaces without calling UI from ingest modules.
<!-- AC:END -->
