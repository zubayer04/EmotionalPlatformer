using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ChunkCalibrationReporterWindow : EditorWindow
{
    private const int SampleSeed = 12345;

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
            "Read-only audit for chunk metadata, generated gap/precision samples, and replacement equivalence. No assets are saved or modified.",
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

            List<ChunkRecord> chunks = CollectConfiguredChunks(generator, warnings);
            AppendHandcraftedChunkMetadata(sb, chunks);
            AppendGeneratedSamples(sb, ChunkTag.Gap, "Generated Gap Samples", warnings);
            AppendGeneratedSamples(sb, ChunkTag.Precision, "Generated Precision Samples", warnings);
            AppendReplacementEquivalence(sb, generator, chunks, warnings);
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
        sb.AppendLine($"avoidSamePrefabBackToBack: {generator.avoidSamePrefabBackToBack}");
        sb.AppendLine($"maxSamePrimaryTagStreak: {generator.maxSamePrimaryTagStreak}");
        sb.AppendLine($"targetDifficulty: {generator.targetDifficulty:0.##}");
        sb.AppendLine($"startDifficultyBias: {generator.startDifficultyBias:0.##}");
        sb.AppendLine($"difficultyPreferenceStrength: {generator.difficultyPreferenceStrength:0.##}");
        sb.AppendLine($"varietyBonus: {generator.varietyBonus:0.##}");
        sb.AppendLine($"earlyHardPenalty: {generator.earlyHardPenalty:0.##}");
        sb.AppendLine($"useGeneratedBlueprintChunks: {generator.useGeneratedBlueprintChunks}");
        sb.AppendLine($"generatedChunkReplacementChance: {generator.generatedChunkReplacementChance:0.##}");
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
        sb.AppendLine("Role | Path | Name | Primary | Tags | Difficulty | Hazard | Jumps | ExitDelta | Replacement Eligible");
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

    private static void AppendGeneratedSamples(StringBuilder sb, ChunkTag tag, string title, List<string> warnings)
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
            BlueprintFeatures features = AnalyzeBlueprintFeatures(blueprint);
            string validationText = validation.isValid ? "Valid" : "Invalid";

            if (!validation.isValid)
                warnings.Add($"{title}: {blueprint.chunkName} at requested difficulty {difficulty} is invalid.");

            if (tag == ChunkTag.Precision && blueprint.difficultyRating != difficulty)
                warnings.Add($"Generated precision sample requested {difficulty} but reports difficulty {blueprint.difficultyRating}.");

            sb.AppendLine(
                $"{difficulty} | {blueprint.chunkName} | {blueprint.difficultyRating} | {blueprint.width} | {blueprint.height} | " +
                $"{TagsToText(blueprint.tags)} | {blueprint.hasHazard} | {blueprint.estimatedJumps} | " +
                $"{features.gapCount} | {features.maxGapWidth} | {features.minLandingWidth} | {validationText} | {RowsToInlineText(blueprint.rows)}");
        }

        sb.AppendLine();
    }

    private static void AppendReplacementEquivalence(
        StringBuilder sb,
        LevelGenerator generator,
        List<ChunkRecord> chunks,
        List<string> warnings)
    {
        sb.AppendLine("## Potential Replacement Equivalence");
        sb.AppendLine("Source | SourceTag | SourceDiff | SourceJumps | SourceHazard | SourceExitDelta | Generated | GeneratedDiff | GeneratedJumps | GeneratedHazard | GeneratedWidth | DiffDelta | Status | Reason");
        sb.AppendLine("--- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | ---");

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

            ChunkGenerationRequest request = new ChunkGenerationRequest
            {
                requestedPrimaryTag = source.primaryTag,
                targetDifficulty = source.difficultyRating,
                requireHazard = source.hasHazard,
                preferredWidth = GetPreferredWidthForTag(source.primaryTag),
                preferredHeight = GetPreferredHeightForTag(source.primaryTag)
            };

            ChunkBlueprint generated = GenerateDeterministicSample(request, source.difficultyRating + source.name.GetHashCode());
            if (generated == null)
            {
                sb.AppendLine($"{source.name} | {source.primaryTag} | {source.difficultyRating} | {source.estimatedJumps} | {source.hasHazard} | ({source.exitDelta.x:0.##}, {source.exitDelta.y:0.##}) | <null> | - | - | - | - | - | Mismatch | Generator returned null");
                warnings.Add($"Replacement equivalence: generator returned null for {source.name}.");
                continue;
            }

            int difficultyDelta = generated.difficultyRating - source.difficultyRating;
            int jumpsDelta = generated.estimatedJumps - source.estimatedJumps;
            bool hazardMatch = generated.hasHazard == source.hasHazard;
            bool tagMatch = generated.primaryTag == source.primaryTag;
            float widthDelta = generated.width - Mathf.Abs(source.exitDelta.x);

            string status = "Equivalent";
            List<string> reasons = new List<string>();

            if (!tagMatch)
                reasons.Add("primary tag differs");
            if (difficultyDelta != 0)
                reasons.Add($"difficulty delta {difficultyDelta:+#;-#;0}");
            if (jumpsDelta != 0)
                reasons.Add($"jumps delta {jumpsDelta:+#;-#;0}");
            if (!hazardMatch)
                reasons.Add("hazard flag differs");
            if (Mathf.Abs(widthDelta) >= 2f)
                reasons.Add($"width differs from source exitDelta by {widthDelta:+0.##;-0.##;0}");

            if (reasons.Count > 0)
                status = reasons.Count == 1 ? "Close" : "Needs Review";

            string reason = reasons.Count > 0 ? string.Join("; ", reasons) : "metadata matches";
            if (status != "Equivalent")
                warnings.Add($"Replacement equivalence: {source.name} -> {generated.chunkName}: {reason}.");

            sb.AppendLine(
                $"{source.name} | {source.primaryTag} | {source.difficultyRating} | {source.estimatedJumps} | {source.hasHazard} | " +
                $"({source.exitDelta.x:0.##}, {source.exitDelta.y:0.##}) | {generated.chunkName} | {generated.difficultyRating} | " +
                $"{generated.estimatedJumps} | {generated.hasHazard} | {generated.width} | {difficultyDelta:+#;-#;0} | {status} | {reason}");
        }

        sb.AppendLine();
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
            secondaryTags = TagsToText(data.tags),
            difficultyRating = data.difficultyRating,
            hasHazard = data.hasHazard,
            estimatedJumps = data.estimatedJumps,
            exitDelta = data.exitDelta,
            replacementEligible = IsReplacementEligible(data.primaryTag)
        });
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
               tag == ChunkTag.Vertical ||
               tag == ChunkTag.Spikes ||
               tag == ChunkTag.Safe ||
               tag == ChunkTag.Rest;
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

    private static string RowsToInlineText(List<string> rows)
    {
        if (rows == null || rows.Count == 0)
            return "-";

        return string.Join(" / ", rows);
    }

    private struct ChunkRecord
    {
        public string role;
        public string assetPath;
        public string name;
        public ChunkTag primaryTag;
        public string secondaryTags;
        public int difficultyRating;
        public bool hasHazard;
        public int estimatedJumps;
        public Vector2 exitDelta;
        public bool replacementEligible;
    }

    private struct BlueprintFeatures
    {
        public int gapCount;
        public int maxGapWidth;
        public int minLandingWidth;
    }
}
