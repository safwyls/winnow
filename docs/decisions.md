# Documentation history

Current choices and the rationale needed to work on them live together in the domain
specifications: [architecture](../game-library-design.md), [visual design](../design-system.md),
[recommendations](recommendation-engine.md) and [product scope](../ROADMAP.md).

Git preserves previous wording, alternatives and reversals. There is no separate append-only
log to update when a specification changes. To investigate an earlier choice, use:

```powershell
git log --oneline -- game-library-design.md
git log -p -- game-library-design.md
```

Use the relevant file in those commands. Earlier versions of this file contain the collected
decision history. Dated reviews and `docs/spikes/` retain their measured evidence; their
findings describe the version inspected, not instructions for the current application.
