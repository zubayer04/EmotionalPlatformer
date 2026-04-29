# Emotional Platformer Evaluation Report

Generated At: 2026-04-29 21:21:02

## Evaluation Framing

This report treats difficulty as a multi-metric construct rather than relying on `actualLevelDifficultyScore` as objective truth. Evidence is grouped into controller intent, delivered structural content, and player outcome/behaviour.

## Dataset Summary

| Dataset | Runs | Avg Target | Avg Actual | Avg Actual-Target | Avg Abs Error | Overshoot > 1 | Avg Deaths | Avg Time/Chunk | Avg Pressure | Avg Generated Slots |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| adaptive_on | 10 | 3.95 | 4.93 | 0.98 | 0.98 | 5 | 0.20 | 1.06 | 0.00 | 3.00 |
| adaptive_off | 10 | 3.50 | 4.04 | 0.54 | 0.67 | 2 | 0.00 | 0.86 | 0.00 | 3.20 |

## Controller Intent

### adaptive_on

- Target increases: 2
- Target decreases: 0
- Adaptation decisions:
  - `keep_content_overshot`: 4
  - `keep_about_right`: 3
  - `increase_clean_streak`: 2
  - `keep_minor_error_content_overshot`: 1

### adaptive_off

- Target increases: 0
- Target decreases: 0
- Adaptation decisions:
  - `adaptive_off`: 10

## Delivered Structural Content

### adaptive_on

- Average actual-target delta: 0.98
- Average absolute target error: 0.98
- Slot-level average absolute error: 0.61
- Slot-level errors above 1.0: 11
- High-pressure transitions: 0
- Average generated slots per run: 3.00

### adaptive_off

- Average actual-target delta: 0.54
- Average absolute target error: 0.67
- Slot-level average absolute error: 0.65
- Slot-level errors above 1.0: 12
- High-pressure transitions: 0
- Average generated slots per run: 3.20

## Player Outcome And Behaviour

| Dataset | Avg Deaths | Avg Deaths/Chunk | Avg Time/Chunk | Engagement | Hesitation | Momentum | Reversals/s |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| adaptive_on | 0.20 | 0.02 | 1.06 | 0.79 | 0.05 | 0.75 | 0.27 |
| adaptive_off | 0.00 | 0.00 | 0.86 | 0.74 | 0.10 | 0.73 | 0.25 |

## Markov Learning Audit

- adaptive_on: caps=5, transition updates=90, avg learning quality=0.29
- adaptive_off: caps=0, transition updates=0, avg learning quality=0.00
- Learned entries: 160
- Baseline entries parsed: 104
- Entries with |delta| >= 0.01: 55
- Average absolute drift among changed entries: 0.11
- Top changed transitions:
- Gap -> Precision -> Gap (Medium): baseline 1.75, learned 2.59, delta 0.84
- Precision -> Gap -> Precision (Medium): baseline 1.75, learned 2.37, delta 0.62
- Rest -> Rest -> Gap (Medium): baseline 2.20, learned 2.56, delta 0.36
- Gap -> Gap -> Precision (Medium): baseline 1.75, learned 2.09, delta 0.34
- Vertical -> Rest -> Gap (Medium): baseline 2.20, learned 2.50, delta 0.30
- Gap -> Precision -> Spikes (Medium): baseline 1.00, learned 1.25, delta 0.25
- Gap -> Vertical -> Gap (Medium): baseline 1.75, learned 2.00, delta 0.25
- Vertical -> Gap -> Precision (Medium): baseline 1.75, learned 1.97, delta 0.22
- Vertical -> Gap -> Gap (Medium): baseline 1.00, learned 1.14, delta 0.14
- Rest -> Rest -> Vertical (Medium): baseline 1.75, learned 1.89, delta 0.14

## Interpretation Notes

- `actualLevelDifficultyScore` should be discussed as a structural estimate, not an objective measure of player difficulty.
- Behavioural values are gameplay proxies for strain/flow disruption, not direct emotion classification.
- Strong evidence comes from agreement between delivered structure, runtime outcomes, and player notes.
- Markov learning should be interpreted conservatively unless weight drift and run-level audit fields show meaningful change.
