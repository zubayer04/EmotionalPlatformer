# Final Submission Evaluation Evidence

Generated on 2026-05-04 from the strongest final JSONL batches in:

`/Users/zubayer/Library/Application Support/DefaultCompany/EmotionalPlatformer/RunLogs`

## Selected Datasets

- `constrained_final.jsonl`
  - Source: `pass4_constrained.jsonl`
  - Purpose: primary current-system evidence with `generationMode = Constrained` and adaptation disabled.
  - Reason selected: clean 20-run batch showing controlled target delivery, active lookahead, structural budget use, and low death/pressure rates.

- `naive_random_final.jsonl`
  - Source: `pass4_naive.jsonl`
  - Purpose: comparison condition for naive/random generation with adaptation disabled.
  - Reason selected: strongest contrast against the constrained generator; shows weaker target tracking, more deaths, and no lookahead-selected slots.

- `adaptive_constrained_final.jsonl`
  - Source: `final_adaptive_constrained.jsonl`
  - Purpose: adaptive-controller trajectory evidence with the final constrained system.
  - Reason selected: clean 10-run adaptive-on batch showing target progression, controller decisions, Markov learning, and comfort-run evidence.

## Generated Files

- `report.md`: dissertation-friendly multi-dataset evaluation summary.
- `runs_summary.csv`: run-level metrics for tables/charts.
- `slots_summary.csv`: slot-level selection and sequencing evidence.
- `markov_weight_drift.csv`: learned Markov table drift evidence.
- `target_vs_actual_*.svg`: target vs delivered structural difficulty charts.

## Reproduction Command

```bash
python3 tools/evaluate_run_logs.py \
  --dataset constrained='EvaluationReports/final_submission/constrained_final.jsonl' \
  --dataset naive='EvaluationReports/final_submission/naive_random_final.jsonl' \
  --dataset adaptive_constrained='EvaluationReports/final_submission/adaptive_constrained_final.jsonl' \
  --out-dir EvaluationReports/final_submission
```

## Interpretation Note

`actualLevelDifficultyScore` is a structural estimate of delivered level content, not an objective measure of player difficulty. Use it alongside runtime outcomes such as deaths, time per chunk, behavioural proxy metrics, transition pressure, generated candidate rate, and qualitative play notes.
