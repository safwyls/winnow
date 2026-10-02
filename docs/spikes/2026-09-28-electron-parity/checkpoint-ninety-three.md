# Checkpoint 93 — Linux Electron distribution

TASK-381.38 preserves the Ubuntu 24.04 x64 Debian and portable release paths.
The primary Electron distribution contains its renderer, independent backend and
self-contained runtimes, update helper, bundled provider, product assets and notices.
Default build and release entry points remain a separate task.

## Qualification scope

The disposable Ubuntu job in `electron-packages.yml` builds and verifies the primary
package, then runs desktop and fullscreen under Xvfb and Openbox. The managed package
owns an AppArmor profile for `/opt/winnow/Winnow`. Portable qualification grants the
same permission to each exact stable executable path before upgrading it. CI enables
Ubuntu's restricted user-namespace policy; no test disables Chromium's sandbox.

An earlier published Debian package and portable archive are selected by release
version and verified against the official asset URL and GitHub digest. Five offline
Debian selection contracts pass, including refusal of untrusted metadata and mismatched
download bytes. Seven existing helper/evidence contract groups also pass after the
portable smoke integration change.

The shared frontend/backend lease policy now lets managed installations start without
creating an adjacent lock, and permits manual-update startup when the filesystem denies
creation and neither a lock nor journal exists. A busy existing lock is a startup error.
The focused Windows run passed **87 tests with no skips**, including 18 permission,
managed-installation, journal and lock-exclusion cases. Evidence is
`.tmp/task38138-leases.log` and `.tmp/task38138-test-results/task38138-leases.trx`.
The known copied-parent timing fixture remains assigned to TASK-381.41 and excluded from
this focused gate; this is not a full-suite result. The same permission cases must run
as a non-root user in Ubuntu CI.

The native probe passes syntax, formatting and full frontend type checks. It verifies
the Linux frontend and backend executable identities, effective renderer preferences,
kernel `NoNewPrivs`/seccomp state, active AppArmor profile, native window association,
fullscreen bounds, activation and owned-process shutdown. Managed runs refuse automatic
download/restart and create no portable helper or adjacent lock. CI versions refuse
release discovery before asset selection, so package-manager asset selection remains
covered by policy tests rather than claimed as an observed live release check.

The Linux packaging contracts have **17 passing offline cases on Windows**; three
explicit Linux-only cases cover the case-sensitive `Winnow`/`winnow` pair, Unix modes
and symlinks. All 20 run in Ubuntu. Shell and Python syntax checks pass. The publisher
normalizes `chrome-sandbox` to mode 0755 before generating hashes. The verifier rejects
privileged modes and checks each resolved system ELF library against the Debian
package's declared transitive dependencies, beyond checking `ldd` resolution.

The installation smoke covers the old package, upgrade, both Electron surfaces,
package-manager update behavior, library preservation and removal. Portable checks
cover external and internal data, failed startup with paired recovery, interrupted
replacement and the actual copied previous-release helper. Successful portable cases
run both presentation probes. The native/Proton session suite remains a separate check.

