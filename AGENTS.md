# Project overview
This repository contains a final-year BSc Computer Science project built in Unity/C#. It is a 2D platformer with emotionally adaptive gameplay.

# Core academic goal
The goal is not simply to build a polished platformer. The core research goal is to investigate how gameplay difficulty and level flow can adapt in real time using player behaviour/performance proxies related to frustration, struggle, or engagement.

# Current project state
The project already contains real implemented systems rather than just a concept prototype. Key existing areas include:
- Celeste-inspired player movement/controller logic
- chunk-based procedural level generation
- a 2-step Markov-style chunk sequencing approach
- difficulty-aware chunk selection and progression logic
- adaptation concepts driven by gameplay/performance metrics
- runtime chunk/blueprint construction logic in Unity

# Important interpretation rule
Treat this project as a research-oriented adaptive gameplay system, not as a generic platformer polish project and not as a full affect-recognition/ML project.

# High-priority work
Prioritize work that strengthens:
1. adaptation logic
2. procedural generation quality and maintainability
3. player-performance metric handling
4. difficulty modelling and progression
5. system clarity, architecture, and dissertation relevance
6. logging, instrumentation, and evaluability where useful

# Lower-priority work
Do not prioritize cosmetic polish, visual flair, large-scale rewrites, or extra mechanics unless explicitly requested and clearly justified.

# Existing technical direction
- Unity + C#
- 2D platformer
- chunk-based content structure
- Markov-influenced sequencing / transition logic
- heuristic adaptation based on player behaviour/performance proxies
- real-time feasibility is important

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
- whether the change improves dissertation relevance, maintainability, or adaptive behaviour