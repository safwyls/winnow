# Shared reading-link validation and Steam capture scripts

Reading destinations now use one policy in the renderer and main process. Game links,
their rendered controls and update-page targets reject unsafe destinations consistently.
Empty labels render no button; accepted targets use the URL parser's normalized output.
HTTP links remain available alongside HTTPS. Game launch/install commands retain their
existing backend ownership boundary and encoded launcher targets.

Every Steam capture-script invocation contains its own DOM failures and returns a failed
probe instead of throwing into the provider document. Account-page navigation requests
no-cache/no-store while following the page's own paginator in the original private session.
Capture preserves separate licence documents, each with its own table header and paginator,
instead of merging rows into Steam's live page.

## Verification

- Build and TypeScript check pass: `.tmp/link-harvest-build-final.log`.
- All 247 focused link, capture, shell and Details-fact cases pass:
  `.tmp/link-harvest-focused.log`.
- All 3,241 component/live API cases pass across 158 files without skips, in 50.27s:
  `.tmp/link-harvest-integration.log`.
- The first native group passed 20 cases, including all 15 Details cases, but failed two
  newly added cookie assertions: the protocol interceptor received no Cookie header.
  The report is retained at `.tmp/link-harvest-native-first/results.json`; log:
  `.tmp/link-harvest-native.log`.
- The corrected account fixture verifies the provider's actual Chromium session object
  and fixture cookie jar at each account request, separately from the no-cache/no-store
  request headers. It does not claim an on-wire Cookie observation or use a real account.
  All seven final native browser/account cases pass in 20.5s:
  `.tmp/link-harvest-native-final.log` and
  `.tmp/link-harvest-native-verified/results.json`.
- Both native surfaces preserve private provider pages, normalized pagination, final
  completion markers, reading-browser isolation and saved-page import. The fixture
  supplies all network responses; no Steam or other external account is contacted.
- The initial focused run passed 239 cases and failed six injected script-failure cases
  plus two new assertions with incorrect licence-cell counts. The script guard fixes the
  six behavior failures. Direct fixture inspection establishes 14 cells on the first
  licence page and three on the final page; those assertions now pass.

Eighteen original methods now have complete evidence. The five allowed protocol families
are checked across reading navigation and the existing backend action parity tests, rather
than granting arbitrary renderer links permission to launch games. The unchanged .NET
Release suite evidence remains the checkpoint-39 6,895 passing cases and two Linux-only
skips; this checkpoint changes no .NET source.

The inventory is 1,217 ported, 625 retained backend, 23 framework-specific, 469 pending and
101 partial methods out of 2,435. Full migration and the final combined native run remain
open. Saved link-destination composition is the next bounded audit.
