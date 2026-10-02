# Checkpoint 94 — primary Electron entry points and CI

TASK-381.39 makes Electron the default source build, run and package target while
retaining the Avalonia reference frontend and all original regression tests.

## Entry points

`Build.ps1` builds the independent backend and update helper, then typechecks and builds
Electron. `Run.ps1` selects those companions for development or compiled preview.
Unpackaged development retains its explicit data-directory requirement; installed
application launches keep normal library discovery. `Publish.ps1` forwards its existing
arguments to the verified Electron publisher. The previous publisher remains available
as `Publish-Avalonia.ps1`.

Frontend `npm run package` produces the verified primary directory. `npm run dist` adds
the existing Inno/ZIP or Debian/tar packagers. Both build locally without publishing.
The former Electron builder routes have explicit secondary names. Source package and
lockfile versions match `Version.props` at `0.2.0-dev`, with a build-time drift check;
requested release versions and commits remain independent overrides.

The actual root build passed using `.tmp/task38139-default-build`. A fresh Debug test
companion build passed using `.tmp/task38139-ci-fixtures`. The primary routing checks
passed 16 Node and 17 PowerShell cases; 33 affected component tests passed. Five
bundled-provider checks passed against the previously verified primary directory,
including negative cases using its backend assembly location. That last result is
metadata inspection of an older package, not a new package launch. The subsequent actual
`npm run package -- --output ../../.tmp/task38139-default-package` produced and verified
a fresh 697-file Windows distribution. Its desktop and fullscreen package probes both
passed, including independent backend health, renderer readiness, activation, update
leases and clean close. All five bundled-provider positive/negative checks passed on
that directory. This local build includes the working changes below; the manifest records
the preceding commit, so final-tree CI remains the release evidence.

## Required validation

The protected Windows check now aggregates the retained .NET suite, fresh Electron
unit/live API/migration tests, native desktop/fullscreen tests, both platform package
jobs and first-party plugin packages. The protected Linux session check retains its
name. The existing 24-hour reusable evidence policy applies only to .NET tests; its
53 policy contracts pass. Electron and packaging always run fresh.

Native tests use eight isolated Windows runners, one worker per runner, no retries and
whole-file scheduling. Fifteen specifications no longer depend on old local build paths
or rebuild their own .NET fixtures. A shared script builds the Debug backend, fixture
and helper once on each runner. A migrated Steam action test passed on both desktop
and fullscreen against these fresh companions.

Actual collection found 1,011 tests in 111 files, distributed as 129, 125, 139, 127, 118,
131, 148 and 94 tests. The shard union contains every test once and splits no files.
This is collection evidence, not a passing full native run. Accounting compares full
inventory, planned shard and executed results, rejecting skipped, missing, repeated,
failed or interrupted bodies and mismatched source/runtime provenance. Its contracts
and the workflow graph checks plus diagnostic-retention contracts passed 48 cases.
Raw reports are accounted before sanitization. Retained evidence excludes source diffs,
credential fields and values, profile/database/archive trees and links; it keeps approved
fake-fixture screenshots, JSON and error context. Failed runs retain diagnostics without
producing a passing receipt. The full regression gate remains
TASK-381.41.

Release tags and build-only dispatch call the same CI workflow with the requested
version, then consume its validated package and plugin artifacts. Feature branches
use the PR CI event; duplicate legacy package runs are removed. No release was created.

## Terminal compatibility

The primary composition now preserves `--epic-login`, separate/equal `--code` forms,
raw standard input, EOF cancellation and selected-library routing. The backend executable
handles this command before host construction; Electron delegates before GUI/profile
startup. Linux launchers route directly to the backend without a display. The existing
renderer callback/state validation remains unchanged.

The command prints consent and the URL before offering to open a browser, keeps that
URL usable when opening fails, preserves safe provider-specific guidance and cancels
the attempt on every exit path. It attaches to or starts the independent backend and
leaves that backend running. Startup handles a competing owner publishing discovery
later. On Windows, backend creation temporarily clears inheritance on the command's
terminal handles so a cold child cannot keep the caller's output pipes open after the
command exits; original handle flags are restored.

Simultaneous cold starts also exposed a Windows sharing violation on the common data-root
write probe. The resolver retries only that exact transient error, eight times at 25 ms;
ACL failures, directory blockers and persistent locks still refuse the selected root.
All 29 data-root and legacy-location contracts passed, including deterministic release
and persistent-lock cases. All 27 backend terminal/provider contracts passed, including
cold and simultaneous startup and bounded output-pipe completion.

Four real Electron terminal tests passed against a real isolated backend and fake Epic
provider: both code forms, stdin and EOF, redirected handles, no GUI or Electron profile,
and a healthy independent backend afterward. The Linux launcher matrix tests the actual
generated shell bodies with fake child commands, absent display variables, literal
arguments/input and exit codes 0/1/2/3. This is not live provider authentication or a
physical terminal-device test.

Local evidence includes `.tmp/task38139-default-build.log`,
`.tmp/task38139-ci-fixtures.log`, `.tmp/task38139-entrypoints-evidence.json`,
`.tmp/task38139-ci-policy.log`, `.tmp/task38139-accounting-tests.log`,
`.tmp/task38139-native-inventory.json`, `.tmp/task38139-shard-plan/`,
`.tmp/task38139-prebuilt-native.log`, `.tmp/task38139-terminal-native-final.log`,
`.tmp/task38139-terminal-dotnet-final.log`, `.tmp/task38139-terminal-data-roots.log`,
`.tmp/task38139-ci-contracts-final.log`, `.tmp/task38139-default-package.log`,
`.tmp/task38139-default-package-native.log` and `.tmp/task38139-default-package-plugins.log`.
