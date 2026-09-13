# Acquisition evidence evaluation — 2026-09-11

Retain baseline scoring without a new acquisition contribution. The repository can display
acquisition facts, but they do not by themselves show that spending predicts a useful
recommendation. The current source contract cannot support per-game discount comparisons.
The captured fixture below verifies account, grouping and baseline boundaries. Real-library
coverage and recommendation quality remain unmeasured.

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

## Captured fixture and measured boundaries

`ReplayTests.Acquisition_captures_preserve_account_provenance_and_baseline_without_comparing_prices`
uses seven native works, releases and ownerships: six Steam and one Epic. A reversible link
joins the Epic copy to one Steam game, leaving six resolved games. Both fake accounts have
the same Steam membership, so changing account selection does not change the population.
Captures use the fixed test instant 2040-01-01 12:00 UTC; it is not a historical library date.

The test captures the baseline before acquisition facts, then the same library with these
observations, and finally with the second account selected:

| Evidence | Constructed cohort |
|---|---|
| Selected account `12345` | Four observations across three Steam ownerships: one amount of 500, one known zero, and two conflicting amounts (500 and 700) on the third ownership. |
| Other account `67890` | One observation of 9,000 on the same ownership where the selected account has 500. |
| Unknown account | One separate observation of 99,999 on that ownership. |
| Legacy ownership fields | Four raw prices, including zero, an unknown-currency amount, and the linked Epic copy's amount. These fields cannot replace named Steam account observations. |
| Transactions | Two unmatched two-item transactions, with different currency symbols and totals. Both retain their transaction-level list price and discount; neither has an app ID or allocates a game price. |

The selected account has observations for three of six Steam ownerships, but only two
ownerships have an unconflicted amount, one of which is zero. These constructed proportions
are not estimates of this user's library or any store's coverage. The unmapped bundle's
ownership amount stays null. No raw-price ordering, currency conversion, bundle allocation
or per-game discount enters the scorer.

Separate production `AccountAcquisitionReader` tests verify that a selected account's zero
remains zero, missing evidence remains null despite legacy or other-account amounts, and
both amount conflicts and equal amounts with conflicting provenance become unknown.
Switching accounts projects that account's observation; aggregate presentation suppresses
conflicting observations. The replay additionally verifies that account selection and
acquisition facts cannot silently change scores when no acquisition signal exists.

A separately captured outcome database supplies one launch-attributed positive, one explicit
negative, and one matured impression without an action. Two of six ranked games are judged,
for coverage of one third; the weak negative is excluded from judged metrics. Both named
policies use identical default tuning and rank the complete population before label selection.
The before/after hashes differ, but every ranked entry, score and reason remains equal across
the three captures and both policies. The linked pair appears exactly once.

This comparison verifies the proposed no-contribution policy's invariants. It is not a
candidate-weight experiment, and synthetic labels cannot establish preference calibration,
precision or lift. Spending and acquisition dates remain inspectable facts with no added
weight, tier or explanation; the existing ownership bonus and retirement behavior stay intact.

## Remaining evidence limits

This fixture evaluation used temporary databases and fake accounts; no production records
were captured. The earlier automatic approval rejection of a full database backup was not
bypassed. The replay tool has no
sanitized-capture mode; its backup includes account and settings data. Real Steam/Epic/GOG
acquisition coverage, conflict rates and later-outcome performance remain **unmeasured**.

The available source semantics and verified boundaries support retaining baseline scoring;
they do not establish that acquisition evidence can never improve recommendations. Reopening
that research would require authorized captures made when their state was current, scoped
per-store/grouped coverage, a predeclared bounded hypothesis, and independently captured later
outcomes. Today's mutable library cannot reconstruct the historical ranking population.
Report unmatched, conflicting and unjudged records without treating changed order as improved
quality or tuning against the evaluation cohort.

## Presentation verification

Both desktop and fullscreen use the shared scorer and phrasebook. Sanitized recommendation
tests cover every ownership-only phrase and the score contribution, prohibiting assertions
of payment or bundles without evidence. Production composition tests instantiate both
presentation paths with real templates, linked Steam/Epic copies, and installed/uninstalled
variants; their visible text must not claim that the game was bought or paid for. No private
library was opened in the UI.

Initial Release verification passed 186/186 recommendation tests and 4/4 desktop/fullscreen
`RecommendationCompositionTests`. The final targeted replay, reason-honesty and resolved-game
selection passed 30/30 tests, production account projection passed 11/11, and the final
desktop/fullscreen composition check passed 4/4. Reports are `acquisition-replay.trx`,
`acquisition-scope.trx` and `acquisition-final-ui.trx` under the respective projects' ignored
`TestResults` directories.
