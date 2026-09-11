ALTER TABLE sessions ADD COLUMN monitor_key TEXT;

CREATE UNIQUE INDEX idx_sessions_monitor_key
    ON sessions(monitor_key) WHERE monitor_key IS NOT NULL;

CREATE TABLE monitored_session_keys (
    monitor_key TEXT PRIMARY KEY NOT NULL,
    session_id  INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE
);

CREATE TABLE monitored_session_processes (
    session_id   INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
    process_id   INTEGER NOT NULL,
    started_at   TEXT NOT NULL,
    process_name TEXT NOT NULL,
    PRIMARY KEY (session_id, process_id, started_at, process_name)
);

CREATE INDEX idx_monitored_session_process_identity
    ON monitored_session_processes(process_id, started_at, process_name);