The [first Ubuntu run 36969598691](https://github.com/safwyls/winnow/actions/runs/36969598691)
at `136342b9175253b3dadbcee2a60a72c47b91babe` passed all **87 focused lease tests**
with no skips, the **691-file** primary publish, **16 package mutations**, **five Debian
baseline contracts** and **all 20 Linux package contracts**. It stopped before native
launch when the syntax-only AppArmor parser attempted to use its root-owned cache.
The preflight now specifies both `--skip-kernel-load` and `--skip-cache`; administrative
policy loading is unchanged. The first log is `.tmp/task38138-linux-first.log`.
The [second Ubuntu run 36970126301](https://github.com/safwyls/winnow/actions/runs/36970126301)
at `c49782af73a6df7de5e67c019715327257f2f795` passed that preflight and launched both
surfaces with the exact AppArmor profile. Reports captured the owned backend, library
read, native fullscreen bounds, renderer `NoNewPrivs=1` and `Seccomp=2`, and clean
shutdown. The probe failed because Chromium rewrote `/proc` command-line arguments
into a single title. The corrected parser retains paths with spaces and checks bounded
switches; 24 offline cases pass. Native kernel assertions remain unchanged. The desktop
entry now uses the observed lowercase `winnow` window class, with a regression check
for both frontend variants. There are now 21 Linux package contracts (18 pass on Windows,
three require Linux). Screenshots wait for startup presentation to finish after changing
mode. The companion Windows job passed native, installer and all five portable checks.
Logs and reports are in `.tmp/task38138-linux-second.log` and
`.tmp/task38138-linux-second-evidence`. Linux installation and recovery qualification
remain pending the corrected run.

The [third Ubuntu run 36971220965](https://github.com/safwyls/winnow/actions/runs/36971220965)
at `b677ec938a0a1b69437d583a85223502b8865159` passed both native probes, all 21 Linux
package contracts, all 24 process-parser cases and Debian/tar creation. Both captured
surfaces render completely. The actual previous Debian installation and upgrade succeed,
then dependency verification stops at `libcoreclrtraceptprovider.so` because its
`liblttng-ust.so.0` dependency is unavailable. The first portable upgrade reaches Ready
with no journal failure, then the smoke refuses cleanup because its recorded child
identity differs from the observed process. These checks remain failures until their
causes are resolved. Evidence is `.tmp/task38138-linux-third.log` and
`.tmp/task38138-linux-third-evidence`.

The dependency verifier now classifies only that exact missing soname in the two
runtime tracepoint providers as optional. The
[.NET 10.0.12 loader](https://github.com/dotnet/runtime/blob/v10.0.12/src/coreclr/pal/src/misc/tracepointprovider.cpp)
tolerates this load failure; Ubuntu 24.04 supplies LTTng ABI 1. Every other missing
dependency, command failure and undeclared resolved library still fails. The original
provider bytes and runtime diagnostic settings remain unchanged. Seven additional
contracts pass, bringing the offline Windows result to 25 passing cases and three
explicit Linux-only cases. OS-level LTTng tracing is recorded as unavailable; this
check does not measure EventPipe trace collection.

Portable cleanup now witnesses the replacement while its exact owned helper is still
its parent, then compares Linux kernel birth ticks, executable path and unchanged
child-record bytes before stopping it. Windows retains its existing exact timestamp
check. Compared identities are retained in the smoke evidence. Diagnostic collection
keeps product and installer logs and excludes Chromium profile storage.

The [fourth Ubuntu run 36972975741](https://github.com/safwyls/winnow/actions/runs/36972975741)
at `075c61c9eaa76e60834c7bca0646d4710808e41e` passed all 28 package contracts and the
actual Debian install, upgrade, both native presentations, library-preserving removal
and purge. Dependency verification passed for all 692 managed files and reported only
the two optional LTTng providers. Portable frontend cleanup passed with the exact kernel
identity. Its retained comparison confirms a 4,268-tick difference between the helper's
and PowerShell's UTC timestamps for the same process. The following backend shutdown
request failed with connection refused after the frontend process-tree termination;
dead-process handling must distinguish that case from a live backend refusing shutdown.
Evidence is `.tmp/task38138-linux-fourth.log` and `.tmp/task38138-linux-fourth-evidence`.
Linux backend shutdown now checks exact kernel birth and state. A missing or terminated
process needs no HTTP request; a refused request succeeds only if the same process is
observed to terminate within the existing shutdown bound. A live backend, reused PID or
unreadable procfs remains a failure. The 55 focused shutdown contracts and all 33 portable
identity contracts pass locally; actual Linux procfs checks run in the next Ubuntu job.

## Optional macOS audit

The secondary Electron builder configuration declares a DMG target and its backend
publisher recognizes macOS host runtimes. The primary publisher and release matrix
exclude macOS. No macOS CI, signing, notarization, install or physical-device evidence
exists. Browser and generic Unix monitoring code is not platform qualification;
credential persistence has no Keychain implementation and updating only opens a
release page. macOS support expansion is outside this migration's release scope.

Virtual X11 evidence does not establish Wayland, a physical Linux compositor, controller
hardware, or TV-distance readability. Those distinctions belong to the device-validation
task. No local installation, merge or release publication is part of this checkpoint.
