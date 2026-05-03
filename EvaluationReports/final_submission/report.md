# Emotional Platformer Evaluation Report

Generated At: 2026-05-04 00:36:36

## Evaluation Framing

This report treats difficulty as a multi-metric construct rather than relying on `actualLevelDifficultyScore` as objective truth. Evidence is grouped into controller intent, delivered structural content, and player outcome/behaviour.

## Dataset Summary

| Dataset | Generation Mode(s) | Runs | Avg Target | Avg Actual | Avg Actual-Target | Avg Abs Error | Overshoot > 1 | Avg Deaths | Avg Time/Chunk | Avg Pressure | Avg Generated Slots |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| constrained | Constrained=20 | 20 | 5.00 | 5.08 | 0.08 | 0.50 | 1 | 0.05 | 1.13 | 0.10 | 2.45 |
| naive | NaiveRandom=11 | 11 | 5.00 | 5.96 | 0.96 | 1.36 | 6 | 2.64 | 1.85 | 0.27 | 2.82 |
| adaptive_constrained | Constrained=10 | 10 | 5.60 | 5.78 | 0.18 | 0.48 | 1 | 0.10 | 1.12 | 0.20 | 2.20 |

## Controller Intent

### constrained

- Target increases: 0
- Target decreases: 0
- Comfort-run evidence: 19 runs, including 0 low-signal one-death runs
- Adaptation decisions:
  - `adaptive_off`: 20

### naive

- Target increases: 0
- Target decreases: 0
- Comfort-run evidence: 6 runs, including 0 low-signal one-death runs
- Adaptation decisions:
  - `adaptive_off`: 11

### adaptive_constrained

- Target increases: 3
- Target decreases: 0
- Comfort-run evidence: 10 runs, including 1 low-signal one-death runs
- Adaptation decisions:
  - `keep_about_right`: 6
  - `increase_clean_streak`: 3
  - `keep_content_overshot`: 1

## Delivered Structural Content

### constrained

- Average actual-target delta: 0.08
- Average absolute target error: 0.50
- Slot-level average absolute error: 0.59
- Slot-level errors above 1.0: 11
- High-pressure transitions: 0
- Average generated slots per run: 2.45

### naive

- Average actual-target delta: 0.96
- Average absolute target error: 1.36
- Slot-level average absolute error: 1.55
- Slot-level errors above 1.0: 56
- High-pressure transitions: 1
- Average generated slots per run: 2.82

### adaptive_constrained

- Average actual-target delta: 0.18
- Average absolute target error: 0.48
- Slot-level average absolute error: 0.62
- Slot-level errors above 1.0: 12
- High-pressure transitions: 1
- Average generated slots per run: 2.20

## Player Outcome And Behaviour

| Dataset | Avg Deaths | Avg Deaths/Chunk | Avg Time/Chunk | Engagement | Hesitation | Momentum | Reversals/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| constrained | 0.05 | 0.01 | 1.13 | 0.78 | 0.07 | 0.79 | 0.27 |
| naive | 2.64 | 0.26 | 1.85 | 0.77 | 0.12 | 0.69 | 0.50 |
| adaptive_constrained | 0.10 | 0.01 | 1.12 | 0.78 | 0.06 | 0.77 | 0.33 |

## Markov Learning Audit

- constrained: caps=0, transition updates=0, avg learning quality=0.00
- naive: caps=0, transition updates=0, avg learning quality=0.00
- adaptive_constrained: caps=1, transition updates=90, avg learning quality=0.50
- Learned entries: 197
- Baseline entries parsed: 104
- Entries with |delta| >= 0.01: 90
- Average absolute drift among changed entries: 0.11
- Top changed transitions:
- Gap -> Precision -> Gap (Medium): baseline 1.75, learned 3.23, delta 1.48
- Precision -> Gap -> Precision (Medium): baseline 1.75, learned 2.99, delta 1.24
- Rest -> Rest -> Gap (Medium): baseline 2.20, learned 2.68, delta 0.48
- Gap -> Precision -> Spikes (Medium): baseline 1.00, learned 1.46, delta 0.46
- Vertical -> Gap -> Precision (Medium): baseline 1.75, learned 2.16, delta 0.41
- Gap -> Gap -> Precision (Medium): baseline 1.75, learned 2.10, delta 0.35
- Gap -> Vertical -> Gap (Medium): baseline 1.75, learned 2.10, delta 0.35
- Rest -> Gap -> Vertical (Medium): baseline 1.25, learned 1.56, delta 0.31
- Rest -> Rest -> Vertical (Medium): baseline 1.75, learned 2.06, delta 0.31
- Rest -> Vertical -> Gap (Medium): baseline 1.75, learned 1.98, delta 0.23

## Interpretation Notes

- `actualLevelDifficultyScore` should be discussed as a structural estimate, not an objective measure of player difficulty.
- Behavioural values are gameplay proxies for strain/flow disruption, not direct emotion classification.
- Strong evidence comes from agreement between delivered structure, runtime outcomes, and player notes.
- Markov learning should be interpreted conservatively unless weight drift and run-level audit fields show meaningful change.
