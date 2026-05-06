# Behaviour-Informed Adaptive PCG Platformer

This Unity project is a final-year Computer Science implementation of a 2D precision platformer with behaviour-informed adaptive procedural content generation.

The project is not intended to be a full commercial platformer or a direct emotion-recognition system. Its main technical focus is a constrained runtime PCG pipeline that generates playable platformer levels, adapts future target difficulty from player-performance evidence, and records logs that make the generator and controller decisions inspectable.

## Unity Version

Developed and tested with:

- Unity `6000.3.6f1`

Using this version is recommended when opening the project for marking.

## Running the Project

1. Open the project folder in Unity Hub.
2. Use Unity `6000.3.6f1`.
3. Open `Assets/Scenes/ProcGenTest.unity`.
4. Press Play.

`Assets/Scenes/Sandbox.unity` is an older player-controller test scene and is not the main project scene.

## Controls

- Move: `A` / `D` or left/right arrow keys
- Jump: `Space`
- Dash: `Left Shift`
- Continue after completing a level: `Enter` / `Return`

## Tips
- You can wall slide and wall jump (for both you have to hold direction key towards target wall)

## Menu Options

The start menu provides:

- Start: begins the generated level sequence.
- How to Play: shows the basic controls.
- Options: allows the marker/player to choose a starting difficulty and toggle advanced runtime statistics.

When advanced statistics are disabled, the game shows a cleaner casual play view. When enabled, additional generator/adaptation evidence is shown on screen for inspection.

## Main Implemented Systems

The project includes:

- chunk-based procedural level generation
- constrained sequencing with difficulty matching, transition pressure, structural budgets, and lookahead planning
- Markov-style transition weighting with bounded pressure-aware learning
- an adaptive difficulty controller using deaths, completion evidence, and behavioural proxy metrics
- behavioural tracking for hesitation, momentum, reversals, retry delay, and death clustering
- symbolic blueprint chunk generation for selected safe/rest, gap, precision, and controlled hazard-accent gap variants
- runtime JSONL logging and Python analysis tooling
- a constrained-vs-naive generation mode used for dissertation evaluation

Generated vertical and full spike blueprint families are intentionally disabled in the final system because the final implementation prioritises controlled, validated, defensible variation over unrestricted generation.

## Useful Files

- `Assets/Scripts/LevelGenerator.cs`: procedural sequencing, candidate scoring, generation modes, structural budgets, and lookahead planning
- `Assets/Scripts/AdaptiveDifficultyController.cs`: adaptive target difficulty decisions
- `Assets/Scripts/PlayerBehaviourTracker.cs`: behavioural proxy tracking
- `Assets/Scripts/MarkovWeightTable.cs`: transition weighting and bounded learning
- `Assets/Scripts/ChunkTransitionPressure.cs`: transition-pressure metadata and known risky pair handling
- `Assets/Scripts/ChunkBlueprint*.cs`: symbolic blueprint representation, validation, and runtime construction
- `Assets/Scripts/LevelRunLog.cs`: JSONL runtime logging structure
- `tools/`: Python analysis and calibration utilities used during evaluation

## Evaluation Evidence

The dissertation report and appendix summarise the final evaluation evidence. The project itself can still generate fresh JSONL runtime logs during play, but the submitted Unity project does not require old development batches to run.

The strongest final evaluation comparison is between the constrained generator and the NaiveRandom baseline, supported by adaptive constrained runs and blueprint-generation evidence.
