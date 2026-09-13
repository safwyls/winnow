CREATE TABLE release_year_evidence (
    release_id INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    source TEXT NOT NULL,
    source_id TEXT NOT NULL,
    year INTEGER NOT NULL CHECK (year BETWEEN 1 AND 9999),
    PRIMARY KEY (release_id, source, source_id)
);
