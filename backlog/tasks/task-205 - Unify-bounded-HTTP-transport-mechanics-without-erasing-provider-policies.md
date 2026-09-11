---
id: TASK-205
title: Unify bounded HTTP transport mechanics without erasing provider policies
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 Provider clients share bounded transport mechanics or a common conformance contract for response limits, per-attempt/overall timeouts, cloning and disposal.
- [ ] #2 Provider-specific rate budgets, Retry-After behavior and safe retry semantics remain explicit, with each attempted send acquiring its required permit.
- [ ] #3 Tests distinguish caller cancellation from timeout and cover oversized responses, retry exhaustion and request/response disposal across the registered clients.
<!-- AC:END -->
