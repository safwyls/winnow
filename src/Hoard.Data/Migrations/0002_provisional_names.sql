-- 0002_provisional_names.sql — flag placeholder work names.
-- Append-only: never edit this file once shipped; add 0003_*.sql instead.
--
-- Steam ingest emits candidates for the union of installed appmanifests and
-- played-but-uninstalled appids from localconfig.vdf. The latter have no local
-- title source, but works.name is NOT NULL, so the resolver mints a placeholder
-- ("App 1203620"). This flag is how the M1 enrichment pass tells a placeholder
-- from a real title — without it, enrichment cannot distinguish "never named"
-- from "named, and the user may have edited it".
--
-- DEFAULT 0: every pre-existing work was created from a real source title.

ALTER TABLE works ADD COLUMN name_is_provisional INTEGER NOT NULL DEFAULT 0
    CHECK (name_is_provisional IN (0, 1));

-- Enrichment sweeps "which works still need a real name?" — a partial index
-- keeps that cheap and stays tiny once the backlog is worked off.
CREATE INDEX ix_works_name_is_provisional
    ON works(name_is_provisional) WHERE name_is_provisional = 1;
