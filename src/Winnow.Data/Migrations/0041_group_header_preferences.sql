CREATE TABLE group_header_preferences (
    work_id INTEGER PRIMARY KEY REFERENCES works(id) ON DELETE CASCADE,
    preferred_store TEXT NULL,
    revision INTEGER NOT NULL
);
