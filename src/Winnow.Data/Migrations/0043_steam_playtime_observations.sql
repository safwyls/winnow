CREATE TABLE steam_playtime_observations (
    id INTEGER PRIMARY KEY,
    ownership_id INTEGER NOT NULL REFERENCES ownerships(id) ON DELETE CASCADE,
    account_ref TEXT NOT NULL CHECK (length(trim(account_ref)) > 0),
    source TEXT NOT NULL CHECK (length(trim(source)) > 0),
    playtime_minutes INTEGER NULL CHECK (playtime_minutes >= 0),
    last_played_at TEXT NULL,
    observed_at TEXT NOT NULL
);

CREATE UNIQUE INDEX ux_steam_playtime_observation
ON steam_playtime_observations (
    ownership_id, account_ref, source, observed_at,
    IFNULL(playtime_minutes, -1), IFNULL(last_played_at, '')
);

CREATE INDEX ix_steam_playtime_observations_account
ON steam_playtime_observations (ownership_id, account_ref, observed_at, id);
