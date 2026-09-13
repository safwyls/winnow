# Achievement evidence evaluation — 2026-09-11

Keep achievement progress out of recommendation scoring and retirement. TASK-15 now
provides usable account-aware observations, but neither achievement ratios nor global
unlock rarity establishes whether the user wants to return. The captured fixture below
verifies the boundaries required to preserve existing behavior. It does not measure
recommendation quality or real-library provider coverage.

## Evidence and interpretation

Steam exposes a game's achievement schema, one account's unlocks and global unlock
percentages through separate operations in [ISteamUserStats](https://partner.steamgames.com/doc/webapi/isteamuserstats).
Winnow records their observation dates separately. Unknown, unavailable, confirmed no
schema and known zero unlocks are distinct states. Provider failure preserves prior dated
progress; legacy unlocks without a known account cannot supply a named account's history.
The producer's account, freshness and failure tests are recorded in TASK-15.

An unlock ratio measures completed achievements in that release's observed schema. It
does not identify credits, remaining playable content, or the user's intention. Global
rarity describes other accounts' unlocks, rather than the order or effort of this user's
play. Treating either value as commitment would require outcome evidence that distinguishes
these interpretations. None was collected in this evaluation.

No grouped percentage is defined. Each release and account retains its own numerator,
denominator and dates. The recommendation engine's existing one-work ranking remains the
aggregation boundary; a second platform copy does not inherit Steam progress or create a
second recommendation. Since no achievement contribution is justified, the scorer adds
no achievement fact, threshold, weight or explanation. This also keeps unavailable
observations from becoming negative evidence.

## Captured fixture and method

`ReplayTests.Achievement_evidence_keeps_baseline_ranking_accounts_platforms_and_retirement_separate`
creates temporary databases with fake accounts `12345` and `67890`. It captures the same
seven works before and after adding achievement observations at the fixed instant
2040-01-01 12:00 UTC. An Epic sibling of one Steam release makes eight releases in total.
Observations are one hour old. This date is a test clock, not a historical library capture.

The selected account's captured cohort is:

| State | Releases | Meaning |
|---|---:|---|
| Available, known progress | 4 | Zero, half, and two fully unlocked schemas; one fully unlocked game is already retired by playtime. |
| Unknown | 2 | An unfetched Steam release and the unsupported Epic sibling. |
| Unavailable | 1 | A failed/private-style observation without usable progress. The fixture tests the repository state, not an actual private API response. |
| No schema | 1 | A confirmed empty achievement schema. |

Known progress covers four of eight releases, or three of the seven releases attached to
eligible works. These are constructed cohort proportions, not estimates of Steam, Epic,
GOG or this user's library coverage. A second account has 100% on the same Steam release
where the selected account has 50%; both values remain separate. The Epic sibling has no
percentage, and unknown data remains null while known zero remains 0%.

A separately captured outcome database contains one launch-attributed positive, one
explicit negative, and one matured impression without an action. The remaining works
have no labels. The replay excludes the weak negative from judged metrics. Both named
policies use identical default tuning, and both rank the complete eligible population
before selecting judged outcomes. Comparing identical policies is an invariance check;
it is not a candidate-weight experiment.

## Measured result

The before and after capture hashes differ, while the complete ranked entries, scores
and reasons are equal. Both policies return six eligible works, including exactly one
entry for the work with Steam and Epic copies. Two of six ranked works have judged
outcomes, for judged coverage of one third. Synthetic labels cannot establish precision,
lift or preference calibration, so no quality improvement is claimed from this result.

Retirement was checked independently: the fully unlocked game with 300 minutes remains
eligible, while the 6,000-minute game already retired under existing bucket rules remains
excluded. Achievement progress changes no bucket query. Desktop and fullscreen therefore
retain the same shared eligibility behavior. TASK-15's separate presentation tests verify
the account and availability summaries on both surfaces; this evaluation adds no UI.

Verification on 2026-09-11: the replay, reason-contract and resolved-game-evidence test
selection passed 29/29 tests in Release configuration. Reproduce from the repository root:

```powershell
dotnet test tests/Winnow.Recommend.Tests/Winnow.Recommend.Tests.csproj -c Release --artifacts-path artifacts/verify-achievement-replay --filter 'FullyQualifiedName~ReplayTests|FullyQualifiedName~ReasonContractTests|FullyQualifiedName~ResolvedGameEvidenceTests'
```

## Decision and remaining limits

The supported decision is to retain baseline scoring and retirement. The provider makes
achievement evidence inspectable, and the replay verifies that adding it does not silently
alter recommendations. There is no measured basis for a nonzero contribution, a rarity
adjustment, a completion claim or an achievement retirement rule.

No production database was captured, no private account data was inspected, and no live
Steam request was made for this study. Real coverage, observation stability and later
recommendation outcomes remain unmeasured. Reopening scoring research would require
authorized captures made when the state was current, independently captured later outcomes,
and a prespecified bounded candidate compared against the existing model. Those outcomes
would need to justify both the contribution and its one-sentence explanation; a retirement
change would need its own shared-bucket evaluation.
