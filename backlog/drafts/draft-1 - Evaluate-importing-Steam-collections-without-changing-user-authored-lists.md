---
id: DRAFT-1
title: Evaluate importing Steam collections without changing user-authored lists
status: Draft
assignee: []
created_date: '2026-09-11 08:03'
labels:
  - deferred
  - steam
  - ingest
dependencies: []
references:
  - src/Winnow.Ingest.Steam/SteamLibrarySource.cs
documentation:
  - ROADMAP.md
  - game-library-design.md
priority: low
type: feature
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Steam collections import is not implemented. The former build-spec source table and TASK92 description implied it existed, but SteamLibrarySource reads libraryfolders, appmanifest and localconfig only and no collection parser or import contract exists. This is a deferred post-beta feature decision, outside the architecture repair milestone; user-authored Winnow lists remain available. Evaluate whether reading per-account Steam collections adds enough value to schedule, including static versus dynamic collections and how imported membership coexists with local edits.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The product decision explicitly schedules or rejects Steam collections import and records the supported static/dynamic collection and account scope.
- [ ] #2 If scheduled, the contract preserves user-authored Winnow lists, defines repeated import and source disappearance behavior, and never writes Steam files.
- [ ] #3 Any scheduled user-facing implementation covers desktop and fullscreen and uses sanitized fixtures for multiple accounts, malformed and unavailable collection files.
<!-- AC:END -->
