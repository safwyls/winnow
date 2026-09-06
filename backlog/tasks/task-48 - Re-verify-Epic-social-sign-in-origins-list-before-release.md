---
id: TASK-48
title: Re-verify Epic social sign-in origins list before release
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:55'
updated_date: '2026-09-06 22:41'
labels:
  - auth
  - security
milestone: m-4
dependencies: []
priority: medium
ordinal: 2400
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`EpicWebOptions.SocialSignInOrigins` is a snapshot of the identity providers Epic's login page offered at the time of implementation, not a contract. If Epic adds or removes a provider, the list may block a legitimate sign-in navigation or allow an unexpected one. The doc comment says "the list is a snapshot of Epic's login page rather than a contract." Must be re-verified against the live Epic login page before any public release. Source: `src/Winnow.Ingest.Epic/Web/EpicWebOptions.cs` (SocialSignInOrigins property); relates to F05's origin-binding hardening.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A live Epic sign-in session confirms that every provider on the login page has its origin in the list
- [x] #2 Any provider no longer present is flagged for removal
- [x] #3 The verified date is recorded in the code comment or a doc
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Verify the current implementation and required live evidence, apply the scoped correction, update governing documentation with superseded text retained in decisions, and close only acceptance criteria supported by objective checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The live English Epic login page displayed nine providers on 2026-09-06. Its loaded public configuration confirms their authorization origins. Added the missing Disney and LEGO origins and the current ecosec captcha origin as render-only destinations; no prior provider was absent. Tests verify navigation without trust or bridge access and reject suffix lookalikes. The provider popup did not open in the in-app browser, so no external account sign-ins were completed. Evidence and limits are recorded in docs/spikes/epic-signin-origins.md. EpicInteractiveSignInTests pass in Release.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Verified current login choices and destinations, added missing Disney/LEGO and captcha origins without granting bridge trust, and recorded the verification date. Origin-policy regressions pass.
<!-- SECTION:FINAL_SUMMARY:END -->
