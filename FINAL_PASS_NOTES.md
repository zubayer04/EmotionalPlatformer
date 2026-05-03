# Final System Pass Notes

This file tracks observations, cleanup ideas, and deferred suggestions from the final pre-evaluation pass. It is not a commitment checklist; it is a lightweight memory aid so final evidence gathering stays focused.

## Pass 1: Generator Delivery

Status: passed.

Evidence from the fixed-target, lookahead-on 10-run batch:
- `useLookaheadSequencePlanning=True` in 10/10 runs.
- Lookahead used in 80/100 slots.
- Adaptation was off in 10/10 runs, so generator delivery was judged against a stable target.
- Average actual-target delta: `+0.16`.
- Average absolute target error: `0.48`.
- Overshoot greater than 1: `1/10`.
- Transition pressure count: `1`.
- High-pressure transitions: `0`.
- Generated candidate rate: `19/100` slots.
- Human notes mostly agreed with shown actual difficulty.

Important observation:
- `Chunk_SpikesMedium_Tilemap -> Chunk_PrecisionJump_Tilemap` appeared once and was logged as `low_recovery_to_harsh_entry`. It felt challenging but not unfair.

Decision:
- Do not tune sequencing delivery yet.
- Carry forward to later passes that Gap + Precision currently dominate the tag distribution.

## Sequencing Technical Review

Current sequencing is complex but defensible, not complexity for its own sake.

Core layers:
- hard constraints for clearly bad/impossible patterns
- exact prefab repeat avoidance
- same primary tag streak control
- two-step Markov weighting
- difficulty preference weighting
- variety bonus
- early hard penalty
- extreme outlier filtering/penalty
- transition pressure weighting
- whole-level structural budget weighting
- generated blueprint candidate weighting
- bounded lookahead

Deferred cleanup ideas:
- Consider adding a dissertation-facing pseudocode/diagram for the sequencing score:
  `score = markov * difficulty * variety * pacing * outlier * transitionPressure * structuralBudget * generatedWeight`.
- Consider documentation cleanup around generated candidate terminology, because some internal methods still use "replacement" naming even though runtime selection now uses generated candidates in the pool.
- Do not combine early hard penalty with extreme outlier handling for now; they overlap but serve different purposes.
- Do not combine transition pressure and lookahead; transition pressure scores local pair risk, while lookahead evaluates short-horizon consequences.

Possible future investigation:
- Transition pressure is currently a supporting structural metric, not part of `actualLevelDifficultyScore`.
- If felt difficulty repeatedly exceeds actual difficulty specifically because of pressured transitions, consider a small transition-pressure contribution to the structural score.
- Do not change this based on one mild case.

## Pass 2: Content Variety and Whole-Level Structural Control

Status: passed and closed.

Original Pass 2 observation:
- Generated safe/gap/precision candidates improved visible/structural variety.
- The main remaining sameyness was mechanical: many levels still revolved around gap/precision jumping.
- Gap and precision tags dominated the early Pass 2 evidence.

Implemented refinements:
- Added controlled hazard-accented generated gap variants for gap sources.
- Hazard-accent gaps remain primary `Gap`, add secondary `Spikes`, set `hasHazard=true`, and add one estimated jump.
- Hazard-accent gaps now keep the source gap difficulty. This avoids double-counting the spike through both chunk difficulty and the hazard/jump structural score.
- The spike collider for generated hazard-gap variants was calibrated to be forgiving and visually aligned enough for gameplay testing.
- Added whole-level structural budget awareness to candidate/lookahead scoring.
- Structural budget tracks additive load from hazards, estimated jumps, and vertical chunks while the level is being sequenced.
- Over-budget candidates are softly down-weighted, not hard-blocked.
- Current tuned values in `ProcGenTest.unity`:
  - `structuralBudgetSlack = 0.60`
  - `structuralBudgetPenaltyStrength = 0.80`
  - minimum budget multiplier = `0.20`

