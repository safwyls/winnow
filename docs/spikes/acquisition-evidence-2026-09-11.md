# Acquisition evidence evaluation — 2026-09-11

No new acquisition contribution is justified yet. The repository can display acquisition
facts, but they do not by themselves show that spending predicts a useful recommendation.
The current source contract cannot support per-game discount comparisons. TASK-136 remains
open for captured-library coverage and later-outcome evaluation.

## Method and findings

Inspected `Ownership`, `OwnershipAcquisitionObservation`, `AccountTransactionFact`,
`AccountAcquisitionReader`, `CandidateFacts`, `RecommendationScorer` and the reason phrasebook.
This is a source audit, not a measurement of today's private library.

| Evidence | Supported interpretation | Unsupported interpretation |
|---|---|---|
| Acquisition observation with known account | That account's recorded date, licence and price provenance | Substituting another account's or unknown legacy price |
| Aggregate acquisition dates | Earliest available recorded date | First-ever acquisition, regret, or time spent avoiding the game |
| Ownership paid cents | An attributed amount with its recorded provenance | Currency-normalized value or willingness to play |
| Transaction total/list price | Amount for the captured transaction | Per-game allocation for an unmatched or multi-item transaction |
| Free acquisition | Known free where the source establishes zero | Low interest, low quality, or a scoring penalty |
| Conflicting prices/provenance | Unknown comparison | Picking the higher amount or filling a plausible value |
| Copies on distinct stores | Multiple visible ownerships of one resolved game | Multiple paid purchases, a bundle, or deliberate repeated buying |

`AccountAcquisitionReader` already suppresses conflicting price/source pairs and withholds
legacy Steam fields in a named account. Recommend does not call this App-layer reader and
must not gain an App dependency. A future signal needs a Core contract and correctly scoped
repository projection, with one bounded contribution per resolved game. An additional copy
must not increase a price contribution or cause a second recommendation.

The existing Tier-0 multiple-store bonus remains +0.05. Only its explanation changes: it
describes ownership, because neither that fact nor a never-played bucket proves payment.
Internal `BoughtTwice` names remain compatible with saved replay tuning files. Missing and
free acquisition evidence still leave the arithmetic unchanged. No acquisition tiers,
price thresholds or negative judgements were added.

## Coverage and replay limits

The standalone replay evaluator built successfully in Release with zero warnings/errors.
No prior `capture.json` was found under the repository's `artifacts` or `.scratch` research
directories. A live database exists, but automatic approval review rejected copying it to
research artifacts because a full capture includes account and settings data. No capture
was created and that rejection was not bypassed. Steam/Epic/GOG acquisition coverage,
conflicting-record counts, eligible grouped cohort sizes and real judged coverage therefore
remain **unmeasured**, not zero.

Even an authorized new capture needs a later outcomes capture; it cannot reconstruct an old
ranking population from today's mutable ownership and metadata. Existing sanitized replay
fixtures compare two tunings with 2 judged games among 4 ranked games (coverage 0.5). Their
precision@1 changes from 0 to 1 and reciprocal rank from 0.5 to 1 when an installed weight is
artificially increased. Those fixtures verify measurement mechanics, not acquisition lift.

To finish the evaluation, capture authorized local evidence, report acquisition availability
for each store and account scope after game grouping, then compare a predeclared bounded
hypothesis against the baseline using subsequent unique, attributable outcomes. Report
unmatched, conflicting and unjudged records; do not tune against the test cohort or treat a
changed order as an improvement. A no-signal conclusion remains valid if that evaluation
does not support a contribution.

## Presentation verification

Both desktop and fullscreen use the shared scorer and phrasebook. Sanitized recommendation
tests cover every ownership-only phrase and the score contribution, prohibiting assertions
of payment or bundles without evidence. Production composition tests instantiate both
presentation paths with real templates, linked Steam/Epic copies, and installed/uninstalled
variants; their visible text must not claim that the game was bought or paid for. No private
library was opened in the UI.

Release verification passed 186/186 recommendation tests and 4/4 desktop/fullscreen
`RecommendationCompositionTests`. Reports are `acquisition-evidence.trx` and
`acquisition-ui.trx` under the respective projects' ignored `TestResults` directories.
