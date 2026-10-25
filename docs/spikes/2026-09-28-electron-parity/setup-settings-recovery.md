# Setup and settings recovery checks

The September 29 package makes Setup use the same Steam and Epic account cards as Settings.
It fixes the setup sign-out route names, exposes reconnect for an expired Epic session, and
shares busy-state navigation guards and Steam key removal. Setup omits purchase import when
its host supplies no importer.

A failed Run setup again write opens local recovery while retaining stored completion.
Snapshot refresh does not dismiss that recovery. Continue or Skip setup clears recovery only
after a successful progress write. Application preferences now add missing cache rows after
saving, so defaults change visibly on their first edit. Invalid saved library sort values
display Dormant longest without rewriting the value merely by opening Settings.

The App waits for the initial setup read before delivering provider installation. Once
loaded, an installation handoff suspends Setup without completing it, shows explicit install
review, and resumes the same cursor when review closes. Setup restores the invoking control
after ordinary completion or Skip setup.

Component tests exercise both desktop and fullscreen. `SetupParityTests` adds five real
backend HTTP cases for new, existing and sample startup, saved cursor restart, and completion
and replay without resetting preferences. The two original tests for sample suppression of
an unfinished cursor and corrupt-cursor recovery continue to exercise the unchanged
production `FirstRunSetupService`; both passed.

The final focused Electron run passed 179 cases across eight files. A fresh production build
and `tests/electron/setup-parity.spec.ts` verified two native presentations in 19.9 seconds,
using the isolated Debug backend. All nine steps retained visible navigation and Next focus
at 1000×640 desktop and 1280×720 fullscreen. Tests covered Tab containment, simulated gamepad
Back/Menu, masked fullscreen keyboard typing, provider handoff with cursor preservation,
and the complete Settings replay/focus-return path. The native run found and fixed loss of
the invoking control when a pending replay disabled its button. Gamepad hardware state was
simulated; no physical-controller claim is made. Screenshots are emitted for each step.

The four Steam result redaction replacements call the actual native sign-in coordinator with
a captured logging callback. They check all JWT segments, refresh tokens, account identity,
provider detail and captured HTML are absent while useful metadata survives. A separate sink
test checks bounded file rotation. No test signs into a live provider account.

Remaining source contracts stay incomplete: native tray creation/removal evidence, the
original fixed credential-action layout at short desktop height, pixel-level rounded-corner
clipping, platform tabs/counts/GOG presentation, and manual-entry library updates and
field-specific validation. The original desktop gamepad keyboard route also remains open;
the native keyboard check here covers fullscreen. Passing component tests does not establish
those behaviors.
