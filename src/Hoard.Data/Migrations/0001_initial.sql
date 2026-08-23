-- 0001_initial.sql — full §6 schema.
-- Append-only: never edit this file once shipped; add 0002_*.sql instead.
-- Timestamps are TEXT, UTC, 'YYYY-MM-DD HH:MM:SS' (sorts lexicographically,
-- compatible with SQLite datetime() modifiers).

-- ── Canonical identity ────────────────────────────────────────────────────

CREATE TABLE works (
    id                  INTEGER PRIMARY KEY,
    igdb_id             INTEGER UNIQUE,
    name                TEXT NOT NULL,
    sort_name           TEXT,
    first_release_year  INTEGER,
    summary             TEXT,
    cover_url           TEXT
);

CREATE TABLE releases (
    id               INTEGER PRIMARY KEY,
    work_id          INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    igdb_version_id  INTEGER,
    name             TEXT NOT NULL,
    platform         TEXT,
    edition_note     TEXT
);

CREATE INDEX ix_releases_work_id ON releases(work_id);

CREATE TABLE external_ids (
    release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    provider     TEXT NOT NULL CHECK (provider IN ('steam', 'gog', 'epic', 'igdb')),
    provider_id  TEXT NOT NULL,
    PRIMARY KEY (provider, provider_id)
);

CREATE INDEX ix_external_ids_release_id ON external_ids(release_id);

-- ── Ownership and play ────────────────────────────────────────────────────

CREATE TABLE ownerships (
    id                INTEGER PRIMARY KEY,
    release_id        INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    store             TEXT NOT NULL,
    account_ref       TEXT,
    acquired_at       TEXT,
    license_type      TEXT,
    price_paid_cents  INTEGER CHECK (price_paid_cents IS NULL OR price_paid_cents >= 0),
    price_source      TEXT,
    install_path      TEXT,
    installed         INTEGER NOT NULL DEFAULT 0 CHECK (installed IN (0, 1))
);

CREATE INDEX ix_ownerships_release_id ON ownerships(release_id);

CREATE TABLE play_records (
    id                INTEGER PRIMARY KEY,
    ownership_id      INTEGER NOT NULL REFERENCES ownerships(id) ON DELETE CASCADE,
    playtime_minutes  INTEGER NOT NULL DEFAULT 0 CHECK (playtime_minutes >= 0),
    last_played_at    TEXT,
    source            TEXT NOT NULL,
    observed_at       TEXT NOT NULL
);

CREATE INDEX ix_play_records_ownership_observed ON play_records(ownership_id, observed_at);

-- Longitudinal history the storefronts discard.
CREATE TABLE playtime_snapshots (
    id                INTEGER PRIMARY KEY,
    ownership_id      INTEGER NOT NULL REFERENCES ownerships(id) ON DELETE CASCADE,
    playtime_minutes  INTEGER NOT NULL CHECK (playtime_minutes >= 0),
    observed_at       TEXT NOT NULL
);

CREATE INDEX ix_playtime_snapshots_ownership_observed ON playtime_snapshots(ownership_id, observed_at);

CREATE TABLE sessions (
    id                INTEGER PRIMARY KEY,
    ownership_id      INTEGER NOT NULL REFERENCES ownerships(id) ON DELETE CASCADE,
    started_at        TEXT NOT NULL,
    ended_at          TEXT,
    duration_s        INTEGER CHECK (duration_s IS NULL OR duration_s >= 0),
    detection_method  TEXT NOT NULL CHECK (detection_method IN ('process_watch', 'wrapper', 'import', 'manual'))
);

CREATE INDEX ix_sessions_ownership_started ON sessions(ownership_id, started_at);

CREATE TABLE session_notes (
    session_id  INTEGER PRIMARY KEY REFERENCES sessions(id) ON DELETE CASCADE,
    note        TEXT,
    rating      INTEGER CHECK (rating IS NULL OR rating BETWEEN 1 AND 5)
);

-- ── Achievements: per-release, never merged across platforms (§6.2) ───────

CREATE TABLE achievements (
    release_id    INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    provider_key  TEXT NOT NULL,
    name          TEXT NOT NULL,
    description   TEXT,
    hidden        INTEGER NOT NULL DEFAULT 0 CHECK (hidden IN (0, 1)),
    global_pct    REAL CHECK (global_pct IS NULL OR (global_pct >= 0.0 AND global_pct <= 100.0)),
    PRIMARY KEY (release_id, provider_key)
);

CREATE TABLE achievement_unlocks (
    release_id    INTEGER NOT NULL,
    provider_key  TEXT NOT NULL,
    unlocked_at   TEXT,
    PRIMARY KEY (release_id, provider_key),
    FOREIGN KEY (release_id, provider_key)
        REFERENCES achievements(release_id, provider_key) ON DELETE CASCADE
);

-- ── Update tracking ───────────────────────────────────────────────────────
-- Both raw signals stored so the "major update" heuristic can be retuned
-- without re-fetching (§4.5).

CREATE TABLE update_events (
    id          INTEGER PRIMARY KEY,
    release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    kind        TEXT NOT NULL CHECK (kind IN ('build_push', 'announcement')),
    build_id    TEXT,
    occurred_at TEXT NOT NULL,
    title       TEXT,
    raw_json    TEXT
);

CREATE INDEX ix_update_events_release_occurred ON update_events(release_id, occurred_at);

-- ── User organisation ─────────────────────────────────────────────────────

CREATE TABLE lists (
    id           INTEGER PRIMARY KEY,
    name         TEXT NOT NULL,
    description  TEXT,
    is_smart     INTEGER NOT NULL DEFAULT 0 CHECK (is_smart IN (0, 1)),
    filter_json  TEXT
);

CREATE TABLE list_items (
    list_id     INTEGER NOT NULL REFERENCES lists(id) ON DELETE CASCADE,
    release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    position    INTEGER NOT NULL,
    PRIMARY KEY (list_id, release_id)
);

CREATE INDEX ix_list_items_release_id ON list_items(release_id);

-- ── Resolution ────────────────────────────────────────────────────────────

CREATE TABLE merge_candidates (
    id                INTEGER PRIMARY KEY,
    left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
    signals_json      TEXT,
    status            TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'confirmed', 'rejected')),
    UNIQUE (left_release_id, right_release_id)
);

CREATE INDEX ix_merge_candidates_status ON merge_candidates(status);

-- ── Caching / config ──────────────────────────────────────────────────────

CREATE TABLE metadata_cache (
    provider     TEXT NOT NULL,
    provider_id  TEXT NOT NULL,
    payload_json TEXT,
    fetched_at   TEXT NOT NULL,
    PRIMARY KEY (provider, provider_id)
);

CREATE TABLE settings (
    key    TEXT PRIMARY KEY,
    value  TEXT
);
