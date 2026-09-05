-- 0024_work_maturity.sql — stored maturity evidence: age ratings and content
-- descriptors from each source that has reported them for a work.
-- Append-only: never edit this file once shipped; add 0025_*.sql instead.
--
-- ── One row per (work, source) ─────────────────────────────────────────
--
-- IGDB and the Steam store each keep their own reading, keyed by the
-- composite (work_id, source). A re-read from one source replaces its
-- own row and never clobbers the other's. Both ratings and descriptors
-- are comma-joined token lists, stored VERBATIM — the same treatment
-- works.epic_categories gets.
--
-- ── No stored verdict ──────────────────────────────────────────────────
--
-- Whether a work is explicit is decided at read time by
-- MaturityRules.IsExplicit in C#, exactly as NonGameEntries.IsNonGame
-- decides non-game-ness over rows the bucket query returns. That is
-- what keeps §6.1's rule ("derived things stay queries") true here: the
-- 18+ vocabulary will be retuned as more rating boards and more
-- storefront descriptors are met, and a stored boolean would rot
-- silently while the evidence that produced it is still on disk. A work
-- with no row is never explicit — absence of data is not a rating, and
-- hiding a game because nobody has looked it up yet is the failure mode
-- to avoid.
--
-- ── No CHECK constraint on source ──────────────────────────────────────
--
-- Migration 0021 had to rebuild identity_links to widen a CHECK, and
-- the source vocabulary is expected to grow as more storefronts and
-- rating aggregators are supported. The vocabulary lives in
-- MaturitySources (Winnow.Core.Queries.Maturity), and the enrichment
-- clients that populate this table must produce exactly those tokens.
--
-- ── The token vocabulary ───────────────────────────────────────────────
--
-- Explicit when any rating token is in the 18+/adults-only tier
-- (esrb:ao, pegi:18, usk:18, cero:z, acb:r18, acb:x18, classind:18,
-- grac:18) OR any descriptor token is adult_only_sexual_content. ESRB M
-- and PEGI 16 are NOT explicit — this setting is an 18+ gate, not a
-- maturity gate. nudity_or_sexual_content, general_mature_content and
-- violence_or_gore are carried as evidence and are not explicit on their
-- own. Unknown tokens are stored and ignored: the stored row is the
-- evidence, and evidence the rule cannot read today is evidence the rule
-- can read after a retune.

CREATE TABLE work_maturity (
    work_id      INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    source       TEXT NOT NULL,
    ratings      TEXT,
    descriptors  TEXT,
    observed_at  TEXT NOT NULL,
    PRIMARY KEY (work_id, source)
);
