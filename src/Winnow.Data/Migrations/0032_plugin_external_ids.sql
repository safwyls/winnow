-- Preserve existing hard joins while allowing namespaced library-source identities.
CREATE TABLE external_ids_with_plugins (
    release_id INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    provider TEXT NOT NULL CHECK (
        provider IN ('steam', 'gog', 'epic', 'igdb')
        OR (provider GLOB 'plugin:*' AND length(provider) > 7)
    ),
    provider_id TEXT NOT NULL,
    PRIMARY KEY (provider, provider_id)
);
INSERT INTO external_ids_with_plugins(release_id, provider, provider_id)
SELECT release_id, provider, provider_id FROM external_ids;
DROP TABLE external_ids;
ALTER TABLE external_ids_with_plugins RENAME TO external_ids;
CREATE INDEX ix_external_ids_release_id ON external_ids(release_id);