Final tuned-budget evidence from the latest 11-run adaptive-off batch:
- Average actual-target delta: `+0.04`.
- Average absolute target error: `0.39`.
- Overshoot greater than `+1`: `0/11`.
- Maximum delta: `+0.70`.
- Generated candidate rate: `28/110` slots (`25.5%`).
- Hazard-gap appearances: `1/110` slots.
- Structural budget penalized slots: `16`.
- Transition pressure count: `2`, both high-pressure precision-to-spikes cases.

Variety comparison:
- Compared against the earlier 20 runs in `level_runs.jsonl`, target delivery improved strongly:
  - Earlier 20 avg delta: `+0.49`; avg abs error: `0.65`; overshoot `> +1`: `6/20`.
  - Latest 11 avg delta: `+0.04`; avg abs error: `0.39`; overshoot `> +1`: `0/11`.
- Variety was not strongly sacrificed:
  - Generated rate stayed at `25.5%`.
  - Average unique chunks per run improved slightly from `8.3` to `8.5`.
  - Chunk entropy improved from `0.89` to `0.91`.
  - Same-tag transition rate dropped from `5.0%` to `2.3%`.
- Compared with `prehazard_pass2.jsonl`, the latest batch had stronger target matching while preserving acceptable chunk variety.

Known tradeoff:
- The tuned structural budget makes target-5 levels less spiky.
- Average hazards per run dropped in the latest target-5 batch.
- This is acceptable for target 5 because the goal was to prevent medium-target levels becoming unintentionally 6.5-7 difficulty.
- Higher target difficulties should be checked later to confirm the generator still allows more hazard/spike spread when the target is intentionally higher.

Decision:
- Close Pass 2.
- No extra Pass 2 batch is required before moving on, because the latest 11-run tuned-budget batch directly tested the active structural-budget settings and showed better target delivery without a meaningful variety collapse.
- Do not add more blueprint shapes or handcrafted chunks during this pass.
- Do not lower hazard scoring based on the target-5 evidence; the issue was whole-level structural stacking, not obviously incorrect hazard weighting.
- Carry forward that hazard-gap variants are occasional accents rather than a frequent source of variety.

## Pressure-Aware Markov Learning

Status: implemented as a narrow adaptive-mode safeguard.

Purpose:
- Make learned Markov weights less decorative without turning every death into a learning signal.
- Only strengthen avoidance when runtime struggle confirms a transition already classified as locally pressured.

Rule:
- Applies only when adaptive mode is on and normal Markov learning runs.
- Requires a receiving slot with:
  - `transitionPressurePenalized == true`
  - pressure severity `strong` or `severe`
  - at least `3` deaths attributed to that slot
- Applies an extra negative Markov quality penalty of `0.8`.
- Logs:
  - `pressureAwareMarkovApplied`
  - `pressureAwareMarkovTransitions`
  - `pressureAwareMarkovMaxDeathsOnSlot`
  - `pressureAwareMarkovPenaltyTotal`
  - `pressureAwareMarkovReasons`

Interpretation:
- This should be described as evidence-gated online tuning, not broad machine learning.
- The system triangulates designer/system pressure metadata with runtime player struggle.
- It is expected to trigger rarely.

Testing note:
- This will not trigger in adaptive-off Pass 2 batches.
- It should be inspected during Pass 3 adaptation runs, but it does not require reopening Pass 2 because it affects adaptive learning after a level, not the fixed-target generator delivery tested in Pass 2.
- If it does not trigger naturally, that is not a failure; it means the player did not hit repeated deaths on a pre-logged strong/severe pressure transition.

## Next Pass: Adaptation Behaviour

Recommended next step:
- Run adaptive mode on from a known starting difficulty.
- Use the now-stabilised generator to judge adaptation decisions without confounding them with frequent generator overshoot.

Questions for Pass 3:
- Does target difficulty increase after sustained low-strain/clean play?
- Does the controller avoid overreacting to single accidental deaths?
- Does it decrease or hold target sensibly when delivered content overshoots?
- Do behavioural signals and Markov learning appear in logs as supporting evidence rather than decorative fields?
- If pressure-aware Markov learning triggers, is the reason interpretable from both the pressure metadata and the runtime deaths?
