---
id: TASK-233
title: Copy bundled plugin package into integration test output
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 15:43'
updated_date: '2026-09-11 15:44'
labels: []
dependencies: []
type: bug
ordinal: 265000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Clean builds with separate project outputs cannot run the shipped SteamGridDB package integration test because the package is only copied to the application output.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The integration test discovers the actual built application plugin package without environment overrides or shared output directories.
- [x] #2 A clean Release test build and the bundled-package integration test pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Copy the bundled plugin files from the resolved application build output into the test output after build, then rerun the failing integration test with isolated artifacts.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The clean isolated Release suite reproduced the missing-package failure while 5341 other Windows tests passed and two Linux-only tests skipped. Added an after-build target that resolves Winnow.App output and copies its packaged plugins into Winnow.Tests output. Rebuilt and reran Shipped_steamgriddb_package_loads_through_the_public_sdk_without_an_app_reference with isolated artifacts and no package-root override: passed 1/1. No application behavior changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Made bundled-plugin integration testing independent of shared output folders by copying the actual built application packages. Verified the originally failing Release test passes with isolated artifacts.
<!-- SECTION:FINAL_SUMMARY:END -->
