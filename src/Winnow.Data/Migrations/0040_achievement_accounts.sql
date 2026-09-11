-- Legacy unlocks retain unknown account provenance; never assign them to a current account.
CREATE TABLE account_achievement_unlocks (
    release_id INTEGER NOT NULL,
    provider_key TEXT NOT NULL,
    account_ref TEXT NOT NULL CHECK (length(account_ref) > 0),
    unlocked_at TEXT,
    PRIMARY KEY (release_id, provider_key, account_ref),
    FOREIGN KEY (release_id, provider_key) REFERENCES achievements(release_id, provider_key) ON DELETE CASCADE
);

CREATE TABLE achievement_observations (
    release_id INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    account_ref TEXT NOT NULL CHECK (length(account_ref) > 0),
    availability INTEGER NOT NULL CHECK (availability BETWEEN 0 AND 3),
    attempted_at TEXT NOT NULL,
    schema_at TEXT,
    progress_at TEXT,
    global_at TEXT,
    PRIMARY KEY (release_id, account_ref)
);
