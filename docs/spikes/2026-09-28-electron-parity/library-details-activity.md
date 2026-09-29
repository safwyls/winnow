# Library, details, activity and identity review checkpoint

This records the Electron migration package measured on 2026-09-28. It supplements
the shared [parity inventory](README.md), which remains incomplete.

## Implemented behavior

Desktop and fullscreen share the same revision-checked library actions. Manual lists
retain member ordering and restore the previous browse order on exit. Live lists load
their saved rules, expose named Update and Revert actions, preserve missing filter
values, and clear their contributed rules when the user leaves them. Add-to-list flows
serve library selections, individual feed cards and the open detail game. Pending
writes, conflicts and uncertain responses preserve drafts and require a saved-state
check before a potentially duplicate write.

Details retain independent editor drafts and section scroll positions. Back and Escape
return from an editor to the previous section. The unread shortcut uses the backend's
resolved-game count once, rather than summing its repeated ownership projections.
Relationship separation uses the child identity and expected link. Manual executable
browsing fills source-derived proposals while preserving user corrections; selecting
an IGDB candidate changes the draft until Save. As in the original application, a
manual executable path supplies installation and tracking evidence. It does not add
a direct executable-launch capability.

Activity provides date-grouped sessions, journal notes and ratings, filters, period
selection, a gameplay dashboard and separate Steam-reported history. Fullscreen starts
on the current week, with 50-row pages. Monthly timeline calculations retain unknown
gaps and counter resets, split tracked sessions across month boundaries, and distinguish
tracked time from approximate imported history.

Identity review now projects the original five proposal sections from the API workspace.
Users can choose each main game, exclude members, select groups across filters, merge a
batch, or accept only exact matches across stores. Confidence and explanations read the
stored matcher evidence. Expansion and variant parents remain fixed. Batch writes use
each returned revision; partial failure preserves the successfully saved acts for Undo.
Consecutive refusals accumulate in a seven-second undo dock. Group state survives
navigation and pending writes remain disabled across a remount. Same-title rows receive
distinct accessible names based on their displayed store, year, publisher and position.

## Measured checks

The earlier focused library/details/activity checkpoint passed 208 tests across 13
files. The identity follow-up passed 22 library tests, 19 grouped-review component tests
and 13 grouped-review model tests. Component tests exercise desktop and fullscreen;
injected DOM scroll dimensions and mocked API responses are identified as partial
evidence in the source-method mappings.

A fresh Electron build and all six `tests/electron/library-details.spec.ts` cases passed
in 24.1 seconds. The cases exercise both presentation modes against a real temporary
backend database, without mocked workspace responses:

- Search actual workspace work names, group editions, undo the returned act immediately,
  and search again after separation.
- Save a metadata year, retain an unfinished title draft, and observe the open live list
  lose the game after its year changes.
- Traverse review rows with the keyboard, merge two selected groups, verify both saved
  backend acts, undo both, and verify no proposal-card horizontal overflow.

Each native run creates a `winnow-electron-library-*` directory beneath `.tmp`, passes
`--data-dir`, and stops its own backend after the test process closes. Backend credentials
and browser profiles remain in ignored temporary data. The verified backend executable
was `.tmp/parity-full-tests/Debug/net10.0/Winnow.Backend.exe`. Fullscreen checks use the
production mode event in a fixed client window; they do not establish physical display
fullscreen or controller hardware behavior.

`tests/migration-library-details.json`, `tests/migration-library-workflows.json`,
`tests/migration-manual-flows.json`, `tests/migration-activity.json` and
`tests/migration-merges.json` record the exact original methods, replacement tests and
remaining differences. `npm run migration:report` validates those references against
the frozen inventory. A green focused suite does not establish full migration parity.

## Shared backend audit

`tests/migration-backend-audited.json` records 141 additional original methods whose
assertions exercise retained production backend behavior: 97 methods across 13 complete
classes and 44 selected methods from five mixed backend/frontend classes. Each mapping
names its inspected implementation files. Application services were checked against
`BackendServiceRegistration` and `BackendStartupService`; historical `Winnow.App`
namespaces alone were not evidence of either frontend or backend ownership.

The existing full-suite result
`.tmp/parity-final-results/safwyl_ZEDSIXNINETY_2026-09-28_22_17_11_net10.0.trx`
contains all 182 expanded cases for these methods, all passed, within 4,997 passing
`Winnow.Tests` cases. This evidence-only audit did not rerun or replace those tests.
`npm run migration:report` checks the fragment against the frozen source inventory and
verifies that each retained implementation exists. The additional permitted paths in
the audit script are restricted to six inspected files in diagnostics, storefront
enrichment, GOG registry reading and launch monitoring.

Mixed tests that also assert Avalonia library/details state, account-panel state or
launch-status strips remain pending. The ten `SteamSignInServiceTests` methods exercise
the legacy service through `LegacyConnectionAliases`, not the production API handshake.
The five `SteamSignInResultRedactionTests` methods exercise the C# result used by the
Avalonia API adapter; Electron has a separate TypeScript result. Neither class was
reclassified by this audit. Retaining backend evidence does not establish browser or
renderer parity for these excluded behaviors.

## Remaining differences in this package

The merge queue still needs the original cover-selection pipeline and dormancy styling,
compact idle facts, fullscreen member-overlay hierarchy, detailed controller shortcuts,
and the complete refresh/cancellation/performance matrix. Its snapshot refresh after
each answer also differs from the original no-read answer-path performance contract.
Several source cases have only component evidence for preferred-platform reload,
excluded-member persistence, expansion refusal, restored position and disclosure geometry.

Library collection sections are not yet independently collapsible. Original deep
controller options and Quick-menu focus restoration still need native evidence.
Details' original More-menu arrangement, pinned-header geometry and background refresh
matrix across updates, artwork, reception and journal drafts remain only partly covered.
These limitations stay explicit in the migration inventory.
