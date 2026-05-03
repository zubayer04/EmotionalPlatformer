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

## Pass 3: Adaptation Behaviour

Status: passed and closed.

Evidence from the adaptive-on 10-run batch:
- Adaptive mode was on for 10/10 runs.
- Target progression was visible:
  - Level 7 increased from `5.00` to `5.50` via `increase_low_strain_undershoot`.
  - Level 10 increased from `5.50` to `6.00` via `increase_clean_streak`.
- Average target difficulty: `5.15`.
- Average actual difficulty: `5.63`.
- Average actual-target delta: `+0.48`.
- Average absolute target error: `0.82`.
- Overshoot greater than `+1`: `3/10`.
- Generated candidate rate: `22/100` slots (`22.0%`).
- Transition pressure count: `3`; high-pressure transitions: `1`.
- Structural budget was enabled in 10/10 runs and penalized `15` slots.
- Markov learning applied in 10/10 runs.

Important run-level observations:
- Level 3 showed the minor-error guard working:
  - target `5.00`, actual `6.50`, one low-strain death.
  - decision: `keep_minor_error_content_overshot`.
- Level 4 and Level 9 showed overshoot blocking immediate increases:
  - Level 4: actual-target `+1.25`, clean run, decision `keep_content_overshot`.
  - Level 9: actual-target `+1.75`, clean run, decision `keep_content_overshot`.
- Level 9 still counted toward the clean streak despite the overshoot, then Level 10 increased target after another clean low-strain run.
- Player notes aligned with logs:
  - `Chunk_StairsUp_Tilemap -> Chunk_Spikes_Tilemap` was logged as `awkward_exit_to_spikes` with strong pressure.
  - `Chunk_PrecisionJump_Tilemap -> Chunk_SpikesMedium_Tilemap` was logged as `precision_to_recoverable_spikes` with moderate pressure.

Interpretation:
- The controller is working defensibly: it can increase target, hold steady, avoid overreacting to a single low-signal death, and block immediate increases when delivered content overshoots heavily.
- The controller is intentionally conservative. A low-strain accidental death avoids a decrease, but still resets the clean-run streak.
- This conservatism is acceptable for the final system, but should be discussed as a limitation/tuning choice.

Final refinement:
- Added a low-strain comfort streak before Pass 4.
- A run now contributes to comfort evidence if it is clean, or if it has exactly one low-signal death with low strain, low deaths-per-chunk, and fast completion.
- This keeps accidental one-death runs from erasing evidence that the player is comfortable.
- It does not make repeated deaths or high-strain deaths count as comfort evidence.
- Retrospective replay of the adaptive-on batch suggested the target would have reached about `6.50` instead of `6.00`, which better matches the low-struggle playdata.

Pressure-aware Markov note:
- Pressure-aware Markov did not trigger in this batch.
- This is not a failure because the trigger requires repeated deaths on a pre-logged strong/severe pressure transition.

Decision:
- Close Pass 3.
- Keep the comfort-streak refinement and verify it with a short adaptive smoke batch before using adaptation results as evidence.
- Pass 4 generator comparison can still proceed with adaptation off after the smoke check.

## Next Pass: Evaluation Evidence

Recommended next step:
- Move into Pass 4: final evaluation evidence and comparison setup.
- Prioritize evidence that supports dissertation claims rather than adding new gameplay systems.

Main comparison to prepare:
- Current constrained generator vs naive/random generation.
- This is the strongest evaluation story because PCG/sequencing quality is a core contribution.
- Evaluation support now exposes `generationMode`:
  - `Constrained` uses the full sequencing stack.
  - `NaiveRandom` randomly selects from the same eligible candidate pool while keeping only basic hard playability constraints.
  - The mode is written into JSONL and surfaced by the analyzer/evaluation report.

Post-smoke playability fix:
- A NaiveRandom run was abandoned after an unclearable local geometry interaction:
  `Generated_GapHazard_ExitOuterSpike` into `Chunk_MovingClimbSpikes_Tilemap`.
- The issue was not a general difficulty-tuning problem; it was a specific geometry conflict between the exit spike on the generated gap and the vertical spike guard inside the moving climb spike chunk.
- Added a narrow constrained-mode hard ban for that exact pair.
- This keeps the constrained generator defensible as a playability-filtered system while preserving NaiveRandom as a deliberately weak comparison mode.

Secondary evidence:
- Adaptive-on vs adaptive-off can be used to show target trajectory/controller behaviour, but should not be overclaimed as proof of improved player experience from the current small sample.
- Blueprint isolation/runtime evidence can support controlled variety and validation.
- Player notes should be treated as qualitative triangulation.
