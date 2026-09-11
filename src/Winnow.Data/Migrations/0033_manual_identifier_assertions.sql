-- User-entered identifiers are retractable assertions, not anonymous store facts.
-- Existing identifiers are deliberately not backfilled: their origin is unknown.
CREATE TABLE manual_entry_identifiers (
    id              INTEGER PRIMARY KEY,
    ownership_id    INTEGER NOT NULL REFERENCES manual_entries(ownership_id) ON DELETE CASCADE,
    provider        TEXT NOT NULL,
    provider_id     TEXT,
    owns_mapping    INTEGER NOT NULL CHECK (owns_mapping IN (0, 1)),
    asserted_at     TEXT NOT NULL,
    retracted_at    TEXT
);

CREATE UNIQUE INDEX ux_manual_entry_identifiers_live
    ON manual_entry_identifiers(ownership_id, provider) WHERE retracted_at IS NULL;

CREATE INDEX ix_manual_entry_identifiers_mapping
    ON manual_entry_identifiers(provider, provider_id) WHERE retracted_at IS NULL;

-- Changes in user mapping intent advance this revision, including selecting the
-- same id again, so asynchronous writers can distinguish two visits to one id.
ALTER TABLE works ADD COLUMN igdb_mapping_revision INTEGER NOT NULL DEFAULT 0;
