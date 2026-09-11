-- Plugin observations have their own source scope; refreshing IGDB cannot erase them.
CREATE TABLE plugin_work_facets (
    work_id INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    source TEXT NOT NULL,
    facet_id INTEGER NOT NULL REFERENCES facets(id) ON DELETE CASCADE,
    PRIMARY KEY (work_id, source, facet_id)
);
