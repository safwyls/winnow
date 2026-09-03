---
id: TASK-48
title: Re-verify Epic social sign-in origins list before release
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
updated_date: '2026-08-29 21:56'
labels:
  - auth
  - security
milestone: m-4
dependencies: []
priority: medium
ordinal: 57000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`EpicWebOptions.SocialSignInOrigins` is a snapshot of the identity providers Epic's login page offered at the time of implementation, not a contract. If Epic adds or removes a provider, the list may block a legitimate sign-in navigation or allow an unexpected one. The doc comment says "the list is a snapshot of Epic's login page rather than a contract." Must be re-verified against the live Epic login page before any public release. Source: `src/Winnow.Ingest.Epic/Web/EpicWebOptions.cs` (SocialSignInOrigins property); relates to F05's origin-binding hardening.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A live Epic sign-in session confirms that every provider on the login page has its origin in the list
- [ ] #2 Any provider no longer present is flagged for removal
- [ ] #3 The verified date is recorded in the code comment or a doc
<!-- AC:END -->
