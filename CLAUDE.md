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
   - Transition weights (Markov state) × difficulty closeness (Gaussian) × variety bonus × pacing weight
   - Hard constraints: no Spikes→Spikes, no Precision→Precision, no 3× Vertical streak
   - Optionally injects runtime-generated blueprint chunks alongside handcrafted prefabs

2. **ChunkBlueprint system** (procedural chunk generation):
   - `ChunkBlueprint` — grid-based chunk representation (rows of characters)
   - `SimpleChunkBlueprintGenerator` — creates blueprint variants from templates
   - `ChunkBlueprintValidator` / `ChunkBlueprintValidationResult` — validates structural correctness
   - `ChunkBlueprintRuntimeBuilder` — converts validated blueprints into Unity GameObjects at runtime
   - `ChunkBlueprintFeatureExtractor` — derives metadata (difficulty, tags) from blueprint structure
   - `RealChunkBlueprintLibrary` — provides source blueprints derived from actual prefabs

3. **Adaptive Difficulty Loop** (LevelManager + AdaptiveDifficultyController):
   - `LevelManager` tracks deaths, time, per-chunk stats during play
   - After each level, `AdaptiveDifficultyController` (pure static logic) computes a difficulty adjustment decision
   - Decision uses deathsPerChunk, timePerChunk, clean-run streaks, strain smoothing
   - Adjusts `targetDifficulty` on `LevelGenerator` for the next level

4. **ChunkData** (MonoBehaviour on each chunk prefab): stores primaryTag, difficultyRating, entry/exit transforms, hazard info

5. **ChunkTransitionPressure** — scores difficulty of transitions between consecutive chunks

### Key Data Flow

```
LevelManager (game loop, stats) 
  → AdaptiveDifficultyController (pure decision logic)
  → LevelGenerator.targetDifficulty (next level's target)
  → LevelGenerator.GenerateLevel() (Markov sequencing + blueprint injection)
  → Instantiated chunk GameObjects with ChunkData
```

### Logging & Instrumentation

- `LevelRunLog` writes JSONL records (schema v6) to persistent data path after each level
- Records include: slot-level chunk details, death events, adaptation decisions, transition pressure
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

## Branch Context

- `main` — stable branch
- `speculative-strengthening` — current feature branch for strengthening the adaptive/generation systems
