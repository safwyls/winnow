# Platforms and manual entry parity

The September 29 package restores a Platforms section with separate Steam, Epic and GOG
cards. Steam opens first, and expired Epic or failing Steam renewal still marks its tab
while another card is selected. GOG describes local ingestion and offers no sign-in action.
IGDB and artwork source order live in Metadata & artwork.

Platform figures count each grouped title once per store before screen filters and search.
They use the shared Library expansion projection: a folded pack does not contribute its
store or minutes to its base game. Missing figures are absent. The Steam account summary
combines those title counts with the backend's confirmed account count.

Steam credential guidance, account scope, purchase import and sign-in consent share one
active layer. Opening consent does not sign in and leaves purchase capture unticked. The
purchase dialog explains the browser and saved-file routes at the same level; saved files
remain available without an API key, session or embedded browser. Pending sign-in blocks
the purchase action and credential changes until it finishes.

Manual entry behavior now lives in ManualGames and ManualEditor, with LibraryTools retaining
the exported editor. The form keeps the existing picker, executable facts, draft recovery
and uncertain-create safeguards. Local title, year and identifier validation puts an
accessible message beside the failing field before a request is sent. Structured backend
identifier conflicts use the same placement. A changed IGDB mapping requires Cancel and
reopen; an unrelated revision conflict still offers an explicit draft rebase. Removal names
the game, begins on Keep game, and writes only after confirmation. Form entry and cancellation
restore keyboard/controller focus to their originating control.

Verification:

- The focused Electron run passed 301 cases across 11 files, including all 24 Steam
  transition cases. Transition fixtures now provide real empty library/workspace shapes;
  pending-route assertions verify the closed purchase action rather than assuming its file
  input remains mounted behind another dialog.
- Six ManualGameParityTests cases passed through the production HTTP API with isolated
  temporary data. They verify creation with executable-derived installation state, editing,
  deletion and restart persistence; fresh identifier corrections; legacy/store/mapping
  refusals without partial writes; and an existing Steam ID collision.
- Four native Electron checks passed on desktop and fullscreen with isolated data. The two
  manual/Platforms cases exercise keyboard Enter and simulated controller Accept/Back,
  exact trigger focus restoration, the four original correction/refusal scenarios, confirmed
  deletion and refreshed store figures against the real backend. Both existing Steam import
  cases also passed through the new purchase dialog. Desktop required a fixture correction:
  wait for the confirmation to close on success before asserting deletion, because its modal
  state hides background rows from accessibility queries while the request is pending.
- The migration audit accepts the new source-method evidence, including the original manual
  form scenario matrix. Native screenshots were inspected for desktop Platforms and a
  fullscreen mapping refusal. Controller input uses a simulated navigator.getGamepads source;
  physical controller hardware was not part of this run.

The renderer rejects IGDB IDs above JavaScript's exact integer range instead of silently
rounding them. The backend's original signed 64-bit identifier range is wider. That boundary
still needs a lossless API representation before claiming complete numeric-range parity.
