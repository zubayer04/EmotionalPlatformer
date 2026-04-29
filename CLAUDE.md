# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Final-year BSc Computer Science project: a Unity 2D platformer with emotionally adaptive level generation. The research goal is investigating real-time difficulty/flow adaptation using player behaviour proxies (frustration, struggle, engagement). This is a research system, not a polish project.

## Build & Run

- **Unity version**: Open in Unity Editor (2D URP project, company "DefaultCompany", product "EmotionalPlatformer")
- **Scenes**: `Assets/Scenes/ProcGenTest.unity` (main test scene), `Assets/Scenes/Sandbox.unity`
- **No CLI build pipeline** — build/run through Unity Editor play mode
- **Run log analysis**: `python3 tools/analyze_run_logs.py [path-to-jsonl]` (defaults to `~/Library/Application Support/DefaultCompany/EmotionalPlatformer/RunLogs/level_runs.jsonl`)
- **Editor tools**: `Assets/Editor/Calibration/ChunkCalibrationReporterWindow.cs` — custom editor window for chunk calibration

## Architecture

### Level Generation Pipeline

1. **LevelGenerator** orchestrates chunk sequencing using a 2-step Markov chain with weighted selection:
   - `MarkovWeightTable.GetWeight(prev2, prev1, next, band)` × difficulty closeness (Gaussian) × variety bonus × pacing weight
   - Hard constraints: no Spikes→Spikes, no Precision→Precision, no 3× Vertical streak
   - `DifficultyBand` (Low ≤3.5, Medium ≤6.5, High >6.5) gates which Markov weights apply
   - Optionally injects runtime-generated blueprint chunks alongside handcrafted prefabs

2. **MarkovWeightTable** (online learning, `Assets/Scripts/MarkovWeightTable.cs`):
   - Stores both hand-tuned `baselineWeights` and live `learnedWeights` (keyed on prev2/prev1/next/band)
   - `UpdateWeight()` nudges learned weights toward quality signal; `DecayTowardBaseline()` prevents drift
   - Persists to `{persistentDataPath}/MarkovWeights/learned_weights.json` via `TrySave`/`TryLoad`
   - `ResetToBaseline()` wipes learned weights back to hand-tuned starting point

3. **ChunkBlueprint system** (procedural chunk generation):
   - `ChunkBlueprint` — grid-based chunk representation (rows of characters)
   - `SimpleChunkBlueprintGenerator` — creates blueprint variants from templates
   - `ChunkBlueprintValidator` / `ChunkBlueprintValidationResult` — validates structural correctness
   - `ChunkBlueprintRuntimeBuilder` — converts validated blueprints into Unity GameObjects at runtime
   - `ChunkBlueprintFeatureExtractor` — derives metadata (difficulty, tags) from blueprint structure
   - `RealChunkBlueprintLibrary` — provides source blueprints derived from actual prefabs

4. **Adaptive Difficulty Loop** (`LevelManager` → `AdaptiveDifficultyController`):
   - `LevelManager` collects deaths, time, and a `BehaviourSummary` from `PlayerBehaviourTracker` each level
   - `AdaptiveDifficultyController.Evaluate(Input)` is pure static logic returning a `Decision` struct
   - Strain formula: classic strain (deaths + time, 60%) blended with behavioural strain (40%)
   - Behavioural strain sources: hesitation score, momentum fluidity, direction-reversal rate, retry delay, death-clustering ratio
   - Decision codes: `decrease_high_strain`, `decrease_content_overshoot`, `increase_low_strain_undershoot`, `increase_clean_streak`, `keep_*`
   - Adjusts `targetDifficulty` on `LevelGenerator` for next level; clamped to `[minTargetDifficulty, maxTargetDifficulty]`

5. **PlayerBehaviourTracker** (`Assets/Scripts/PlayerBehaviourTracker.cs`):
   - Runs in `FixedUpdate`; accumulates per-chunk and per-level motion stats from `Rigidbody2D`
   - Signals: `hesitationScore` (grounded near-stationary fraction), `momentumFluidity` (avg |vx|/maxSpeed), `directionReversalRate` (sign flips/s), `avgRetryDelay` (respawn→input latency), `deathClusteringRatio`
   - `BehaviourSummary.EngagementScore()` is a weighted composite (0–1); logged to JSONL

6. **ChunkData** (MonoBehaviour on each chunk prefab): stores primaryTag, difficultyRating, entry/exit transforms, hazard info

7. **ChunkTransitionPressure** — scores difficulty of transitions between consecutive chunks

### Key Data Flow

```
PlayerBehaviourTracker (FixedUpdate motion signals)
  → BehaviourSummary (per-level aggregate)
  → AdaptiveDifficultyController.Evaluate() (pure decision logic, blends classic + behavioural strain)
  → LevelGenerator.targetDifficulty (next level's target)
  → LevelGenerator.GenerateLevel() (Markov sequencing via MarkovWeightTable + blueprint injection)
  → Instantiated chunk GameObjects with ChunkData
  → (post-level) MarkovWeightTable.UpdateWeight() / DecayTowardBaseline()
```

### Logging & Instrumentation

- `LevelRunLog` writes JSONL records (schema v7) to persistent data path after each level
- Records include: slot-level chunk details, death events, adaptation decisions, transition pressure, `BehaviourSummary` fields, engagement score
- `tools/analyze_run_logs.py` summarizes logs for calibration review

### Chunk Prefabs

17 handcrafted chunks in `Assets/Prefabs/Chunks/` following naming convention `Chunk_{Name}_Tilemap.prefab`. Each has a `ChunkData` component with Entry/Exit child transforms for snapping.

## Working Conventions

- Treat as research-oriented adaptive system; prioritize adaptation logic, generation quality, metrics, and dissertation relevance
- Prefer small, reviewable diffs over broad rewrites
- Avoid unnecessary Unity asset/scene/prefab churn
- Before major changes: inspect current implementation, explain subsystem, propose minimal plan
- When changing code: explain how to test in Unity, side effects, and dissertation relevance
- `ChunkTag` enum: Safe, Gap, Spikes, Vertical, Precision, Rest
- Difficulty ratings are int 0–10 on ChunkData
- `DifficultyBand` thresholds: Low ≤3.5, Medium ≤6.5, High >6.5 — changing these shifts all Markov weight lookups

## Known Design Gaps

- No `Rest` chunk prefab exists despite `ChunkTag.Rest` being used in Markov tables
- `MarkovWeightTable.UpdateWeight()` is wired but the call site (quality signal computation after each level) is the key research question — what makes a good quality signal?
- `BehaviourSummary` engagement weights and strain weights in `AdaptiveDifficultyController` are empirical; not calibrated from data yet
- No persistent player profile across sessions (learned Markov weights persist, but `targetDifficulty` resets)
- `LevelDifficultyScore` is computed post-generation — no lookahead or rejection/retry

## Branch Context

- `main` — stable branch
- `emotional-signals-learning` — current branch; adds `PlayerBehaviourTracker`, `BehaviourSummary`, online `MarkovWeightTable` learning, and behavioural strain signals to `AdaptiveDifficultyController`
