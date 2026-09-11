---
id: TASK-224
title: Run packaging verification for bundled plugin-only changes
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:07'
labels:
  - architecture
  - review
dependencies: []
references:
  - '.github/workflows/release.yml:7'
  - 'src/Winnow.App/Winnow.App.csproj:117'
  - plugins/Winnow.Plugin.SteamGridDb/plugin.json
documentation:
  - docs/architecture-review-2026-09-10.md
priority: low
type: chore
ordinal: 255000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R36. Evidence: Source verified. The application build/publish targets copy the SteamGridDb assembly and plugin.json from plugins/. The release workflow's push and pull-request path filters include src/ and packaging/ but omit plugins/. A plugin-only code/manifest change therefore does not trigger the normal packaging workflow. Ordinary CI can pass while the package layout or plugin manifest change is not exercised until a tag/manual release. Tagged releases still run their required gates; this is a pull-request coverage gap.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Bundled plugin code and manifest changes trigger the appropriate package build/verification on pull requests.
- [ ] #2 Packaged Windows/Linux artifacts contain a discoverable matching plugin assembly and manifest, with a regression check for the supported bundled layout.
- [ ] #3 The change preserves tag release gates and avoids unnecessary duplicate CI runs.
<!-- AC:END -->
