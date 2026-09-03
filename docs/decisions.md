# Decisions

Why things in Winnow are the way they are, what was reversed, and what a document used to
say before it was corrected.

## How this file works

**Append-only.** Entries are added at the end of the relevant section and never edited or
deleted. If a decision is itself reversed, the reversal is a new entry that names the one it
reverses; the old entry stays where it is.

**Nothing here binds.** Every rule an agent must obey lives in a domain document named by
`AGENTS.md`. This file holds the reasoning that was removed from those documents so they
could state rules without arguing for them, and the text of claims that were deleted because
they had become false. No document says "see `decisions.md`" for a rule, and no agent is
instructed to read it.

**It exists so that deleting rationale from a spec is not deleting it from the repository.**
A human asking "why is this like this" reads this file. An agent asking "what must I do"
does not.

**Entry shape.** A dated heading, the rule or document it concerns, and the reasoning. Where
an entry records a correction, it quotes the superseded text verbatim so the change is
legible without a `git log`.

---

## Naming and the 2026-08-28 rename

### 2026-08-28 — The product was renamed from Hoard to Winnow

The product, the assembly, the binary and the mascot were called Hoard until this date. The
rename covers anything hyphenated or possessive: `Winnow-launched`, `Winnow's own`,
`Winnow-owned`, "a Winnow theme".

### 2026-08-28 — Why "hoard" survives as a common noun

The premise of the app is *winnowing a hoard*, the dragon's pile of a thousand unplayed
games, so the common noun is load-bearing rather than a leftover. Four places use it
deliberately and a search-and-replace over them is a regression. The rule is in `AGENTS.md`;
this entry is the reason for it.

### 2026-08-28 — Why the DbUp journal has to be re-pointed

`DatabaseInitializer.RenameLegacyJournalEntries` re-points DbUp's `SchemaVersions` rows from
`Hoard.Data.Migrations.*` to `Winnow.Data.Migrations.*`. DbUp keys applied scripts by
embedded-resource name, and that name carries the root namespace. Without the re-point every
shipped migration replays against a populated database and `0001` dies on `table works
already exists` before the window opens.

### 2026-08-28 — Why the data-directory move falls back rather than failing

`WinnowDataLocation` moves `%LOCALAPPDATA%\Hoard` to `%LOCALAPPDATA%\Winnow` once. If the
move cannot be completed it reads the legacy directory in place instead. An install that
predates the rename holds the user's only copy of their library, so a half-completed move
that leaves the app pointing at an empty new directory is worse than not moving at all.

---

## Working practice

### 2026-08-31 — Why every clickable run needs `--data-dir`

Clicks in the running app write to the real library. This has already happened once during
development. The rule that a run you might click in passes `-- --data-dir <path>` exists
because of that incident, not as a precaution.

`Environment.GetFolderPath` uses the Windows shell API, so setting `%LOCALAPPDATA%` in the
environment does not redirect anything. The override had to be a command-line flag.

---

## Product scope

## Architecture

## The visual system

## Corrections made during the source-of-truth migration

### 2026-09-02 — `CLAUDE.md` and `AGENTS.md` were two copies of one file

They had drifted. `AGENTS.md` said domain agents live in `.Codex/agents/` where `CLAUDE.md`
said `.claude/agents/`, and the directory on disk was `.codex/agents/`, lower case.
`AGENTS.md` also omitted the `--data-dir` paragraph, the exit-code-2 sentence and the
`%LOCALAPPDATA%` finding, so an agent reading only `AGENTS.md` would click on the real
library.

`CLAUDE.md`'s text was taken wherever the two diverged. `AGENTS.md` is now the single file
and `CLAUDE.md` is one line, `@AGENTS.md`, so Claude Code loads it.

### 2026-09-02 — `.codex/agents/` deleted

Seven `.toml` charters duplicated the seven `.claude/agents/*.md` charters with six
divergences, all defects: no delegation block, no model pins, `docs-writer.toml` untracked in
git and pointing at `AGENTS.md` while claiming a "Codex-opus-4-6" model, and
`avalonia-ui.toml` stored with literal `\r` escapes. Codex is not in active use on this
project, so the tree was deleted rather than generated from `.claude/agents/`.

### 2026-09-02 — Documents no longer state precedence over each other

`CLAUDE.md`, `AGENTS.md`, `README.md` and `ROADMAP.md` each carried a different ordering of
the same documents, conditional in places: the roadmap "supersedes the design doc's §8
milestones and amends its §1 non-goals", the spikes "OVERRIDE spec guesses", `README`'s
table listed six documents "in precedence order". Reconciling that chain per task was
producing inconsistent answers.

The chain is replaced by one document per domain, listed in `AGENTS.md`. Where a document
was wrong it has been edited to the current truth and the sentence it used to say appended
here, rather than corrected in place by a later document.

### 2026-09-02 — What `AGENTS.md` used to say about the visual system

The authority section carried visual values directly: "Flare (#FF5C8A) marks ONLY unread
updates; all numbers render in IBM Plex Mono `tnum`. Root `tokens.axaml` is the design
RECORD; the compiling copy is `src/Winnow.App/Themes/tokens.axaml` — change tokens there.
Fonts are static OFL cuts (Avalonia 11 has no variable-axis API)."

The hex was wrong: the shipping `winnow` theme seeds `#FF4D93`. Visual values now live only
in `design-system.md` and `tokens.axaml`, which is why `AGENTS.md` states none of them.

### 2026-09-02 — The second `tokens.axaml` deleted

A copy at the repository root was described as "the design RECORD", with
`src/Winnow.App/Themes/tokens.axaml` as "the compiling copy". The two had diverged: the root
file held 93 keys, the compiling copy 121. Every one of the 93 was present in the compiling
copy with an identical value, so the root file held nothing the app had not adopted; it was
simply 28 keys out of date, all of them the scrollbar and text-style entries.

The root copy is deleted. `src/Winnow.App/Themes/tokens.axaml` is the only token file.
