-- 0020_expansion_refusals.sql — remembers that the user said no to an
-- expansion proposal.
-- Append-only: never edit this file once shipped; add 0021_*.sql instead.
--
-- One additive table. Nothing existing is rebuilt and nothing is dropped.
--
-- ── Why only the refusals are stored ──────────────────────────────────
--
-- The expansion PROPOSALS are not stored. They are derived on read from
-- the library's own titles, for exactly the reason §6.1 gives for
-- buckets: the detector's guards get tuned, and a stored proposal
-- computed under last month's rules rots in place. A refusal is
-- different in kind. It is a decision a person made about two specific
-- titles, and a decision cannot be re-derived. So decisions are stored
-- and questions are not.
--
-- The affirmative answer is not here either. Grouping writes an
-- identity_links row at kind = 'expansion_of' (migration 0018), and a
-- proposal is answered affirmatively if and only if such a link is live.
-- That is the same single-home rule 0019 applied to same-game answers:
-- one answer, one place to read it.
--
-- ── The shape ─────────────────────────────────────────────────────────
--
-- The pair is DIRECTIONAL. "Beyond the Sword extends Civilization IV" is
-- a different claim from "Civilization IV extends Beyond the Sword", and
-- refusing one says nothing about the other. The detector only ever
-- proposes the direction where the base's title is a prefix of the
-- child's, so in practice one row answers the question that was asked.
--
-- ON DELETE CASCADE on both columns, as identity_links has: a refusal
-- about a work that no longer exists is not a fact about anything.
--
-- Deleting a row re-opens the question on the next scan, because the
-- scan reads this table fresh every pass. No code path deletes one
-- today; it is stated here so a later "show me what I dismissed" screen
-- knows the table supports it.

CREATE TABLE expansion_refusals (
    id            INTEGER PRIMARY KEY,
    base_work_id  INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    child_work_id INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    refused_at    TEXT NOT NULL,
    note          TEXT,

    CHECK (base_work_id <> child_work_id)
);

-- One row per directional pair. The scan reads the whole table once per pass
-- and filters in memory, so this index is here to make the refusal write
-- idempotent (INSERT OR IGNORE) rather than to serve a query.
CREATE UNIQUE INDEX ux_expansion_refusals_pair
    ON expansion_refusals(base_work_id, child_work_id);

-- A work's own refusals, for the day a screen asks "what did I dismiss about
-- this game".
CREATE INDEX ix_expansion_refusals_child ON expansion_refusals(child_work_id);
