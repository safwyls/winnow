-- Only positively observed registry identifiers earn registry absence authority.
-- Legacy ownership rows have no evidence distinguishing Galaxy and registry installs.
CREATE TABLE gog_registry_installations (
    provider_id TEXT PRIMARY KEY NOT NULL CHECK (length(trim(provider_id)) > 0)
);
