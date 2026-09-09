CREATE TABLE lifecycle_observations (
    id INTEGER PRIMARY KEY,
    release_id INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    source TEXT NOT NULL,
    source_id TEXT,
    observed_at TEXT NOT NULL,
    signals_json TEXT NOT NULL,
    raw_json TEXT
);

CREATE INDEX ix_lifecycle_observations_release_source
    ON lifecycle_observations(release_id, source, observed_at DESC, id DESC);
