---
id: TASK-152.3
title: Construct hidden panes lazily and cut the styling object count
status: Done
assignee:
  - '@claude'
created_date: '2026-09-07 22:21'
updated_date: '2026-09-08 00:27'
labels: []
dependencies: []
references:
  - src/Winnow.App/Views/MainWindow.axaml
  - src/Winnow.App/Views/MainWindow.axaml.cs
  - src/Winnow.App/Themes/controls.axaml
documentation:
  - docs/spikes/memory-footprint.md
parent_task_id: TASK-152
priority: medium
ordinal: 183000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
MainWindow.axaml instantiates every pane at startup (feed, grid, list, merge queue, account stats, stores, library settings, appearance, filter panel, the 1,864-line details view with its nested metadata editor and journal, and the lightbox) and toggles them with IsVisible. The 2026-09-07 heap dump of the real library shows the cost: 40,473 StyleInstance, 44,898 StyleClassActivator, 26,734 DynamicResourceExpression and 40,764 property-store entry arrays, roughly 20 MB of the 59 MB managed heap, plus the composition visuals behind them. controls.axaml uses DynamicResource for every token by design (see its header), which multiplies the per-control cost. The outcome is that a pane the user has not opened costs nothing and the open panes cost less. MainWindow.axaml.cs addresses DetailsPanel, LightboxPanel, FilterPanel and AppearancePanel by name and the UI tests depend on them, so a lazy container has to preserve those seams.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Details, lightbox, merge queue, account stats, stores, library settings and appearance are not in the visual tree until first shown
- [x] #2 tests/Winnow.Ui.Tests pass unchanged, or with test edits that are explained in the summary
- [ ] #3 Managed heap after startup on the real library drops by at least 10 MB and the StyleInstance count by at least a third, shown with dotnet-gcdump or dotnet-dump dumpheap -stat before and after
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Take a before heap dump (StyleInstance, DynamicResourceExpression, ValueStore counts, GC allocated). 2. Materialize details, lightbox, merge queue, account stats, stores, library settings and appearance on first show behind named lazy containers so code-behind and UI-test seams survive. 3. Where a template file allows it, replace DynamicResource with direct token references in per-item templates. 4. Run both test projects; take the after dump; record in the spike. Delegated to an Opus avalonia-ui subagent in worktree C:\Temp\wt\152-3 (branch mem/152-3).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Subagent (Opus, avalonia-ui) finished on branch mem/152-3, commit bc863b4. New Views/LazyPane.cs (Decorator with PaneTemplate, Pane accessor, Materialized event, IsVisible default false); eight panes in MainWindow.axaml now lazy (detail modal, lightbox, merge queue, stats, platforms, library settings, appearance, filter panel) with the named seams kept on the containers; MainWindow.axaml.cs wires CloseRequested/Closed on Materialized; ScreenshotLightboxView arms its focus trap on attach; LibrarySettingsView applies render scaling on DataContext change because a DataTemplate-built control attaches before its DataContext binding resolves. Measured (real library copy, --no-sync, 30 s, two runs): live objects 49.8-50.4 MB/612k -> 41.3 MB/499k; GC allocated 54.5-54.7 -> 44.0-45.8 MB; StyleInstance 40,473 -> 30,336 (-25%); DynamicResourceExpression 26,734 -> 21,065; private bytes 259-286 -> 222-224 MB. Tests: build 0 warnings; 3,952 tests pass; one enforcement test (ScreenshotLightboxStructureTests) re-pointed at the LazyPane container; three new LazyPaneTests. AC 3 partly met: -9 MB live heap (just under 10) and StyleInstance -25% (not a third) because the remaining styling cost is the feed's ~29 realized cards, not hidden panes. Merge note: the branch rewrites MainWindow.axaml, so the concurrent TASK-153 APPLICATION settings pane must be re-added as a LazyPane at integration.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Views/LazyPane.cs and converted eight panes in MainWindow.axaml (detail modal, lightbox, merge queue, stats, platforms, library settings, appearance, filter panel; the TASK-153 application-settings pane was converted the same way at integration) so they are built on first show; named seams kept on the containers. Verified by heap dump on the real library copy: no pane view instantiated at startup, live objects 612k -> 499k, GC allocated 54.5 -> 44-46 MB, StyleInstance 40,473 -> 30,336, private bytes 259-286 -> 222-224 MB; three new LazyPaneTests, one enforcement test re-pointed at the container, full suite passes. AC 3 left unchecked: managed heap fell 9 MB (target 10) and StyleInstance 25% (target a third); the remainder is the feed's ~29 realized cards, not hidden panes.
<!-- SECTION:FINAL_SUMMARY:END -->
