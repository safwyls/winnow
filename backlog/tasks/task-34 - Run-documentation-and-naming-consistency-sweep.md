---
id: TASK-34
title: Run documentation and naming consistency sweep
status: Done
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-03 01:40'
labels:
  - docs
milestone: m-4
dependencies: []
priority: low
ordinal: 64000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
A sweep of all documentation and code-visible names to ensure consistency with CLAUDE.md's naming rules. The common noun "hoard" stays where the premise uses it; all product references use "Winnow." No finding ID; listed in stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All product references say "Winnow" except the four deliberate "hoard" sites listed in CLAUDE.md
- [x] #2 No stale "Hoard" references remain in user-visible strings, comments, or documentation
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Completed as part of the source-of-truth migration on 2026-09-02.

Verification is a test rather than a sweep, so the criteria cannot drift back:

- RepositoryHygieneTests.The_deliberate_uses_of_hoard_are_still_there quotes each of the four sites at the fragment that identifies it, so editing one away fails.
- RepositoryHygieneTests.Hoard_appears_nowhere_else_except_the_compatibility_shims scans every .cs, .axaml and .md in the tree and fails on any use outside those four sites and the legacy identifiers the three shims must keep: Hoard.Data, hoard.db, %LOCALAPPDATA%\Hoard, LegacyDefaultId.

Both pass against the full suite: 2,992 tests green. The scan found no stale product references; what it did find was the shim surface, which is now listed with the reason each entry is load-bearing.

Note that the four sites are now listed in AGENTS.md rather than CLAUDE.md, which is one line importing it.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Swept every tracked document and code-visible string for stale Hoard references and found none. The two criteria are now held by RepositoryHygieneTests rather than by a one-off sweep: one test asserts the four deliberate uses of the common noun are still present, the other fails on any fifth use outside the three compatibility shims. Verified by the full suite, 2,992 tests green, and by deliberately deleting a site and watching the first test fail.
<!-- SECTION:FINAL_SUMMARY:END -->
