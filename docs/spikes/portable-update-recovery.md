# Portable update recovery verification

Measured on Windows x64 on 2026-09-12 for TASK-159. Current behavior and recovery
commands are in [release instructions](../releases.md).

## Local checks

- Release solution build: zero warnings and errors, using
  `dotnet build --configuration Release --artifacts-path C:\Temp\winnow-task159-final`.
- Release recovery engine tests: 33 passed. Cases cover ZIP/tar rejection, digest and
  staged-file verification, internal/external data replacement cuts, committed WAL backup,
  explicit restore, missing/corrupt backups, simulated disk exhaustion, competing instances,
  abandoned staging, transaction ownership and interrupted restore retry.
- Release updater tests: 34 passed, including stage failure, retry and channel cancellation.
- Release presentation/installation tests: 31 passed across `ApplicationUpdaterUiTests`,
  `UpdateInstallationTests`, `WindowsUpdateInstallerTests`, `FullscreenQuickMenuTests` and
  `FullscreenSettingsTests`. Desktop keyboard and fullscreen controller actions use the
  same download-then-restart command. Failure prevents restart; recovery notices survive
  transient status changes. Desktop and fullscreen captures were visually inspected.
- Migration integrity: all 42 existing migration hashes verified; no migration was added.
- Portable baseline-selection fixture and PowerShell parsing passed. Linux packaging Bash
  syntax was checked through WSL.

The first full-suite attempt used a shared `BaseOutputPath`, which mixed dependency versions
and hit DLL locks. The repeat used per-project `--artifacts-path` outputs: 5,758 passed,
three failed and two Linux-only cases skipped. The activity paging failure passed a focused
rerun. Two failures remain outside the changed behavior: the existing accessible name on
`PluginSettingsView.axaml`'s TextBlock and a fullscreen platform test expecting a chevron
where the current platform row renders `Open`. The full suite is therefore not green.

## Real app/helper handshake

A temporary copy of the built app and helper used fabricated `1.0.0`/`1.0.1` release
manifests and an internal `--data-dir`. The debug app initialized sample data, and the
helper staged the ZIP while that app remained open. The test checked the transaction's
ready/proceed exchange and confirmed the journal stayed Staged until that exact app process
exited. Replacement, SQLite backup, restart and the real app's readiness acknowledgment
completed successfully. The test then stopped only the disposable application's process.

Evidence is in
`C:\Users\safwyl\AppData\Local\Temp\winnow-task159-handshake-4d3296076f0c435b8d061c3688c2ded6`.
This verifies the local integration with built binaries; it is not a published older-release
upgrade or an Ubuntu result. No production library or launcher files were modified.

## Release-runner evidence still required

`packaging/Test-PortableUpgrade.ps1` runs on disposable Windows and Ubuntu 24.04 release
runners. It fetches an earlier published archive with a verified digest, initializes and
seeds its temporary database, and tests external/internal data, failed apphost startup with
explicit paired restore, and interrupted replacement. The release workflow also runs the
engine suite on both platforms and retains journals, TRX and baseline-selection evidence.

Those release-runner checks were implemented but not executed in this local session.
The available WSL distribution is Fedora 44 without .NET, so it cannot establish Ubuntu
runtime or Unix permission coverage. Hardware power-loss behavior was not tested.

## Release-runner failures investigated on 2026-09-13

Release run `34740262012` built both packages, passed engine tests, and passed the
installed-package smoke checks. Ubuntu's portable upgrade failed at staging with
`Unsafe or duplicate archive entry`: the case-insensitive entry set treated the packaged
`Winnow` apphost and `winnow` launcher as the same file. The extractor now uses platform
case sensitivity. ZIP and tar regression cases cover case-distinct filenames and exact
duplicate rejection. All 37 engine tests pass on Windows and Ubuntu.

Both Windows release runs stalled in the portable upgrade smoke step. The helper was
invoked through a captured PowerShell pipeline; its persistent Winnow child inherits the
output handles. A local reproduction with a short-lived native launcher and a five-second
child took 5.50 seconds to return, despite the launcher exiting immediately. The smoke
script now waits for the helper process itself with a timeout rather than waiting for
pipeline EOF. This changes test orchestration, not application startup or recovery policy.

The next Ubuntu run exposed a second case-sensitivity issue: default PowerShell JSON
conversion rejected the journal's `Winnow` and `winnow` hash keys. Smoke journal reads now
use `ConvertFrom-Json -AsHashtable`. A local roundtrip preserved both keys and a changed
phase value.

At commit `382ec6c`, Ubuntu job `103680250659` in release run `34740857716` passed all
four portable scenarios from `v0.1.0-beta.6` to `0.1.0-ci.64`: external data, internal
data, failed startup with explicit paired restore, and interrupted replacement with
internal data. The engine suite passed 37 tests and the Debian/startup smoke also passed.

Windows in that run failed earlier in installed-update smoke: the helper's immediate
exclusive-open check found `Avalonia.Controls.dll` still locked after its parent process
exited. The other run failed on `Avalonia.Base.dll`. The installed helper and its smoke
script had not changed since the earlier passing runs; the logs do not identify the
remaining lock holder. The installed helper now retries sharing violations for at most
five seconds and checks cancellation during the wait. Windows PowerShell 5.1 regression
checks passed for writable files, a released lock, a persistent lock and cancellation.
Windows release verification remains pending a rerun. Desktop and fullscreen use the same update helpers;
no presentation behavior changed in this follow-up.
