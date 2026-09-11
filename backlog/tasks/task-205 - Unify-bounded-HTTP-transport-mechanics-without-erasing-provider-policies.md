---
id: TASK-205
title: Unify bounded HTTP transport mechanics without erasing provider policies
status: Done
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 07:47'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Enrich.Igdb/Http/IgdbResilienceHandler.cs:30'
  - 'src/Winnow.Enrich.Igdb/ServiceCollectionExtensions.cs:88'
  - 'src/Winnow.Enrich.Stores/ServiceCollectionExtensions.cs:23'
  - src/Winnow.Enrich.SteamWeb/Http
  - src/Winnow.Enrich.Updates/Http
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: enhancement
ordinal: 236000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R17. Evidence: Source-verified divergence; stress impact unmeasured. Six resilience handlers repeat cloning, retry and limiter mechanics; timeout handling is inconsistent. Some request clones are disposed and others are not. Stores explicitly bounds response buffers/time while most clients rely on broad HttpClient defaults. Timeout predicates include TimeoutException without a consistent explicit per-attempt timeout model, although HttpClient timeouts use cancellation exceptions. Copied infrastructure makes resource, cancellation and retry behavior inconsistent. A shared mechanism should retain provider-specific rate budgets, safe retry rules and interpretation of statuses rather than forcing one vendor policy.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Provider clients share bounded transport mechanics or a common conformance contract for response limits, per-attempt/overall timeouts, cloning and disposal.
- [x] #2 Provider-specific rate budgets, Retry-After behavior and safe retry semantics remain explicit, with each attempted send acquiring its required permit.
- [x] #3 Tests distinguish caller cancellation from timeout and cover oversized responses, retry exhaustion and request/response disposal across the registered clients.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Share bounded request replay, response buffering and disposal mechanics through linked infrastructure source while retaining module independence. 2. Give registered providers explicit Polly per-attempt and overall timeout budgets, preserve provider retry/status/Retry-After rules and innermost per-send rate permits. 3. Repair auth/retry clone lifetime gaps without replaying mutations or logging credentials. 4. Add canned conformance tests across registered providers for caller cancellation, timeout retries/exhaustion, bounded responses and request/response disposal; document transport limits and run focused HTTP/client suites.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared linked transport is adopted by seven provider assemblies and15 actual registered typed/named HTTP clients. Conformance plus existing policy/storefront suite passed140/140, including104 conformance cases: retry replay preserves body/headers/options, every clone and replaced response is disposed, caller cancellation never retries, header/body timeouts exhaust as transport failure, overall budget stops work, unknown-length oversize refuses without retry, and14 rate-limited clients spend a measured permit for every retry (Twitch minting correctly has no IGDB budget). Broader provider client suites are running before finalization. No Core IO dependency or live API traffic introduced.

Final affected provider-client regression suite passed1067/1067 in Windows Release after140/140 conformance/policy checks. The conformance suite caught an omitted outer limit on SteamLifecycleClient registration; corrected and rerun green. Build-spec5.1 documents shared mechanics and operating bounds; decisions record the choice. All requests used canned handlers. Desktop/fullscreen consume the same background services; this changes no presentation layout or credential settings flow.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Unified bounded replay, timeout, response buffering and disposal across15 registered provider pipelines without changing their vendor budgets or status semantics. Verified140/140 conformance/policy tests and1067/1067 affected client regressions; caller cancellation, timeout exhaustion, oversize disposal and per-retry rate permits are covered.
<!-- SECTION:FINAL_SUMMARY:END -->
