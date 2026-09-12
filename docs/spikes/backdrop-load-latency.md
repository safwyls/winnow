# Backdrop loading latency

## 2026-09-12 investigation

The reported symptom was that some feed backdrops appeared immediately while others
took two or three seconds. This investigation reproduced an avoidable queueing delay;
it did not measure the individual games or CDN responses in the user's running app.

Memory-cache hits return immediately. Disk-cache hits need decoding. Before this fix,
`CoverCache.LoadAsync` took a decode permit before calling the pipeline, which also
waited for network downloads and retries. Slow downloads therefore occupied all available
decode permits and delayed unrelated artwork already stored on disk.

`CoverConversionBoundaryTests.A_disk_hit_does_not_wait_for_an_unrelated_network_fetch`
uses a temporary cache, one decode permit and a fake source held behind a completion
signal. After the source begins fetching one key, the test requests a different key
whose source bytes already exist on disk. The original implementation fails the
one-second completion deadline. The test releases the source in cleanup, so it never
depends on external network timing.

After the fix, the same controlled disk hit completed in **11.2 ms** while the other
source was still blocked. This is a synthetic regression measurement, not an end-to-end
timing for real game artwork. The 159 cover tests and 12 cache lifetime/conversion tests
passed, including the existing bound on concurrent bitmap conversion.

The pipeline now takes the decode permit only around disk decoding, floor generation
and conversion to Avalonia bitmaps. Downloads keep their fetch permit until their bytes
are consumed, bounding the number of encoded payloads awaiting decoding. Disk hits do
not need a fetch permit. The public pipeline API retains its existing ownership contract.

This shared path serves desktop cards and details as well as fullscreen artwork. Source
preferences, fallback order and download concurrency are unchanged. Cold downloads can
still take seconds; the HTTP policy retries transient failures with exponential backoff
starting at 400 ms and jitter. That is a possible contributor to the reported duration,
not a measured diagnosis of a particular provider response.
