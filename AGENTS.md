# Project overview
This repository contains a final-year BSc Computer Science project built in Unity/C#. It is a 2D precision platformer investigating behaviour-informed adaptive procedural content generation.

# Core academic goal
The goal is not simply to build a polished platformer. The strongest dissertation framing is:

> Behaviour-informed adaptive procedural content generation for a 2D precision platformer.

The core research goal is to investigate whether constrained runtime PCG can produce playable, varied, and adaptively challenging platformer levels using lightweight player behaviour/performance proxies. The system should be framed as behaviour-informed or emotion-responsive through proxies, not as direct emotion detection or a full affect-recognition/ML system.

# Current project state
The project already contains real implemented systems rather than just a concept prototype. Key existing areas include:
- Celeste-inspired player movement/controller logic
- chunk-based procedural level generation
- a 2-step Markov-style chunk sequencing approach
- difficulty-aware chunk selection and progression logic
- transition pressure logic for local sequencing quality
- evidence-based adaptation driven by gameplay/performance and behavioural proxy metrics
- behavioural signal tracking such as hesitation, momentum, reversals, retry delay, and death clustering
- runtime symbolic chunk/blueprint generation, validation, and construction logic in Unity
- generated blueprint candidates integrated into the sequencing candidate pool
- JSONL runtime logging, Python analyzer tooling, and evaluation report generation
- UI support for starting difficulty and advanced/casual stats display

# Important interpretation rule
Treat this project as a research-oriented adaptive PCG system, not as a generic platformer polish project and not as a full affect-recognition/ML project.

The three main technical pillars are:
1. constrained chunk-based PCG and sequencing quality
2. behaviour-informed adaptive difficulty control
3. validated symbolic blueprint chunk generation as a controlled PCG extension

The strongest evaluation story should prioritize:
1. current constrained generator vs naive/random generation, to demonstrate the value of PCG/sequencing logic
2. adaptive ON vs adaptive OFF, to demonstrate that the controller changes target trajectory without overclaiming player-experience improvement
3. blueprint isolation/runtime evidence, to demonstrate controlled variety and validation
4. player notes as qualitative supporting evidence, not the only proof

# High-priority work
Prioritize work that strengthens:
1. procedural generation quality, sequencing logic, and maintainability
2. evaluation evidence for constrained PCG vs naive/random generation
3. player-performance metric handling
4. difficulty modelling and progression
5. system clarity, architecture, and dissertation relevance
6. logging, instrumentation, and evaluability where useful
7. small, low-risk fixes that make the final submission more defensible

# Lower-priority work
Do not prioritize cosmetic polish, visual flair, large-scale rewrites, or extra mechanics unless explicitly requested and clearly justified.

Near submission, avoid risky new systems. Prefer evidence generation, bug fixes, report/video support, controlled comparisons, screenshots, tables, and reproducible logs.

# Existing technical direction
- Unity + C#
- 2D platformer
- chunk-based content structure
- Markov-influenced sequencing / transition logic
- heuristic adaptation based on player behaviour/performance proxies
- symbolic blueprint candidates for selected safe/gap/precision families
- runtime logs and report/analyzer tooling used as dissertation evidence
- real-time feasibility is important

# Dissertation framing rules
- Do not claim the system directly detects emotion. Say it uses behavioural proxies for strain, confidence, flow disruption, or engagement.
- Do not treat `actualLevelDifficultyScore` as objective difficulty. Treat it as a structural estimate of delivered content.
- Explain difficulty using three layers: controller intent, delivered structural content, and player outcome.
- PCG is a central contribution, not background context.
- Blueprint generation is a controlled extension of the chunk system, not a replacement for handcrafted design.
- Strong claims require logged evidence, analyzer output, calibration/report tables, screenshots, or clear player-note triangulation.

# Working rules
Before making major code changes:
1. inspect the current implementation
2. explain how the relevant subsystem currently works
3. identify the files to change
4. propose a minimal implementation plan
5. then make the changes

# Change style
- prefer small, reviewable diffs
- avoid broad rewrites unless explicitly requested
- preserve working systems where possible
- avoid unnecessary Unity asset churn or scene/prefab disruption

# Verification expectations
When changing code, explain:
- how the change should be tested in Unity
- likely side effects or regression risks
- whether the change improves dissertation relevance, maintainability, adaptive behaviour, or PCG evaluation evidence
