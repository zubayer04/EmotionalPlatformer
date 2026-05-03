# Evaluation Report Status

`final_submission/` is the current final evidence bundle for the dissertation. It contains selected raw JSONL logs, regenerated CSV summaries, target-vs-actual SVG charts, Markov drift data, and a report summary.

The other folders in this directory are evidence snapshots from earlier test batches. They are useful for historical comparison, but they should not be treated as current post-change evidence after generator or adaptation changes.

Current stale-after-change notes:
- Blueprint duplicate suppression now removes source-identical `Generated_Rest` and `Generated_Gap_Centered_Flat` runtime candidates.
- Blueprint decoration cells use visual-only `D` symbols, which do not affect structural difficulty or collision.
- Regenerate fresh reports after a new runtime batch before using report tables or charts in the dissertation.
