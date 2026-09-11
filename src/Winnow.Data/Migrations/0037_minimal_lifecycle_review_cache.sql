-- Retain valid review evidence and its original cache timestamp while removing
-- author profiles and review prose, including entries never requested again.
DELETE FROM metadata_cache
WHERE provider = 'steam-lifecycle-v1' AND provider_id LIKE 'reviews:%'
  AND payload_json IS NOT NULL
  AND CASE WHEN json_valid(payload_json) THEN json_type(payload_json) <> 'object' ELSE 1 END;

DELETE FROM metadata_cache
WHERE provider = 'steam-lifecycle-v1' AND provider_id LIKE 'reviews:%'
  AND payload_json IS NOT NULL
  AND (json_extract(payload_json, '$.success') IS NOT 1
       OR json_type(payload_json, '$.reviews') IS NOT 'array'
       OR json_array_length(payload_json, '$.reviews') > 100
       OR unixepoch(fetched_at) IS NULL
       OR EXISTS (
           SELECT 1 FROM json_each(payload_json, '$.reviews') AS review
           WHERE CASE WHEN review.type = 'object' THEN
               json_type(review.value, '$.timestamp_created') IS NOT 'integer'
               OR json_extract(review.value, '$.timestamp_created') < 0
               OR json_extract(review.value, '$.timestamp_created') > unixepoch(fetched_at)
             ELSE 1 END));

UPDATE metadata_cache SET payload_json = json_object(
    'version', 2, 'success', 1, 'filter', 'recent', 'language', 'all',
    'purchase_type', 'all', 'review_type', 'all', 'window_days', 30,
    'observed_at', fetched_at,
    'complete', json(CASE WHEN json_array_length(payload_json, '$.reviews') < 100
        OR EXISTS (SELECT 1 FROM json_each(payload_json, '$.reviews') AS review
            WHERE json_extract(review.value, '$.timestamp_created') < unixepoch(fetched_at) - 2592000)
        THEN 'true' ELSE 'false' END),
    'timestamp_created', json((SELECT json_group_array(json_extract(review.value, '$.timestamp_created'))
        FROM json_each(payload_json, '$.reviews') AS review)))
WHERE provider = 'steam-lifecycle-v1' AND provider_id LIKE 'reviews:%' AND payload_json IS NOT NULL;
