# Complete native regression and retained backend audit

The complete native Electron suite ran against the unchanged source and application
bundles at `147997eb`. The audit and next recommendation-card implementation were
prepared only under ignored `.tmp` paths during the run. This matters because several
native fixtures compile their renderer directly from source instead of loading `out`.

All **295 native tests pass**, with no skips, flaky outcomes or failures, in
29.71 minutes. Evidence: `.tmp/unread-complete-native.log` and
`.tmp/unread-complete-native/results.json`. The run covers both desktop and fullscreen,
including all earlier dashboard, rating, metadata, cover-control and unread-copy fixes.
Strict fixture shutdown is included; a killed or nonzero child would fail its test.

## Retained domain contracts

Sixty-five previously pending methods exercise implementations still composed by the
production backend. Each method was reviewed for its actual assertions, implementation
path and registration. The old `Winnow.App` namespace appears in several services that
now live in `Winnow.Application`; the namespace alone does not make a test frontend code.
The original tests remain in the .NET suite.

| Source class | Methods retained | Passing cases in the existing Release TRX |
|---|---:|---:|
| ArtworkBrowserServiceTests | 5 | 9 |
| EditionEvidenceTests | 8 | 20 |
| ExpansionLinkTests | 6 | 6 |
| IgdbIdLookupTests | 9 | 11 |
| LocalLibrarySyncContractTests | 4 | 4 |
| ReleaseYearEvidenceTests | 6 | 14 |
| SteamAccountPageProvenanceTests | 5 | 5 |
| SteamAccountPageTruncationTests | 7 | 7 |
| SteamBrowserArtworkSourceTests | 5 | 7 |
| SteamLibraryAssetLookupTests | 9 | 17 |
| UserSetNameTests | 1 | 1 |
| Total | 65 | 101 |

The audit checks `.tmp/metadata-final-regression-results` from checkpoint 39 and writes
the per-case match to `.tmp/backend-domain-trx.json`. All 101 matched cases passed.
`git diff --quiet c42471b5 HEAD -- '*.cs' '*.csproj' '*.sql'` confirms that the .NET
implementation and tests did not change between that verified checkpoint and this one.
The existing full Release result remains 6,895 passed, two non-Linux platform skips and
no failures. This checkpoint does not claim a newly executed .NET run.

The evidence covers durable artwork bytes and provenance, exact native edition IDs,
expansion scans and link transactions, IGDB ID lookup, local-only sync dependencies,
release-year evidence, account-scoped import/export, account HTML completeness, Steam
artwork renditions and cache behavior, and demo consolidation after a name correction.
The validator adds only the exact newly reviewed parser/client files to its allowlist.

Mixed frontend tests remain pending. Examples include displayed-cover projection through
the Avalonia wrapper, Library/Details view-model assertions after identity changes,
account-statistics view-model copy, and the old browser-capture identity and completeness
policies. Merely sharing a Core type does not prove that Electron uses the same policy.

The inventory is now 1,154 ported, 625 retained backend, 17 framework-specific, 538 pending
and 101 partial methods, out of 2,435. The migration gate still fails appropriately.
The full suite verifies the covered desktop and fullscreen paths on Windows; it does not
establish all remaining source contracts or physical controller/screen-reader validation.
