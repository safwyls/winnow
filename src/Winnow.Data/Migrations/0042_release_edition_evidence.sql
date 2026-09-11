CREATE TABLE release_edition_evidence (
    id INTEGER PRIMARY KEY,
    release_id INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    work_id INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    provider TEXT NOT NULL,
    provider_id TEXT NOT NULL,
    edition_game_id INTEGER NOT NULL CHECK (edition_game_id > 0),
    version_parent_id INTEGER NOT NULL CHECK (version_parent_id > 0 AND version_parent_id <> edition_game_id),
    version_title TEXT NOT NULL,
    sources_json TEXT NOT NULL,
    valid_until TEXT NOT NULL,
    observed_at TEXT NOT NULL
);
CREATE INDEX ix_release_edition_evidence_release ON release_edition_evidence(release_id, provider, provider_id, id DESC);
