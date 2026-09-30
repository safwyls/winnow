# Metadata sync and fullscreen source settings

This checkpoint restores the original manual metadata operation and the separate fullscreen
IGDB and artwork-order pages. The contract list remains frozen at
`cf45d9f1127243a987d3cf6e664a32fc767ecb67`.

## Behavior and presentation

- Desktop, fullscreen and Operations share one metadata operation. Busy controls cannot
  start duplicate work; progress survives navigation and presentation changes. Completion
  retains the original guidance for missing credentials, partial updates and a failed
  library refresh. A lost response keeps the accepted operation ID for explicit retry.
- The backend ignores progress after a terminal state. Five HTTP regressions first failed
  against the previous implementation because callbacks replaced completed or failed
  messages; all pass after the running-state guard. Numeric metadata results survive the
  HTTP boundary, and failure responses do not reveal private exception details.
- Fullscreen Metadata & artwork opens separate IGDB metadata and Artwork source order
  reading pages. They retain source headings, rules, typography and one scrolling region.
  Back returns to the originating action. An IGDB loading failure focuses Retry; successful
  retry restores the form. Existing secret masking, save focus and draft removal remain.
- Artwork moves persist through the backend, refuse endpoints and retain focus on the
  moved source. A pending write that finishes after navigation cannot reclaim focus.
  Save failures retain the confirmed order and offer actionable guidance.

## Verification

- The first focused renderer run passed 119 cases and exposed lost focus after retrying a
  credential read. Giving the entry-focus hook a control identity fixes that transition.
  All 120 focused cases then pass: `.tmp/metadata-focused-final.log`.
- All 3,139 component/live API cases pass across 155 files without skips in 52.52s:
  `.tmp/metadata-integration.log`. Build/typecheck pass in
  `.tmp/metadata-final-build.log`; the inventory audit is `.tmp/metadata-migration.log`.
- A repeat alongside the full .NET build/test hit three timeouts (startup navigation,
  compiled dependency graph and a long merge workflow). The unchanged suite then passes
  all 3,139 cases in 51.65s when run alone: `.tmp/metadata-serial-integration.log`.
  `.tmp/metadata-final-integration.log` retains the failed run. Resource contention is
  consistent with the result; these checks do not establish its exact cause.
- All 138 backend HTTP cases pass, including the five new regressions:
  `.tmp/metadata-backend.log`. The original failing run remains at
  `.tmp/metadata-backend-before.log`.
- The first full .NET regression passed 6,891 cases, failed one fixture cleanup and skipped
  two Linux-only cases. `LibraryMultiSelectionTests` had finished its behavior assertions
  but deleted its temporary database while a connection was still open. Its isolated
  fixture now refuses new reads and drains tracked connections before deletion. Three
  controlled cases cover open connections, borrowed leases and transaction scopes. The
  complete Release repeat passes 6,895 cases across 13 assemblies, including all 899
  Avalonia UI cases, with only the two Linux skips:
  `.tmp/metadata-final-regression.log` and `.tmp/metadata-final-regression-results`.
- All 36 native regression cases pass together in 5.3 minutes:
  `.tmp/metadata-regression-native/results.json`. They cover the 11 new metadata workflows,
  the existing IGDB workflows, Details layout, plugin settings and update flags. All three
  failures from the preceding full run have passing follow-ups. Initial-readiness fixtures
  use the production 45-second attachment allowance; Details waits for both applied scale
  values before retaining its original geometry assertions.
- Screenshot review found doubled artwork separators, muted credential labels and centered
  action text. The final styles restore one-pixel separators, normal text color and the
  source's left alignment. All seven final native cases pass in 1.3 minutes, including
  1280×720 and 2560×1440 at 100% and 140% text, plus the existing IGDB secret workflows:
  `.tmp/metadata-final-native/results.json`. Final captures were inspected. Tests measure
  label color, button text inset, row borders, source font sizes, horizontal containment
  and control visibility after focus, as well as controller return and persisted order.

Six source methods now have complete evidence. Inventory: 1,130 ported, 559 retained
backend, 17 framework-specific, 628 pending and 101 partial out of 2,435 methods. The
complete 273-case native suite has not yet passed together. The broader migration acceptance criteria remain
unchecked; cover-tile action work is a separate package.
