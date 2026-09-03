---
id: TASK-22
title: Add startup error boundary around async void initialization
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - infra
dependencies: []
priority: high
ordinal: 22000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The startup path contains `async void` initialization that can throw unobserved. An exception during initialization crashes without diagnostics. Finding F36. Source: stabilization-2026-08-28.md Group 2. Lands with F39 in the same startup pass.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 All `async void` initialization paths are wrapped in an error boundary
- [ ] #2 An exception during initialization is caught, logged, and surfaced to the user
- [ ] #3 The app does not crash silently on a startup fault
<!-- AC:END -->
