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
