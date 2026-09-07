---
id: TASK-148
title: Build Windows and Linux release packages in GitHub Actions
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-06 23:56'
updated_date: '2026-09-07 00:08'
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
- [ ] #1 Windows x64 self-contained build produces an installer and portable archive with upgrade/uninstall behavior that preserves user data.
- [ ] #2 Linux x64 self-contained build produces a Debian package and portable archive with desktop integration and documented native dependencies.
- [ ] #3 GitHub Actions validates versions, builds both platforms, smoke-checks packages, and creates a draft versioned release with checksums only after all required jobs pass.
- [ ] #4 Manual workflow dispatch builds reviewable artifacts without publishing a release; release instructions and platform limits are documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add version-validated, self-contained x64 packaging for Windows (Inno Setup and ZIP) and Linux (Debian package and tar.gz). Add package installation/startup/removal smoke checks using temporary data. Wire tag-driven draft releases and build-only branch/manual runs, reuse CI verification before release creation, publish SHA-256 checksums, and document runtime requirements. Verify locally where possible and exercise both hosted package jobs without creating a tag or public release.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented version validation, self-contained publishers, Windows Inno installer/ZIP, Linux Debian/tar packaging, CI-only isolated smoke scripts, and a tag-to-draft release job gated on reusable CI and both packages. Local win-x64 and linux-x64 publishes succeeded. PowerShell parsing, Bash syntax/LF checks, version boundary tests, actionlint, and diff checks passed. Hosted installer verification is next; no release tag or public release is being created.
<!-- SECTION:NOTES:END -->
