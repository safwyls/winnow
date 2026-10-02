# Application update and recovery checkpoint — 2026-10-01

TASK-381.36 closes the six remaining frozen installation-policy and restart
contracts. The user authorized finishing TASK-381.36–381.41 as one sequential
batch, superseding the earlier pause after each individual task.

## Behavior

Electron now has a primary distribution route for the existing Windows Inno
installer and ZIP, and Ubuntu Debian package and tar archive. The Inno identity
and portable recovery engine are retained. An adjacent Electron release manifest
must match the app version and runtime; automatic replacement requires the
registered apphost or a supported portable manifest, apphost and helper.
Ubuntu 24.04 is the supported portable Linux distribution. System paths and
package-manager markers refuse archive replacement. Secondary NSIS/AppImage
builds obey the corresponding registration, distribution and managed-path checks.

Release selection has bounded, cancellable metadata reads and requires the newest
eligible release's canonical asset, size and SHA-256 digest. Real streamed bytes
are verified before staging, and rechecked before handoff. A repeated check keeps
only unchanged, reverified staging. Changed metadata revokes readiness. Cancel,
verification failure and failed helper preparation remove owned staging and keep
the application open for retry.

Primary Windows handoff verifies the actual parent and registered executable,
uses the existing installer script, and preserves only the selected library and
`--no-sync`. Portable handoff uses the existing checked database backup,
transaction journal, replacement and paired recovery commands. Its child process
does not inherit the command's protocol pipes, so confirmation can finish before
the frontend exits. Secondary restart context restoration also filters one-time
seed, sign-in, URI and unknown arguments.

A parent-bound helper holds the frontend's native installation lease before the
backend starts. Unsafe journal phases refuse startup; possible migration is
recorded before opening the backend. Readiness requires both a mounted renderer
and a healthy backend, including successful recovery after an initial connection
failure. The lease stays held while quit drains updater work. Normal exit, parent
death and closed input release it. Backend lease lookup recognizes only the
direct, `backend/`, and `resources/backend/` release layouts.

## Executed evidence

- The original six methods expand to twelve cases; all twelve pass against the
  matching frozen source assembly. Subsequent shared Windows policy extraction
  retains the original assertions in the current suite. Provenance, hashes,
  commands and TRX are in `.tmp/task38136-source-evidence.json`.
- The final focused Electron run passes **233 cases across ten files**, with no
  skips. These include the exact original fixtures, primary route selection,
  streamed bytes over an isolated socket adapter, retry/cancellation, helper
  dispatch, and both presentation modes. Report:
  `.tmp/task38136-updater-final-results.json`.
- The complete current update assembly passes **70 cases**, with no skips:
  24 helper cases, seven installation-boundary cases, and 39 recovery cases.
  Real subprocesses verify parent ownership/death, EOF, native leases, journal
  readiness, bounded frames and copied-helper handoff. Report:
  `.tmp/task38136-update-final-results/task38136-update-final.trx`.
- Backend build and Electron typecheck/build pass. The renderer is unchanged:
  `index-CEqesFQd.js`, with `index-Bz0s1M9P.css`.
- **Four native checks pass** in 35.7 seconds. Three cover desktop and fullscreen
  updates, cancellation, verification failure, retry, deliberate restart, focus
  and recovery notices; fullscreen repeats at 140% text. They substitute updater
  state and installer callbacks. The fourth uses the real Electron main adapter
  and .NET helper to verify Windows share-mode exclusion, explicit readiness,
  disposal, reacquisition and child/lock release. Report and per-case ledgers:
  `.tmp/task38136-native-results/results.json`.

Desktop and fullscreen screenshots were inspected. Recovery text is readable,
focus remains on an available action, and fullscreen root bumper and local input
hints remain visible. Captures are under `.tmp/task38136-native-results/`.

`migration-update-installation.json` records all six per-method assertion scopes.
The complete inventory now contains **1,688 ported, 706 retained-backend and 41
framework-specific methods**, with **zero pending or partial methods**. Both
migration audit commands pass for the frozen 2,435 methods in 299 files.

## Remaining delivery checks

This checkpoint does not install or uninstall software, mutate registration,
exercise a published release feed, or establish physical controller behavior.
The Windows installer/ZIP and Linux Debian/archive packaging and previous-version
upgrade checks belong to TASK-381.37 and TASK-381.38. Default release/CI entry points
remain a separate TASK-381.39 gate. Full combined regression gates and hardware
limitations are assessed in TASK-381.40 and TASK-381.41. No release was published.
