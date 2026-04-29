# Emotional Platformer Evaluation Report

Generated At: 2026-04-29 18:02:34

## Evaluation Framing

This report treats difficulty as a multi-metric construct rather than relying on `actualLevelDifficultyScore` as objective truth. Evidence is grouped into controller intent, delivered structural content, and player outcome/behaviour.

## Dataset Summary

| Dataset | Runs | Avg Target | Avg Actual | Avg Actual-Target | Avg Abs Error | Overshoot > 1 | Avg Deaths | Avg Time/Chunk | Avg Pressure | Avg Generated Slots |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| current | 5 | 4.70 | 4.81 | 0.11 | 0.35 | 0 | 0.20 | 1.14 | 0.00 | 2.20 |

## Controller Intent

### current

- Target increases: 0
- Target decreases: 0
- Adaptation decisions:
  - `keep_about_right`: 5

## Delivered Structural Content

### current

- Average actual-target delta: 0.11
- Average absolute target error: 0.35
- Slot-level average absolute error: 0.62
- Slot-level errors above 1.0: 7
- High-pressure transitions: 0
- Average generated slots per run: 2.20

## Player Outcome And Behaviour

| Dataset | Avg Deaths | Avg Deaths/Chunk | Avg Time/Chunk | Engagement | Hesitation | Momentum | Reversals/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| current | 0.20 | 0.02 | 1.14 | 0.81 | 0.05 | 0.82 | 0.36 |

## Markov Learning Audit

- current: caps=0, transition updates=45, avg learning quality=0.62
- Learned entries: 139
- Baseline entries parsed: 104
- Entries with |delta| >= 0.01: 43
- Average absolute drift among changed entries: 0.10
- Top changed transitions:
- Gap -> Precision -> Gap (Medium): baseline 1.75, learned 2.30, delta 0.55
- Precision -> Gap -> Precision (Medium): baseline 1.75, learned 2.18, delta 0.43
- Rest -> Rest -> Gap (Medium): baseline 2.20, learned 2.46, delta 0.26
- Gap -> Vertical -> Gap (Medium): baseline 1.75, learned 1.98, delta 0.23
- Gap -> Gap -> Precision (Medium): baseline 1.75, learned 1.95, delta 0.20
- Gap -> Precision -> Spikes (Medium): baseline 1.00, learned 1.17, delta 0.17
- Vertical -> Gap -> Precision (Medium): baseline 1.75, learned 1.91, delta 0.16
- Safe -> Gap -> Vertical (Medium): baseline 1.25, learned 1.38, delta 0.13
- Vertical -> Gap -> Gap (Medium): baseline 1.00, learned 1.13, delta 0.13
- Precision -> Gap -> Gap (Medium): baseline 1.00, learned 1.12, delta 0.12

## Interpretation Notes

- `actualLevelDifficultyScore` should be discussed as a structural estimate, not an objective measure of player difficulty.
- Behavioural values are gameplay proxies for strain/flow disruption, not direct emotion classification.
- Strong evidence comes from agreement between delivered structure, runtime outcomes, and player notes.
- Markov learning should be interpreted conservatively unless weight drift and run-level audit fields show meaningful change.
