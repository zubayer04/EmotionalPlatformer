using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ChunkCalibrationReporterWindow : EditorWindow
{
    private const int SampleSeed = 12345;
    private const int SequenceSampleRuns = 10;
    private const int SequenceSampleBaseSeed = SampleSeed + 1000;

    private Vector2 scroll;
    private string report = "Click Generate Report to build a read-only calibration audit.";

    [MenuItem("Emotional Platformer/Calibration Reporter")]
    public static void Open()
    {
        GetWindow<ChunkCalibrationReporterWindow>("Calibration Reporter");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Chunk Calibration Reporter", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Read-only audit for chunk metadata, generated blueprint samples, and runtime generated-candidate equivalence. No assets are saved or modified.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Report", GUILayout.Width(150f)))
                report = BuildReport();

            if (GUILayout.Button("Copy Report", GUILayout.Width(120f)))
                EditorGUIUtility.systemCopyBuffer = report;
        }

        EditorGUILayout.Space(6f);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        report = EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private static string BuildReport()
    {
        UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;

        try
        {
            LevelGenerator generator = FindSceneLevelGenerator();
            List<string> warnings = new List<string>();
            StringBuilder sb = new StringBuilder(12000);

            AppendHeader(sb, generator);
            AppendLevelGeneratorSettings(sb, generator, warnings);
            AppendDifficultyRubric(sb);

            List<ChunkRecord> chunks = CollectConfiguredChunks(generator, warnings);
            AppendHandcraftedChunkMetadata(sb, chunks);
            AppendMetadataSmellAudit(sb, chunks, warnings);
            AppendGeneratedSamples(sb, ChunkTag.Gap, "Generated Gap Samples", warnings);
            AppendGeneratedSamples(sb, ChunkTag.Precision, "Generated Precision Samples", warnings, IsReplacementAllowedBySettings(generator, ChunkTag.Precision));
            AppendReplacementEquivalence(sb, generator, chunks, warnings);
            AppendSequenceSampling(sb, generator, chunks, warnings);
            AppendWarnings(sb, warnings);

            return sb.ToString();
        }
        finally
        {
            UnityEngine.Random.state = previousRandomState;
        }
    }

    private static void AppendHeader(StringBuilder sb, LevelGenerator generator)
    {
        Scene activeScene = SceneManager.GetActiveScene();

        sb.AppendLine("# Chunk Calibration Report");
        sb.AppendLine();
        sb.AppendLine($"Generated At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Active Scene: {activeScene.name}");
        sb.AppendLine($"LevelGenerator: {(generator != null ? generator.name : "None found")}");
        sb.AppendLine($"Sample Seed: {SampleSeed}");
        sb.AppendLine("Report Mode: Editor Calibration Audit");
        sb.AppendLine("Read Only: true");
        sb.AppendLine();
    }

    private static void AppendLevelGeneratorSettings(StringBuilder sb, LevelGenerator generator, List<string> warnings)
    {
        sb.AppendLine("## Current LevelGenerator Calibration Settings");

        if (generator == null)
        {
            sb.AppendLine("No active scene LevelGenerator was found.");
            warnings.Add("No active scene LevelGenerator found. Report is limited to generated samples.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"totalChunks: {generator.totalChunks}");
        sb.AppendLine($"startingChunkPrefab: {AssetPath(generator.startingChunkPrefab)}");
        sb.AppendLine($"chunkPrefabs.Count: {(generator.chunkPrefabs != null ? generator.chunkPrefabs.Length : 0)}");

        if (generator.chunkPrefabs != null)
        {
            for (int i = 0; i < generator.chunkPrefabs.Length; i++)
                sb.AppendLine($"chunkPrefabs[{i}]: {AssetPath(generator.chunkPrefabs[i])}");
        }

        sb.AppendLine($"useTwoStepMarkov: {generator.useTwoStepMarkov}");
        sb.AppendLine($"useLookaheadSequencePlanning: {generator.useLookaheadSequencePlanning}");
        sb.AppendLine($"lookaheadDepth: {generator.lookaheadDepth}");
        sb.AppendLine($"lookaheadBeamWidth: {generator.lookaheadBeamWidth}");
        sb.AppendLine($"avoidSamePrefabBackToBack: {generator.avoidSamePrefabBackToBack}");
        sb.AppendLine($"maxSamePrimaryTagStreak: {generator.maxSamePrimaryTagStreak}");
        sb.AppendLine($"targetDifficulty: {generator.targetDifficulty:0.##}");
        sb.AppendLine($"startDifficultyBias: {generator.startDifficultyBias:0.##}");
        sb.AppendLine($"difficultyPreferenceStrength: {generator.difficultyPreferenceStrength:0.##}");
        sb.AppendLine($"varietyBonus: {generator.varietyBonus:0.##}");
        sb.AppendLine($"earlyHardPenalty: {generator.earlyHardPenalty:0.##}");
        sb.AppendLine($"useGeneratedBlueprintChunks: {generator.useGeneratedBlueprintChunks}");
        sb.AppendLine($"useGeneratedBlueprintCandidateSelection: {generator.useGeneratedBlueprintCandidateSelection}");
        sb.AppendLine($"generatedChunkReplacementChance: {generator.generatedChunkReplacementChance:0.##}");
        sb.AppendLine($"generatedCandidateVariantsPerSource: {generator.generatedCandidateVariantsPerSource}");
        sb.AppendLine($"generatedCandidateFamilyWeight: {generator.generatedCandidateFamilyWeight:0.##}");
        sb.AppendLine($"allowGeneratedGap: {generator.allowGeneratedGap}");
        sb.AppendLine($"allowGeneratedPrecision: {generator.allowGeneratedPrecision}");
        sb.AppendLine($"allowGeneratedVertical: {generator.allowGeneratedVertical}");
        sb.AppendLine($"allowGeneratedSpikes: {generator.allowGeneratedSpikes}");
        sb.AppendLine($"allowGeneratedSafeRest: {generator.allowGeneratedSafeRest}");
        sb.AppendLine($"wAvgDifficulty: {generator.wAvgDifficulty:0.##}");
        sb.AppendLine($"wHazardChunk: {generator.wHazardChunk:0.##}");
        sb.AppendLine($"wEstimatedJump: {generator.wEstimatedJump:0.##}");
        sb.AppendLine($"wVerticalChunk: {generator.wVerticalChunk:0.##}");
        sb.AppendLine($"clampMaxScore: {generator.clampMaxScore:0.##}");
        sb.AppendLine();
    }

    private static void AppendHandcraftedChunkMetadata(StringBuilder sb, List<ChunkRecord> chunks)
    {
        sb.AppendLine("## Handcrafted Chunk Metadata");
        sb.AppendLine("Role | Path | Name | Primary | Tags | Difficulty | Hazard | Jumps | ExitDelta | Generated Candidate Eligible");
        sb.AppendLine("--- | --- | --- | --- | --- | --- | --- | --- | --- | ---");

        if (chunks.Count == 0)
        {
            sb.AppendLine("No configured chunk prefabs found.");
            sb.AppendLine();
            return;
        }

        foreach (ChunkRecord chunk in chunks)
        {
            sb.AppendLine(
                $"{chunk.role} | {chunk.assetPath} | {chunk.name} | {chunk.primaryTag} | {chunk.secondaryTags} | " +
                $"{chunk.difficultyRating} | {chunk.hasHazard} | {chunk.estimatedJumps} | " +
                $"({chunk.exitDelta.x:0.##}, {chunk.exitDelta.y:0.##}) | {chunk.replacementEligible}");
        }

        sb.AppendLine();
    }

    private static void AppendDifficultyRubric(StringBuilder sb)
    {
        sb.AppendLine("## Difficulty Rubric");
        sb.AppendLine("Rating | Heuristic Meaning");
        sb.AppendLine("--- | ---");
        sb.AppendLine("1 | Rest/safe traversal, flat ground, no required jump, no hazard");
        sb.AppendLine("2 | Basic traversal challenge: simple gap, stairs, basic vertical movement");
        sb.AppendLine("3 | Medium gap or slightly tighter platforming; still low punishment");
        sb.AppendLine("4 | Hard gap, basic precision, static spike chunk, moderate retry risk");
        sb.AppendLine("5 | Dash/precision challenge, longer movement commitment, or mixed traversal");
        sb.AppendLine("6 | Moving hazard or sustained timing challenge");
        sb.AppendLine("7 | Hard moving/vertical hazard combination, high execution demand");
        sb.AppendLine("8+ | High-risk hybrid such as spike plus dash/gap, intentionally severe");
        sb.AppendLine();
    }

    private static void AppendMetadataSmellAudit(StringBuilder sb, List<ChunkRecord> chunks, List<string> warnings)
    {
        sb.AppendLine("## Metadata Smell Audit");
        sb.AppendLine("Chunk | Review Notes");
        sb.AppendLine("--- | ---");

        int reviewCount = 0;
        foreach (ChunkRecord chunk in chunks)
        {
            List<string> notes = CollectMetadataSmells(chunk);
            if (notes.Count == 0)
                continue;

            reviewCount++;
            string joined = string.Join("; ", notes);
            sb.AppendLine($"{chunk.name} | {joined}");
            warnings.Add($"Metadata review: {chunk.name}: {joined}.");
        }

        if (reviewCount == 0)
            sb.AppendLine("No metadata review notes generated by the current heuristic checks.");

        sb.AppendLine();
    }

    private static void AppendGeneratedSamples(StringBuilder sb, ChunkTag tag, string title, List<string> warnings, bool replacementActive = true)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine("RequestedDifficulty | Blueprint | GeneratedDifficulty | Width | Height | Tags | Hazard | Jumps | GapCount | MaxGapWidth | LandingWidth | Validation | Rows");
        sb.AppendLine("--- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | ---");

        for (int difficulty = 1; difficulty <= 10; difficulty++)
        {
            ChunkGenerationRequest request = new ChunkGenerationRequest
            {
                requestedPrimaryTag = tag,
                targetDifficulty = difficulty,
                requireHazard = false,
                preferredWidth = GetPreferredWidthForTag(tag),
                preferredHeight = GetPreferredHeightForTag(tag)
            };

            ChunkBlueprint blueprint = GenerateDeterministicSample(request, difficulty);
            if (blueprint == null)
            {
                sb.AppendLine($"{difficulty} | <null> | - | - | - | - | - | - | - | - | - | Invalid | -");
                warnings.Add($"{title}: generator returned null for requested difficulty {difficulty}.");
                continue;
            }

            ChunkBlueprintValidationResult validation = ChunkBlueprintValidator.Validate(blueprint);
            ChunkBlueprintFeatures features = ChunkBlueprintFeatureExtractor.Analyze(blueprint);
            string validationText = validation.isValid ? "Valid" : "Invalid";

            if (!validation.isValid)
                warnings.Add($"{title}: {blueprint.chunkName} at requested difficulty {difficulty} is invalid.");

            sb.AppendLine(
                $"{difficulty} | {blueprint.chunkName} | {blueprint.difficultyRating} | {blueprint.width} | {blueprint.height} | " +
                $"{TagsToText(blueprint.tags)} | {blueprint.hasHazard} | {blueprint.estimatedJumps} | " +
                $"{features.gapCount} | {features.maxGapWidth} | {features.minLandingWidth} | {validationText} | {RowsToInlineText(blueprint.rows)}");
        }

        if (tag == ChunkTag.Precision)
        {
            sb.AppendLine();
            sb.AppendLine("_Note: these are source-free precision blueprint samples. Runtime precision candidate selection is source-restricted and is checked in the Potential Generated Candidate Equivalence section, so template difficulty clamping here is informational rather than a runtime failure._");
        }

        sb.AppendLine();
    }

    private static void AppendReplacementEquivalence(
        StringBuilder sb,
        LevelGenerator generator,
        List<ChunkRecord> chunks,
        List<string> warnings)
    {
        sb.AppendLine("## Potential Generated Candidate Equivalence");
        sb.AppendLine("Source | SourceTag | SourceDiff | SourceJumps | SourceHazard | SourceExitDelta | SourceMaxGap | Generated | GeneratedDiff | GeneratedJumps | GeneratedHazard | GeneratedWidth | GeneratedEstimatedExitDelta | GapCount | MaxGapWidth | LandingWidth | DiffDelta | Status | Reason");
        sb.AppendLine("--- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | ---");
        int replacementReviewCount = 0;

        if (generator == null)
        {
            sb.AppendLine("No active scene LevelGenerator found.");
            sb.AppendLine();
            return;
        }

        foreach (ChunkRecord source in chunks)
        {
            if (!source.replacementEligible)
                continue;

            bool allowed = IsReplacementAllowedBySettings(generator, source.primaryTag);
            if (!allowed)
                continue;

            if (!IsRuntimeGeneratedCandidateEligible(source))
            {
                sb.AppendLine(
                    $"{source.name} | {source.primaryTag} | {source.difficultyRating} | {source.estimatedJumps} | {source.hasHazard} | " +
                    $"({source.exitDelta.x:0.##}, {source.exitDelta.y:0.##}) | {source.maxGapWidth} | - | - | - | - | - | - | - | - | - | - | Not Runtime Eligible | {GetRuntimeEligibilityReason(source)}");
                continue;
            }

            ChunkGenerationRequest request = new ChunkGenerationRequest
            {
                requestedPrimaryTag = source.primaryTag,
                targetDifficulty = source.difficultyRating,
                requireHazard = source.hasHazard,
                preferredWidth = GetPreferredWidthForTag(source.primaryTag),
                preferredHeight = GetPreferredHeightForTag(source.primaryTag),
                hasSourceContext = true,
                sourceChunkName = source.name,
                sourceDifficulty = source.difficultyRating,
                sourceHasHazard = source.hasHazard,
                sourceEstimatedJumps = source.estimatedJumps,
                sourceExitDelta = source.exitDelta,
                sourceMaxGapWidth = source.maxGapWidth
            };

            ChunkBlueprint generated = GenerateDeterministicSample(request, source.difficultyRating + source.name.GetHashCode());
            if (generated == null)
            {
                sb.AppendLine($"{source.name} | {source.primaryTag} | {source.difficultyRating} | {source.estimatedJumps} | {source.hasHazard} | ({source.exitDelta.x:0.##}, {source.exitDelta.y:0.##}) | <null> | - | - | - | - | - | Mismatch | Generator returned null");
                warnings.Add($"Generated candidate equivalence: generator returned null for {source.name}.");
                continue;
            }

            int difficultyDelta = generated.difficultyRating - source.difficultyRating;
            int jumpsDelta = generated.estimatedJumps - source.estimatedJumps;
            bool hazardMatch = generated.hasHazard == source.hasHazard;
            bool tagMatch = generated.primaryTag == source.primaryTag;
            ChunkBlueprintFeatures features = ChunkBlueprintFeatureExtractor.Analyze(generated);
            Vector2 estimatedExitDelta = features.estimatedExitDelta;
            Vector2 estimatedExitDeltaDelta = estimatedExitDelta - source.exitDelta;

            string status = "Equivalent";
            List<string> reasons = new List<string>();

            if (!tagMatch)
                reasons.Add("primary tag differs");
            if (difficultyDelta != 0 && !IsGeneratedSafeRestDifficultyEquivalent(source, generated))
                reasons.Add($"difficulty delta {difficultyDelta:+#;-#;0}");
            if (jumpsDelta != 0)
                reasons.Add($"jumps delta {jumpsDelta:+#;-#;0}");
            if (!hazardMatch)
                reasons.Add("hazard flag differs");
            if (source.maxGapWidth > 0 && generated.primaryTag == ChunkTag.Gap && features.maxGapWidth != source.maxGapWidth)
                reasons.Add($"max gap delta {features.maxGapWidth - source.maxGapWidth:+#;-#;0}");
            float verticalExitDeltaTolerance = Mathf.Max(
                GetGeneratedGapVerticalExitDeltaTolerance(source, generated),
                Mathf.Max(
                    GetGeneratedSafeVerticalExitDeltaTolerance(source, generated),
                    GetGeneratedPrecisionVerticalExitDeltaTolerance(source, generated)));
            if (Mathf.Abs(estimatedExitDeltaDelta.x) > 1.25f || Mathf.Abs(estimatedExitDeltaDelta.y) > verticalExitDeltaTolerance)
                reasons.Add($"estimated exit delta differs by ({estimatedExitDeltaDelta.x:+0.##;-0.##;0}, {estimatedExitDeltaDelta.y:+0.##;-0.##;0})");

            if (reasons.Count > 0)
                status = reasons.Count == 1 ? "Close" : "Needs Review";

            string reason = reasons.Count > 0 ? string.Join("; ", reasons) : "metadata matches";
            if (status != "Equivalent")
            {
                replacementReviewCount++;
                warnings.Add($"Generated candidate equivalence: {source.name} -> {generated.chunkName}: {reason}.");
            }

            sb.AppendLine(
                $"{source.name} | {source.primaryTag} | {source.difficultyRating} | {source.estimatedJumps} | {source.hasHazard} | " +
                $"({source.exitDelta.x:0.##}, {source.exitDelta.y:0.##}) | {source.maxGapWidth} | {generated.chunkName} | {generated.difficultyRating} | " +
                $"{generated.estimatedJumps} | {generated.hasHazard} | {generated.width} | ({estimatedExitDelta.x:0.##}, {estimatedExitDelta.y:0.##}) | " +
                $"{features.gapCount} | {features.maxGapWidth} | {features.minLandingWidth} | {difficultyDelta:+#;-#;0} | {status} | {reason}");
        }

        sb.AppendLine();
        sb.AppendLine($"Generated Candidate Review Count: {replacementReviewCount}");
        sb.AppendLine();
    }

    private static void AppendSequenceSampling(
        StringBuilder sb,
        LevelGenerator generator,
        List<ChunkRecord> chunks,
        List<string> warnings)
    {
        sb.AppendLine("## Sequence Sampling");

        if (generator == null)
        {
            sb.AppendLine("No active scene LevelGenerator found.");
            sb.AppendLine();
            warnings.Add("Sequence sampling skipped because no active scene LevelGenerator was found.");
            return;
        }

        MethodInfo buildFreshSequence = typeof(LevelGenerator).GetMethod("BuildFreshSequence", BindingFlags.Instance | BindingFlags.NonPublic);
        if (buildFreshSequence == null)
        {
            sb.AppendLine("BuildFreshSequence() could not be reflected.");
            sb.AppendLine();
            warnings.Add("Sequence sampling skipped because BuildFreshSequence() could not be reflected.");
            return;
        }

        bool hasFixedStartingChunk = generator.startingChunkPrefab != null;
        int expectedGeneratedSlots = Mathf.Max(0, generator.totalChunks - (hasFixedStartingChunk ? 1 : 0));
        List<SequenceSlotSample> slotSamples = new List<SequenceSlotSample>(SequenceSampleRuns * Mathf.Max(1, expectedGeneratedSlots));
        Dictionary<string, int> chunkUsage = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, int> tagUsage = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int runIndex = 0; runIndex < SequenceSampleRuns; runIndex++)
        {
            int seed = SequenceSampleBaseSeed + runIndex;
            UnityEngine.Random.InitState(seed);

            IList sequence = null;
            try
            {
                sequence = buildFreshSequence.Invoke(generator, null) as IList;
            }
            catch (Exception ex)
            {
                warnings.Add($"Sequence sampling run {runIndex + 1} could not invoke BuildFreshSequence(): {ex.GetBaseException().Message}.");
            }

            if (sequence == null)
            {
                warnings.Add($"Sequence sampling run {runIndex + 1} returned a null sequence.");
                continue;
            }

            int startOffset = 0;
            if (hasFixedStartingChunk && sequence.Count > 0 && GetCandidateSourcePrefab(sequence[0]) == generator.startingChunkPrefab)
            {
                startOffset = 1;
            }
            else if (hasFixedStartingChunk)
            {
                warnings.Add($"Sequence sampling run {runIndex + 1} did not begin with the configured starting chunk; review sequence assumptions.");
            }

            int generatedSlots = Mathf.Max(0, sequence.Count - startOffset);
            if (generatedSlots < expectedGeneratedSlots)
                warnings.Add($"Sequence sampling run {runIndex + 1} returned {generatedSlots} generated slots instead of the expected {expectedGeneratedSlots}; review pool sufficiency or hard constraints.");

            for (int i = startOffset; i < sequence.Count; i++)
            {
                int generatedSlotIndex = i - startOffset;
                object candidate = sequence[i];
                GameObject prefab = GetCandidateSourcePrefab(candidate);
                float slotTargetDifficulty = GetSlotTargetDifficulty(generator, generatedSlotIndex);
                int selectedDifficulty = GetCandidateDifficulty(candidate);
                string selectedChunkName = GetCandidateDisplayName(candidate);
                ChunkTag selectedTag = GetCandidatePrimaryTag(candidate);
                string selectedPrimaryTag = selectedTag.ToString();
                float delta = selectedDifficulty >= 0 ? selectedDifficulty - slotTargetDifficulty : 0f;
                object previousCandidate = i > 0 ? sequence[i - 1] : null;
                string previousChunkName = GetCandidateDisplayName(previousCandidate);
                ChunkTag previousTag = GetCandidatePrimaryTag(previousCandidate);
                float transitionPressureMultiplier = 1f;
                string transitionPressureReason = "none";

                if (candidate != null && previousCandidate != null)
                {
                    transitionPressureMultiplier = ChunkTransitionPressure.GetSelectionWeightMultiplier(
                        previousChunkName,
                        previousTag,
                        selectedChunkName,
                        selectedTag,
                        selectedDifficulty,
                        slotTargetDifficulty,
                        generator.targetDifficulty);

                    transitionPressureReason = ChunkTransitionPressure.GetTransitionReason(
                        previousChunkName,
                        previousTag,
                        selectedChunkName,
                        selectedTag,
                        selectedDifficulty,
                        slotTargetDifficulty,
                        generator.targetDifficulty);
                }

                slotSamples.Add(new SequenceSlotSample
                {
                    runIndex = runIndex + 1,
                    seed = seed,
                    slotIndex = generatedSlotIndex + 1,
                    slotTargetDifficulty = slotTargetDifficulty,
                    previousChunkName = previousChunkName,
                    selectedChunkName = selectedChunkName,
                    selectedPrimaryTag = selectedPrimaryTag,
                    selectedDifficulty = selectedDifficulty,
                    deltaFromSlotTarget = delta,
                    transitionPressureMultiplier = transitionPressureMultiplier,
                    transitionPressureReason = transitionPressureReason
                });

                if (prefab == null)
                    continue;

                IncrementCount(chunkUsage, selectedChunkName);
                IncrementCount(tagUsage, selectedPrimaryTag);
            }
        }

        AppendSequenceSamplingConfig(sb, generator, expectedGeneratedSlots);
        AppendSequenceRunDetails(sb, slotSamples);
        AppendSequenceSlotSummary(sb, slotSamples);
        AppendSequenceProgressionSummary(sb, slotSamples, warnings);
        AppendSequenceTransitionPressureSummary(sb, slotSamples);
        AppendSequenceUsageSummary(sb, slotSamples, chunkUsage, tagUsage, chunks);
        AppendPoolSufficiencySnapshot(sb, slotSamples, chunkUsage, chunks, generator, warnings);
    }

    private static void AppendSequenceSamplingConfig(StringBuilder sb, LevelGenerator generator, int expectedGeneratedSlots)
    {
        sb.AppendLine("### Sequence Sampling Config");
        sb.AppendLine($"sampleRuns: {SequenceSampleRuns}");
        sb.AppendLine($"baseSeed: {SequenceSampleBaseSeed}");
        sb.AppendLine($"totalChunks: {generator.totalChunks}");
        sb.AppendLine($"expectedGeneratedSlotsPerRun: {expectedGeneratedSlots}");
        sb.AppendLine($"startingChunkPrefab: {AssetPath(generator.startingChunkPrefab)}");
        sb.AppendLine("slotTargetComparisonExcludesFixedStartingChunk: true");
        sb.AppendLine($"sequenceSamplingIncludesGeneratedBlueprintCandidates: {generator.useGeneratedBlueprintCandidateSelection}");
        sb.AppendLine();
    }

    private static void AppendSequenceRunDetails(StringBuilder sb, List<SequenceSlotSample> slotSamples)
    {
        sb.AppendLine("### Per-Run Slot Detail");
        sb.AppendLine("Run | Seed | SlotIndex | SlotTargetDifficulty | PreviousChunk | SelectedChunk | SelectedPrimaryTag | SelectedDifficulty | DeltaFromSlotTarget | PressureMultiplier | PressureReason");
        sb.AppendLine("--- | --- | --- | --- | --- | --- | --- | --- | --- | --- | ---");

        if (slotSamples.Count == 0)
        {
            sb.AppendLine("No sequence slot samples were collected.");
            sb.AppendLine();
            return;
        }

        for (int i = 0; i < slotSamples.Count; i++)
        {
            SequenceSlotSample sample = slotSamples[i];
            sb.AppendLine(
                $"{sample.runIndex} | {sample.seed} | {sample.slotIndex} | {sample.slotTargetDifficulty:0.##} | {sample.previousChunkName} | {sample.selectedChunkName} | " +
                $"{sample.selectedPrimaryTag} | {sample.selectedDifficulty} | {sample.deltaFromSlotTarget:+0.##;-0.##;0} | " +
                $"{sample.transitionPressureMultiplier:0.##} | {sample.transitionPressureReason}");
        }

        sb.AppendLine();
    }

    private static void AppendSequenceSlotSummary(StringBuilder sb, List<SequenceSlotSample> slotSamples)
    {
        sb.AppendLine("### Per-Slot Summary");
        sb.AppendLine("SlotIndex | AvgSlotTarget | AvgSelectedDifficulty | AvgDelta | AvgAbsDelta | UniqueChunks | MostCommonChunk");
        sb.AppendLine("--- | --- | --- | --- | --- | --- | ---");

        Dictionary<int, SequenceSlotAggregate> aggregates = BuildSlotAggregates(slotSamples);
        if (aggregates.Count == 0)
        {
            sb.AppendLine("No per-slot summary is available.");
            sb.AppendLine();
            return;
        }

        List<int> slotIndices = new List<int>(aggregates.Keys);
        slotIndices.Sort();

        for (int i = 0; i < slotIndices.Count; i++)
        {
            int slotIndex = slotIndices[i];
            SequenceSlotAggregate aggregate = aggregates[slotIndex];
            string mostCommonChunk = GetMostCommonKey(aggregate.chunkCounts);
            float count = Mathf.Max(1, aggregate.count);

            sb.AppendLine(
                $"{slotIndex} | {aggregate.sumTarget / count:0.##} | {aggregate.sumSelectedDifficulty / count:0.##} | " +
                $"{aggregate.sumDelta / count:+0.##;-0.##;0} | {aggregate.sumAbsDelta / count:0.##} | " +
                $"{aggregate.chunkCounts.Count} | {mostCommonChunk}");
        }

        sb.AppendLine();
    }

    private static void AppendSequenceProgressionSummary(StringBuilder sb, List<SequenceSlotSample> slotSamples, List<string> warnings)
    {
        sb.AppendLine("### Progression Summary");

        if (slotSamples.Count == 0)
        {
            sb.AppendLine("No progression summary is available.");
            sb.AppendLine();
            return;
        }

        int maxSlotIndex = GetMaxSlotIndex(slotSamples);
        float firstSum = 0f;
        float middleSum = 0f;
        float lastSum = 0f;
        int firstCount = 0;
        int middleCount = 0;
        int lastCount = 0;
        float totalAbsDelta = 0f;

        Dictionary<int, SequenceRunAggregate> runAggregates = new Dictionary<int, SequenceRunAggregate>();

        for (int i = 0; i < slotSamples.Count; i++)
        {
            SequenceSlotSample sample = slotSamples[i];
            SequenceRegion region = GetSequenceRegion(sample.slotIndex, maxSlotIndex);

            switch (region)
            {
                case SequenceRegion.First:
                    firstSum += sample.selectedDifficulty;
                    firstCount++;
                    break;
                case SequenceRegion.Middle:
                    middleSum += sample.selectedDifficulty;
                    middleCount++;
                    break;
                case SequenceRegion.Last:
                    lastSum += sample.selectedDifficulty;
                    lastCount++;
                    break;
            }

            totalAbsDelta += Mathf.Abs(sample.deltaFromSlotTarget);

            if (!runAggregates.TryGetValue(sample.runIndex, out SequenceRunAggregate runAggregate))
            {
                runAggregate = new SequenceRunAggregate();
                runAggregates.Add(sample.runIndex, runAggregate);
            }

            if (region == SequenceRegion.First)
            {
                runAggregate.firstSelectedSum += sample.selectedDifficulty;
                runAggregate.firstCount++;
            }
            else if (region == SequenceRegion.Last)
            {
                runAggregate.lastSelectedSum += sample.selectedDifficulty;
                runAggregate.lastCount++;
            }
        }

        float firstAvg = firstCount > 0 ? firstSum / firstCount : 0f;
        float middleAvg = middleCount > 0 ? middleSum / middleCount : 0f;
        float lastAvg = lastCount > 0 ? lastSum / lastCount : 0f;
        float overallAvgAbsDelta = totalAbsDelta / Mathf.Max(1, slotSamples.Count);

        int runsWhereLastHarder = 0;
        foreach (KeyValuePair<int, SequenceRunAggregate> pair in runAggregates)
        {
            SequenceRunAggregate aggregate = pair.Value;
            if (aggregate.firstCount == 0 || aggregate.lastCount == 0)
                continue;

            float runFirstAvg = aggregate.firstSelectedSum / aggregate.firstCount;
            float runLastAvg = aggregate.lastSelectedSum / aggregate.lastCount;
            if (runLastAvg > runFirstAvg)
                runsWhereLastHarder++;
        }

        sb.AppendLine($"firstThirdAvgSelectedDifficulty: {firstAvg:0.##}");
        sb.AppendLine($"middleThirdAvgSelectedDifficulty: {middleAvg:0.##}");
        sb.AppendLine($"lastThirdAvgSelectedDifficulty: {lastAvg:0.##}");
        sb.AppendLine($"lastMinusFirst: {lastAvg - firstAvg:+0.##;-0.##;0}");
        sb.AppendLine($"runsWhereLastThirdHarderThanFirstThird: {runsWhereLastHarder}/{SequenceSampleRuns}");
        sb.AppendLine($"overallAvgAbsDeltaFromSlotTarget: {overallAvgAbsDelta:0.##}");

        List<string> notes = new List<string>();
        if (lastAvg <= firstAvg)
            notes.Add("later slots are not clearly harder on average; review progression settings");
        if (overallAvgAbsDelta > 1f)
            notes.Add("selected chunk difficulty is drifting from slot target; review sequencing pressure");

        if (notes.Count == 0)
        {
            sb.AppendLine("reviewNotes: current sampled progression looks broadly aligned with the slot ramp.");
        }
        else
        {
            sb.AppendLine($"reviewNotes: {string.Join("; ", notes)}");
            for (int i = 0; i < notes.Count; i++)
                warnings.Add($"Sequence sampling: {notes[i]}.");
        }

        sb.AppendLine();
    }

    private static void AppendSequenceTransitionPressureSummary(StringBuilder sb, List<SequenceSlotSample> slotSamples)
    {
        sb.AppendLine("### Transition Pressure Summary");

        if (slotSamples.Count == 0)
        {
            sb.AppendLine("No transition pressure summary is available.");
            sb.AppendLine();
            return;
        }

        int penalizedSelectedTransitions = 0;
        Dictionary<string, int> reasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < slotSamples.Count; i++)
        {
            SequenceSlotSample sample = slotSamples[i];
            if (sample.transitionPressureMultiplier >= 0.999f)
                continue;

            penalizedSelectedTransitions++;
            IncrementCount(reasonCounts, sample.transitionPressureReason);
        }

        sb.AppendLine($"selectedTransitionsWithPressurePenalty: {penalizedSelectedTransitions}/{slotSamples.Count}");

        if (reasonCounts.Count == 0)
        {
            sb.AppendLine("pressurePenaltyReasons: none selected in current sample");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("PressureReason | Count");
        sb.AppendLine("--- | ---");

        List<string> reasons = new List<string>(reasonCounts.Keys);
        reasons.Sort(StringComparer.Ordinal);
        for (int i = 0; i < reasons.Count; i++)
        {
            string reason = reasons[i];
            sb.AppendLine($"{reason} | {reasonCounts[reason]}");
        }

        sb.AppendLine();
    }

    private static void AppendSequenceUsageSummary(
        StringBuilder sb,
        List<SequenceSlotSample> slotSamples,
        Dictionary<string, int> chunkUsage,
        Dictionary<string, int> tagUsage,
        List<ChunkRecord> chunks)
    {
        sb.AppendLine("### Usage Summary");

        int totalSelections = slotSamples.Count;
        List<KeyValuePair<string, int>> chunkEntries = SortCountsDescending(chunkUsage);
        List<KeyValuePair<string, int>> tagEntries = SortCountsDescending(tagUsage);
        int configuredSelectableChunks = CountConfiguredSelectableChunks(chunks);

        sb.AppendLine("#### By Chunk");
        sb.AppendLine("Chunk | SelectionCount | SelectionPercent");
        sb.AppendLine("--- | --- | ---");
        if (chunkEntries.Count == 0)
        {
            sb.AppendLine("No chunk usage samples collected.");
        }
        else
        {
            for (int i = 0; i < chunkEntries.Count; i++)
            {
                KeyValuePair<string, int> entry = chunkEntries[i];
                float percent = totalSelections > 0 ? (entry.Value * 100f) / totalSelections : 0f;
                sb.AppendLine($"{entry.Key} | {entry.Value} | {percent:0.##}%");
            }
        }

        sb.AppendLine();
        sb.AppendLine("#### By Primary Tag");
        sb.AppendLine("PrimaryTag | SelectionCount | SelectionPercent");
        sb.AppendLine("--- | --- | ---");
        if (tagEntries.Count == 0)
        {
            sb.AppendLine("No primary-tag usage samples collected.");
        }
        else
        {
            for (int i = 0; i < tagEntries.Count; i++)
            {
                KeyValuePair<string, int> entry = tagEntries[i];
                float percent = totalSelections > 0 ? (entry.Value * 100f) / totalSelections : 0f;
                sb.AppendLine($"{entry.Key} | {entry.Value} | {percent:0.##}%");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"uniqueConfiguredChunks: {configuredSelectableChunks}");
        sb.AppendLine($"uniqueSelectedChunks: {chunkUsage.Count}");
        sb.AppendLine($"neverSelectedConfiguredChunks: {GetNeverSelectedChunks(chunks, chunkUsage).Count}");
        sb.AppendLine();
    }

    private static void AppendPoolSufficiencySnapshot(
        StringBuilder sb,
        List<SequenceSlotSample> slotSamples,
        Dictionary<string, int> chunkUsage,
        List<ChunkRecord> chunks,
        LevelGenerator generator,
        List<string> warnings)
    {
        sb.AppendLine("### Pool Sufficiency Snapshot");

        List<ChunkRecord> configuredSelectableChunks = GetConfiguredSelectableChunks(chunks);
        Dictionary<string, int> configuredByTag = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<int, int> configuredByDifficulty = new Dictionary<int, int>();

        for (int i = 0; i < configuredSelectableChunks.Count; i++)
        {
            ChunkRecord chunk = configuredSelectableChunks[i];
            IncrementCount(configuredByTag, chunk.primaryTag.ToString());
            IncrementCount(configuredByDifficulty, chunk.difficultyRating);
        }

        sb.AppendLine("configuredChunkCountByPrimaryTag:");
        AppendIndentedCounts(sb, SortCountsDescending(configuredByTag));
        sb.AppendLine("configuredChunkCountByDifficulty:");
        AppendIndentedCounts(sb, SortIntegerCountsAscending(configuredByDifficulty));

        List<string> neverSelected = GetNeverSelectedChunks(chunks, chunkUsage);
        float totalSelections = Mathf.Max(1, slotSamples.Count);
        List<KeyValuePair<string, int>> chunkEntries = SortCountsDescending(chunkUsage);
        float top3Share = 0f;
        for (int i = 0; i < chunkEntries.Count && i < 3; i++)
            top3Share += chunkEntries[i].Value;
        top3Share = (top3Share * 100f) / totalSelections;

        List<string> familyNotes = new List<string>();
        Dictionary<string, int> configuredFamilyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> observedFamilies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        for (int i = 0; i < configuredSelectableChunks.Count; i++)
        {
            string family = configuredSelectableChunks[i].primaryTag.ToString();
            IncrementCount(configuredFamilyCounts, family);
            if (!observedFamilies.ContainsKey(family))
                observedFamilies.Add(family, new HashSet<string>(StringComparer.Ordinal));
        }

        foreach (KeyValuePair<string, int> pair in chunkUsage)
        {
            ChunkRecord configuredChunk = FindChunkByName(configuredSelectableChunks, pair.Key);
            if (string.IsNullOrEmpty(configuredChunk.name))
                continue;

            string family = configuredChunk.primaryTag.ToString();
            if (!observedFamilies.TryGetValue(family, out HashSet<string> observed))
            {
                observed = new HashSet<string>(StringComparer.Ordinal);
                observedFamilies.Add(family, observed);
            }

            observed.Add(pair.Key);
        }

        foreach (KeyValuePair<string, int> pair in configuredFamilyCounts)
        {
            int observedCount = observedFamilies.TryGetValue(pair.Key, out HashSet<string> observed) ? observed.Count : 0;
            if (pair.Value > 1 && observedCount <= 1)
                familyNotes.Add($"{pair.Key} has {pair.Value} configured chunks but only {observedCount} observed in samples");
        }

        int targetAdjacentCount = 0;
        for (int i = 0; i < configuredSelectableChunks.Count; i++)
        {
            if (Mathf.Abs(configuredSelectableChunks[i].difficultyRating - generator.targetDifficulty) <= 1f)
                targetAdjacentCount++;
        }

        List<string> reviewNotes = new List<string>();
        if (top3Share > 50f)
            reviewNotes.Add("selection appears concentrated in a small subset of chunks");
        if (neverSelected.Count > 0)
            reviewNotes.Add($"{neverSelected.Count} configured chunks were never selected across the current sample set");
        if (targetAdjacentCount < 3)
            reviewNotes.Add("the handcrafted pool near the current target difficulty looks thin");
        reviewNotes.AddRange(familyNotes);

        sb.AppendLine($"top3ChunkSelectionShare: {top3Share:0.##}%");
        sb.AppendLine($"targetAdjacentConfiguredChunks(+/-1 difficulty): {targetAdjacentCount}");
        sb.AppendLine($"neverSelectedConfiguredChunks: {(neverSelected.Count > 0 ? string.Join(", ", neverSelected) : "None")}");
        sb.AppendLine($"familiesWithLimitedObservedVariety: {(familyNotes.Count > 0 ? string.Join("; ", familyNotes) : "None")}");
        sb.AppendLine($"reviewNotes: {(reviewNotes.Count > 0 ? string.Join("; ", reviewNotes) : "current handcrafted pool looks broadly sufficient for the sampled settings")}");
        sb.AppendLine();

        for (int i = 0; i < familyNotes.Count; i++)
            warnings.Add($"Sequence sampling: {familyNotes[i]}.");

        if (top3Share > 50f)
            warnings.Add("Sequence sampling: selection appears concentrated in a small subset of chunks.");
    }

    private static void AppendWarnings(StringBuilder sb, List<string> warnings)
    {
        sb.AppendLine("## Warnings / Next-Step Summary");

        if (warnings.Count == 0)
        {
            sb.AppendLine("- No calibration warnings generated by this report.");
            return;
        }

        foreach (string warning in warnings)
            sb.AppendLine($"- {warning}");
    }

    private static List<ChunkRecord> CollectConfiguredChunks(LevelGenerator generator, List<string> warnings)
    {
        List<ChunkRecord> chunks = new List<ChunkRecord>();
        HashSet<GameObject> seen = new HashSet<GameObject>();

        if (generator == null)
            return chunks;

        AddChunkRecord(chunks, seen, generator.startingChunkPrefab, "Starting", warnings);

        if (generator.chunkPrefabs == null)
            return chunks;

        for (int i = 0; i < generator.chunkPrefabs.Length; i++)
            AddChunkRecord(chunks, seen, generator.chunkPrefabs[i], $"Chunk[{i}]", warnings);

        return chunks;
    }

    private static void AddChunkRecord(
        List<ChunkRecord> chunks,
        HashSet<GameObject> seen,
        GameObject prefab,
        string role,
        List<string> warnings)
    {
        if (prefab == null)
            return;

        if (!seen.Add(prefab))
            return;

        ChunkData data = prefab.GetComponent<ChunkData>();
        if (data == null)
        {
            warnings.Add($"{prefab.name} has no ChunkData component.");
            return;
        }

        chunks.Add(new ChunkRecord
        {
            role = role,
            assetPath = AssetPath(prefab),
            name = prefab.name,
            primaryTag = data.primaryTag,
            tags = data.tags,
            secondaryTags = TagsToText(data.tags),
            difficultyRating = data.difficultyRating,
            hasHazard = data.hasHazard,
            estimatedJumps = data.estimatedJumps,
            exitDelta = data.exitDelta,
            maxGapWidth = ChunkBlueprintFeatureExtractor.EstimateSourceMaxGapWidth(prefab),
            replacementEligible = IsReplacementEligible(data.primaryTag)
        });
    }

    private static List<string> CollectMetadataSmells(ChunkRecord chunk)
    {
        List<string> notes = new List<string>();
        bool hasSpikesTag = chunk.primaryTag == ChunkTag.Spikes || HasTag(chunk.tags, ChunkTag.Spikes);
        bool isRestLike = chunk.primaryTag == ChunkTag.Rest || chunk.primaryTag == ChunkTag.Safe;
        bool isMovingNamed = chunk.name.IndexOf("Moving", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isDashNamed = chunk.name.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0;

        if (hasSpikesTag && !chunk.hasHazard)
            notes.Add("spike tag/name family but hasHazard is false; review whether this is intentional");

        if (chunk.hasHazard && !hasSpikesTag)
            notes.Add("hasHazard is true without a Spikes tag; review hazard tagging");

        if (chunk.hasHazard && chunk.difficultyRating < 4)
            notes.Add("hazard chunk below difficulty 4; review difficulty band");

        if (isRestLike && chunk.hasHazard)
            notes.Add("rest/safe chunk has hazard; review family classification");

        if (chunk.primaryTag == ChunkTag.Gap && chunk.estimatedJumps < 1)
            notes.Add("gap chunk has estimatedJumps below 1");

        if (chunk.primaryTag == ChunkTag.Precision && chunk.estimatedJumps < 2 && !isDashNamed)
            notes.Add("precision chunk has estimatedJumps below 2; review unless another movement skill dominates");

        if (chunk.primaryTag == ChunkTag.Vertical && Mathf.Abs(chunk.exitDelta.y) > 0.1f && chunk.estimatedJumps == 0)
            notes.Add("vertical chunk changes height but estimatedJumps is 0; review whether climb/wall movement should be represented");

        if (chunk.difficultyRating >= 5 && chunk.estimatedJumps == 0)
            notes.Add("difficulty 5+ with zero estimated jumps; review metric coverage");

        if (chunk.difficultyRating >= 7 && chunk.estimatedJumps <= 1 && !chunk.hasHazard)
            notes.Add("difficulty 7+ with low jumps and no hazard flag; review missing hazard or skill metadata");

        if (isRestLike && chunk.difficultyRating > 2)
            notes.Add("rest/safe family above difficulty 2; review rubric fit");

        if (chunk.primaryTag == ChunkTag.Gap && (chunk.difficultyRating < 2 || chunk.difficultyRating > 5))
            notes.Add("gap family outside expected difficulty band 2..5");

        if (chunk.primaryTag == ChunkTag.Precision && (chunk.difficultyRating < 4 || chunk.difficultyRating > 6))
            notes.Add("precision family outside expected difficulty band 4..6");

        if (chunk.primaryTag == ChunkTag.Vertical && (chunk.difficultyRating < 2 || chunk.difficultyRating > 5))
            notes.Add("vertical family outside expected difficulty band 2..5");

        if (chunk.primaryTag == ChunkTag.Spikes && chunk.difficultyRating < 4)
            notes.Add("spikes family below difficulty 4; review rubric fit");

        if (isMovingNamed && chunk.difficultyRating < 5)
            notes.Add("moving chunk below difficulty 5; review timing challenge rating");

        if (isDashNamed && chunk.difficultyRating < 5)
            notes.Add("dash chunk below difficulty 5; review dash challenge rating");

        return notes;
    }

    private static LevelGenerator FindSceneLevelGenerator()
    {
        LevelGenerator[] generators = Resources.FindObjectsOfTypeAll<LevelGenerator>();
        Scene activeScene = SceneManager.GetActiveScene();

        for (int i = 0; i < generators.Length; i++)
        {
            LevelGenerator generator = generators[i];
            if (generator == null) continue;
            if (EditorUtility.IsPersistent(generator)) continue;
            if (generator.gameObject.scene == activeScene) return generator;
        }

        for (int i = 0; i < generators.Length; i++)
        {
            LevelGenerator generator = generators[i];
            if (generator == null) continue;
            if (!EditorUtility.IsPersistent(generator)) return generator;
        }

        return null;
    }

    private static ChunkBlueprint GenerateDeterministicSample(ChunkGenerationRequest request, int salt)
    {
        UnityEngine.Random.InitState(SampleSeed + salt);
        return SimpleChunkBlueprintGenerator.Generate(request);
    }

    private static float GetGeneratedGapVerticalExitDeltaTolerance(ChunkRecord source, ChunkBlueprint generated)
    {
        if (generated != null &&
            generated.primaryTag == ChunkTag.Gap &&
            source.maxGapWidth > 0 &&
            source.maxGapWidth <= 4)
        {
            return 2.25f;
        }

        return 1.25f;
    }

    private static bool IsGeneratedSafeRestDifficultyEquivalent(ChunkRecord source, ChunkBlueprint generated)
    {
        if (generated == null)
            return false;

        bool controlledRiseVariant =
            generated.chunkName == "Generated_Safe_RiseRest_Box2" ||
            generated.chunkName == "Generated_Safe_RiseRest_Box3";

        return controlledRiseVariant &&
               source.primaryTag == ChunkTag.Safe &&
               generated.primaryTag == ChunkTag.Safe &&
               !source.hasHazard &&
               source.estimatedJumps == 0 &&
               generated.difficultyRating == source.difficultyRating + 1;
    }

    private static float GetGeneratedSafeVerticalExitDeltaTolerance(ChunkRecord source, ChunkBlueprint generated)
    {
        if (generated != null &&
            generated.primaryTag == ChunkTag.Safe &&
            generated.chunkName.StartsWith("Generated_Safe_") &&
            source.primaryTag == ChunkTag.Safe &&
            source.estimatedJumps == 0 &&
            !source.hasHazard)
        {
            return 4.25f;
        }

        return 1.25f;
    }

    private static float GetGeneratedPrecisionVerticalExitDeltaTolerance(ChunkRecord source, ChunkBlueprint generated)
    {
        if (generated != null &&
            generated.primaryTag == ChunkTag.Precision &&
            generated.chunkName.StartsWith("Generated_Precision_ElevatedPlatform_") &&
            source.primaryTag == ChunkTag.Precision &&
            source.estimatedJumps == 2 &&
            !source.hasHazard)
        {
            return 2.25f;
        }

        return 1.25f;
    }

    private static BlueprintFeatures AnalyzeBlueprintFeatures(ChunkBlueprint blueprint)
    {
        BlueprintFeatures features = new BlueprintFeatures();
        if (blueprint == null || blueprint.rows == null || blueprint.rows.Count == 0)
            return features;

        int traversalRowIndex = Mathf.Clamp(blueprint.entryCell.y, 0, blueprint.rows.Count - 1);
        string row = blueprint.rows[traversalRowIndex];

        int currentGap = 0;
        int currentLanding = 0;
        features.minLandingWidth = int.MaxValue;

        for (int i = 0; i < row.Length; i++)
        {
            char cell = row[i];

            if (cell == '.')
            {
                if (currentLanding > 0)
                {
                    features.minLandingWidth = Mathf.Min(features.minLandingWidth, currentLanding);
                    currentLanding = 0;
                }

                currentGap++;
                features.maxGapWidth = Mathf.Max(features.maxGapWidth, currentGap);
            }
            else
            {
                if (currentGap > 0)
                {
                    features.gapCount++;
                    currentGap = 0;
                }

                if (cell == '#')
                    currentLanding++;
            }
        }

        if (currentGap > 0)
            features.gapCount++;

        if (currentLanding > 0)
            features.minLandingWidth = Mathf.Min(features.minLandingWidth, currentLanding);

        if (features.minLandingWidth == int.MaxValue)
            features.minLandingWidth = 0;

        return features;
    }

    private static bool IsReplacementEligible(ChunkTag tag)
    {
        return tag == ChunkTag.Gap ||
               tag == ChunkTag.Precision ||
               tag == ChunkTag.Safe ||
               tag == ChunkTag.Rest;
    }

    private static bool IsRuntimeGeneratedCandidateEligible(ChunkRecord source)
    {
        if (source.primaryTag == ChunkTag.Gap)
            return true;

        if (source.primaryTag == ChunkTag.Precision)
            return IsElevatedPlatformPrecisionSource(source);

        return source.primaryTag == ChunkTag.Safe || source.primaryTag == ChunkTag.Rest;
    }

    private static bool IsElevatedPlatformPrecisionSource(ChunkRecord source)
    {
        return source.primaryTag == ChunkTag.Precision &&
               source.name.Contains("Chunk_ElevatedPlatform_Tilemap");
    }

    private static string GetRuntimeEligibilityReason(ChunkRecord source)
    {
        if (source.primaryTag == ChunkTag.Precision)
            return "runtime candidate selection only enables precision blueprints for Chunk_ElevatedPlatform_Tilemap";

        return "runtime candidate selection does not currently enable this generated family";
    }

    private static bool IsReplacementAllowedBySettings(LevelGenerator generator, ChunkTag tag)
    {
        if (generator == null || !generator.useGeneratedBlueprintChunks)
            return false;

        switch (tag)
        {
            case ChunkTag.Gap:
                return generator.allowGeneratedGap;
            case ChunkTag.Precision:
                return generator.allowGeneratedPrecision;
            case ChunkTag.Vertical:
                return generator.allowGeneratedVertical;
            case ChunkTag.Spikes:
                return generator.allowGeneratedSpikes;
            case ChunkTag.Safe:
            case ChunkTag.Rest:
                return generator.allowGeneratedSafeRest;
            default:
                return false;
        }
    }

    private static int GetPreferredWidthForTag(ChunkTag tag)
    {
        switch (tag)
        {
            case ChunkTag.Vertical:
                return 5;
            case ChunkTag.Spikes:
            case ChunkTag.Safe:
            case ChunkTag.Rest:
                return 6;
            default:
                return 8;
        }
    }

    private static int GetPreferredHeightForTag(ChunkTag tag)
    {
        switch (tag)
        {
            case ChunkTag.Safe:
            case ChunkTag.Rest:
                return 2;
            default:
                return 3;
        }
    }

    private static string AssetPath(GameObject asset)
    {
        if (asset == null) return "None";

        string path = AssetDatabase.GetAssetPath(asset);
        return string.IsNullOrEmpty(path) ? asset.name : path;
    }

    private static string TagsToText(ChunkTag[] tags)
    {
        if (tags == null || tags.Length == 0)
            return "-";

        List<string> names = new List<string>(tags.Length);
        for (int i = 0; i < tags.Length; i++)
            names.Add(tags[i].ToString());

        return string.Join(",", names);
    }

    private static bool HasTag(ChunkTag[] tags, ChunkTag tag)
    {
        if (tags == null) return false;

        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i] == tag)
                return true;
        }

        return false;
    }

    private static string RowsToInlineText(List<string> rows)
    {
        if (rows == null || rows.Count == 0)
            return "-";

        return string.Join(" / ", rows);
    }

    private static float GetSlotTargetDifficulty(LevelGenerator generator, int generatedSlotIndex)
    {
        float progress = Mathf.Clamp01((generatedSlotIndex + 1f) / Mathf.Max(1, generator.totalChunks - 1));
        return Mathf.Lerp(generator.startDifficultyBias, generator.targetDifficulty, progress);
    }

    private static GameObject GetCandidateSourcePrefab(object candidate)
    {
        if (candidate == null)
            return null;

        FieldInfo field = candidate.GetType().GetField("sourcePrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null ? field.GetValue(candidate) as GameObject : null;
    }

    private static string GetCandidateDisplayName(object candidate)
    {
        if (candidate == null)
            return "<none>";

        PropertyInfo property = candidate.GetType().GetProperty("DisplayName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object value = property != null ? property.GetValue(candidate, null) : null;
        return value as string ?? "<unknown>";
    }

    private static ChunkTag GetCandidatePrimaryTag(object candidate)
    {
        if (candidate == null)
            return ChunkTag.Rest;

        PropertyInfo property = candidate.GetType().GetProperty("PrimaryTag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object value = property != null ? property.GetValue(candidate, null) : null;
        return value is ChunkTag ? (ChunkTag)value : ChunkTag.Rest;
    }

    private static int GetCandidateDifficulty(object candidate)
    {
        if (candidate == null)
            return -1;

        PropertyInfo property = candidate.GetType().GetProperty("Difficulty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object value = property != null ? property.GetValue(candidate, null) : null;
        return value is int ? (int)value : -1;
    }

    private static Dictionary<int, SequenceSlotAggregate> BuildSlotAggregates(List<SequenceSlotSample> slotSamples)
    {
        Dictionary<int, SequenceSlotAggregate> aggregates = new Dictionary<int, SequenceSlotAggregate>();

        for (int i = 0; i < slotSamples.Count; i++)
        {
            SequenceSlotSample sample = slotSamples[i];
            if (!aggregates.TryGetValue(sample.slotIndex, out SequenceSlotAggregate aggregate))
            {
                aggregate = new SequenceSlotAggregate();
                aggregates.Add(sample.slotIndex, aggregate);
            }

            aggregate.count++;
            aggregate.sumTarget += sample.slotTargetDifficulty;
            aggregate.sumSelectedDifficulty += sample.selectedDifficulty;
            aggregate.sumDelta += sample.deltaFromSlotTarget;
            aggregate.sumAbsDelta += Mathf.Abs(sample.deltaFromSlotTarget);
            IncrementCount(aggregate.chunkCounts, sample.selectedChunkName);
        }

        return aggregates;
    }

    private static int GetMaxSlotIndex(List<SequenceSlotSample> slotSamples)
    {
        int maxSlotIndex = 0;
        for (int i = 0; i < slotSamples.Count; i++)
            maxSlotIndex = Mathf.Max(maxSlotIndex, slotSamples[i].slotIndex);

        return maxSlotIndex;
    }

    private static SequenceRegion GetSequenceRegion(int slotIndex, int maxSlotIndex)
    {
        if (maxSlotIndex <= 1)
            return SequenceRegion.First;

        float progress = (slotIndex - 1f) / Mathf.Max(1f, maxSlotIndex);
        if (progress < 1f / 3f)
            return SequenceRegion.First;
        if (progress < 2f / 3f)
            return SequenceRegion.Middle;
        return SequenceRegion.Last;
    }

    private static List<KeyValuePair<string, int>> SortCountsDescending(Dictionary<string, int> counts)
    {
        List<KeyValuePair<string, int>> entries = new List<KeyValuePair<string, int>>(counts);
        entries.Sort((a, b) =>
        {
            int countCompare = b.Value.CompareTo(a.Value);
            return countCompare != 0 ? countCompare : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
        });
        return entries;
    }

    private static List<KeyValuePair<int, int>> SortIntegerCountsAscending(Dictionary<int, int> counts)
    {
        List<KeyValuePair<int, int>> entries = new List<KeyValuePair<int, int>>(counts);
        entries.Sort((a, b) => a.Key.CompareTo(b.Key));
        return entries;
    }

    private static void AppendIndentedCounts(StringBuilder sb, List<KeyValuePair<string, int>> entries)
    {
        if (entries.Count == 0)
        {
            sb.AppendLine("  - None");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
            sb.AppendLine($"  - {entries[i].Key}: {entries[i].Value}");
    }

    private static void AppendIndentedCounts(StringBuilder sb, List<KeyValuePair<int, int>> entries)
    {
        if (entries.Count == 0)
        {
            sb.AppendLine("  - None");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
            sb.AppendLine($"  - {entries[i].Key}: {entries[i].Value}");
    }

    private static string GetMostCommonKey(Dictionary<string, int> counts)
    {
        string bestKey = "-";
        int bestCount = -1;

        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (pair.Value > bestCount || (pair.Value == bestCount && string.Compare(pair.Key, bestKey, StringComparison.Ordinal) < 0))
            {
                bestKey = pair.Key;
                bestCount = pair.Value;
            }
        }

        return bestKey;
    }

    private static void IncrementCount(Dictionary<string, int> counts, string key)
    {
        if (counts.TryGetValue(key, out int current))
            counts[key] = current + 1;
        else
            counts.Add(key, 1);
    }

    private static void IncrementCount(Dictionary<int, int> counts, int key)
    {
        if (counts.TryGetValue(key, out int current))
            counts[key] = current + 1;
        else
            counts.Add(key, 1);
    }

    private static int CountConfiguredSelectableChunks(List<ChunkRecord> chunks)
    {
        int count = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].role != "Starting")
                count++;
        }

        return count;
    }

    private static List<ChunkRecord> GetConfiguredSelectableChunks(List<ChunkRecord> chunks)
    {
        List<ChunkRecord> configured = new List<ChunkRecord>();
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].role != "Starting")
                configured.Add(chunks[i]);
        }

        return configured;
    }

    private static List<string> GetNeverSelectedChunks(List<ChunkRecord> chunks, Dictionary<string, int> chunkUsage)
    {
        List<string> neverSelected = new List<string>();
        for (int i = 0; i < chunks.Count; i++)
        {
            ChunkRecord chunk = chunks[i];
            if (chunk.role == "Starting")
                continue;

            if (!chunkUsage.ContainsKey(chunk.name))
                neverSelected.Add(chunk.name);
        }

        neverSelected.Sort(StringComparer.Ordinal);
        return neverSelected;
    }

    private static ChunkRecord FindChunkByName(List<ChunkRecord> chunks, string name)
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].name == name)
                return chunks[i];
        }

        return default;
    }

    private struct ChunkRecord
    {
        public string role;
        public string assetPath;
        public string name;
        public ChunkTag primaryTag;
        public ChunkTag[] tags;
        public string secondaryTags;
        public int difficultyRating;
        public bool hasHazard;
        public int estimatedJumps;
        public Vector2 exitDelta;
        public int maxGapWidth;
        public bool replacementEligible;
    }

    private struct BlueprintFeatures
    {
        public int gapCount;
        public int maxGapWidth;
        public int minLandingWidth;
    }

    private struct SequenceSlotSample
    {
        public int runIndex;
        public int seed;
        public int slotIndex;
        public float slotTargetDifficulty;
        public string previousChunkName;
        public string selectedChunkName;
        public string selectedPrimaryTag;
        public int selectedDifficulty;
        public float deltaFromSlotTarget;
        public float transitionPressureMultiplier;
        public string transitionPressureReason;
    }

    private class SequenceSlotAggregate
    {
        public int count;
        public float sumTarget;
        public float sumSelectedDifficulty;
        public float sumDelta;
        public float sumAbsDelta;
        public Dictionary<string, int> chunkCounts = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    private class SequenceRunAggregate
    {
        public float firstSelectedSum;
        public int firstCount;
        public float lastSelectedSum;
        public int lastCount;
    }

    private enum SequenceRegion
    {
        First,
        Middle,
        Last
    }
}
