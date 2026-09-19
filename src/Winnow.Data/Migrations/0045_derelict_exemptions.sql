-- A user decision is independent of provider evidence and survives later observations.
CREATE TABLE derelict_exemptions (
    release_id INTEGER PRIMARY KEY REFERENCES releases(id) ON DELETE CASCADE
);
