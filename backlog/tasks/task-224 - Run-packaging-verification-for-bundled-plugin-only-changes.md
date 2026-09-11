---
id: TASK-224
title: Run packaging verification for bundled plugin-only changes
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 06:24'
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
- [x] #1 Bundled plugin code and manifest changes trigger the appropriate package build/verification on pull requests.
- [x] #2 Packaged Windows/Linux artifacts contain a discoverable matching plugin assembly and manifest, with a regression check for the supported bundled layout.
- [x] #3 The change preserves tag release gates and avoids unnecessary duplicate CI runs.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Include bundled plugin inputs in release workflow path filters. 2. Validate each bundled manifest and assembly in published artifacts before packaging using a shared packaging check. 3. Exercise that check on Windows/Linux publish outputs and failure fixtures, preserve tag gates, and update release documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified isolated self-contained win-x64 and linux-x64 publishes, both with bundled manifest/assembly/type checks and all malformed-output rejection scenarios. Windows verification disabled ReadyToRun only for this local packaging check; production CI retains it. Verification also exposed and fixed a real RID mismatch in CopyBundledPlugins: GetTargetPath now queries the same RID-neutral library target the SDK built. Workflow triggers now include plugins/**.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Plugin-only changes trigger release validation. Publishing checks the shipped manifest, assembly identity and declared entry type; negative checks run on both CI OS targets. Corrected the self-contained publish path so the bundled managed plugin is actually copied. Local Windows/Linux publish outputs verified; no installer was run on this workstation.
<!-- SECTION:FINAL_SUMMARY:END -->
