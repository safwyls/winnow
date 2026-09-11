# Architecture review correction checks

This records verification of the corrections to the September architecture review.
Backlog TASK-227 owns delivery status; the original review remains the finding record.
All checks below use temporary databases or isolated build/publish directories. No
launcher files, real library data, accounts or installed application were modified.

## Foundation checkpoint

The first checkpoint covers identity undo, atomic metadata writes, manual corrections,
credential-scoped Steam/Epic caches, incomplete Epic scans, coherent Galaxy snapshots,
update acknowledgement, schema refusal, migration checksums, recommendation evidence and
cold-start shelves, startup failure handling, shared prompts/year/note validation and
bundled-plugin packaging. TASK-192, TASK-193 and TASK-195 still require their explicit
Stores/action presentation checks; their backend regression coverage is recorded below.

### Integrated checks

Windows, .NET SDK 10.0.400 / runtime 10.0.11, Release configuration:

| Assembly | Passed | Skipped |
|---|---:|---:|
| Winnow.Tests | 4,102 | 0 |
| Winnow.Ui.Tests | 340 | 0 |
| Winnow.Recommend.Tests | 166 | 0 |
| Winnow.Covers.Tests | 153 | 0 |
| Winnow.Plugins.Tests | 35 | 0 |
| Winnow.Plugin.SteamGridDb.Tests | 42 | 0 |
| Winnow.Monitor.Linux.Tests | 0 | 2 |

The complete pass initially found three stale expectations: two announcement-count
assertions and legacy-brand prose rejected by repository naming enforcement. After the
corrections, all 46 LibraryViewModel tests and all eight naming-enforcement tests passed.
The table combines that focused recheck with the other results from the complete pass;
it is not a claim that the first run was clean. No product test failure remains at this
checkpoint. The solution build finished with zero warnings and zero errors.

The shared scratch output made `dotnet test` at solution scope build one assembly while
another testhost held the same dependency DLL. Building first and testing the affected
assembly with `--no-build` avoided that verification-only collision. Future full checks
should build first and then use `dotnet test --no-build` with the same output property.

Migration verification against review commit `a21753b` passed all 33 hashes. The checksum
mutation checks passed when consolidating the manifest; only migration 0033 was appended.
`git diff --check` passed.

### Focused evidence

- Identity, atomic metadata, manual corrections and read inventory: 217 focused tests;
  26 related headless cases, including eight new desktop/fullscreen correction cases.
- Steam credential/history/account seams: 391 tests. Epic caching and scan completeness:
  269 tests, including account changes and partial/failed filesystem reads.
- Galaxy snapshots: 50 tests using native SQLite writers and WAL rollover. A writer
  denied by the held read guard could not interleave a mixed copy; a later snapshot
  included the committed update. Non-Windows live-copy refusal was inspected in source,
  not executed on a Linux host.
- Update acknowledgement: 123 focused repository/model cases, seven actual production
  composition cases and the final 46-test library regression recheck.
- Recommendations: all 166 engine cases plus four production desktop/fullscreen cases
  proving cold shelf rendering, installed-sibling action choice and original-release
  impression/verdict/undo. Plugin candidate and feedback regressions also passed.
- Prompt parity: eight new headless cases and 58 existing list/feed regressions. Year
  parsing and shared journal editors have dedicated desktop/fullscreen tests; the
  integrated 340-test UI pass includes them.
- Startup: seven real child processes selected desktop/fullscreen via the persisted
  setting and used only temporary data. Bad configuration and unsupported schemas exit
  with code 3; an unusable explicit data directory exits with code 2. Reporter tests
  cover secret scrubbing and a logger that itself fails.
- Packaging: isolated self-contained Windows and Linux publishes both contained a
  matching SteamGridDB manifest, assembly and declared entry type. The verifier rejected
  a missing DLL, wrong assembly, altered manifest and nonexistent entry type. Local
  Windows verification disabled ReadyToRun; release CI retains its normal setting.

TRX outputs live under each test project's ignored `TestResults` directory. The complete
pass uses the `foundation` prefix; focused commands and outcomes also appear in the
corresponding Backlog tasks. No installer, live external API or physical gamepad/TV check
is inferred from these results. Linux process checks remain a separate required gate.
