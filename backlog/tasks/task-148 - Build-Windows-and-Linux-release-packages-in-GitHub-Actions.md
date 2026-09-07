---
id: TASK-148
title: Build Windows and Linux release packages in GitHub Actions
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 23:56'
updated_date: '2026-09-07 00:18'
labels: []
milestone: m-4
dependencies: []
priority: high
type: feature
ordinal: 175000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Produce installable Winnow beta artifacts for Windows and Linux from tagged source, with a manual build-only verification path and versioned GitHub Release assets.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Windows x64 self-contained build produces an installer and portable archive with upgrade/uninstall behavior that preserves user data.
- [x] #2 Linux x64 self-contained build produces a Debian package and portable archive with desktop integration and documented native dependencies.
- [x] #3 GitHub Actions validates versions, builds both platforms, smoke-checks packages, and creates a draft versioned release with checksums only after all required jobs pass.
- [x] #4 Manual workflow dispatch builds reviewable artifacts without publishing a release; release instructions and platform limits are documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add version-validated, self-contained x64 packaging for Windows (Inno Setup and ZIP) and Linux (Debian package and tar.gz). Add package installation/startup/removal smoke checks using temporary data. Wire tag-driven draft releases and build-only branch/manual runs, reuse CI verification before release creation, publish SHA-256 checksums, and document runtime requirements. Verify locally where possible and exercise both hosted package jobs without creating a tag or public release.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented version validation, self-contained publishers, Windows Inno installer/ZIP, Linux Debian/tar packaging, CI-only isolated smoke scripts, and a tag-to-draft release job gated on reusable CI and both packages. Local win-x64 and linux-x64 publishes succeeded. PowerShell parsing, Bash syntax/LF checks, version boundary tests, actionlint, and diff checks passed. Hosted installer verification is next; no release tag or public release is being created.

Hosted Release builds run 34069040888 at 863764f passed version validation and both win-x64/linux-x64 package jobs. Windows built Inno Setup and portable ZIP, then installed, launched with isolated data, reinstalled and uninstalled while preserving the database. Ubuntu built Debian/tar artifacts, validated manifests and desktop integration, installed dependencies, started under Xvfb with isolated data, then removed the package while preserving data. Actionlint and version tests passed; review verified tag-only draft publishing, scoped write permission, required CI/build gates and refusal to overwrite public releases. Untagged run skipped verification reuse and release creation as designed; normal CI runs independently. No actual tag-triggered draft was created, and workflow_dispatch awaits default-branch merge. Resolved Debian tilde expansion and trailing-newline version validation before the passing run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added self-contained Windows installer/ZIP and Linux Debian/tar builds, isolated installer smoke checks, build-only branch/manual runs, and gated tag-driven draft GitHub Releases with checksums. Both hosted package jobs passed: https://github.com/safwyls/winnow/actions/runs/34069040888. Local cross-platform publishes, script checks, version regressions and actionlint passed. Windows packages are unsigned; Linux platform limits are documented. Tag publication itself was not exercised and no release was created.
<!-- SECTION:FINAL_SUMMARY:END -->
