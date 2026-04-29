using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum DifficultyBand { Low, Medium, High }

[Serializable]
public class MarkovWeightTable
{
    private const float MinWeight = 0.05f;
    private const float MaxWeight = 5f;
    private const float DefaultWeight = 1f;

    [Serializable]
    private struct WeightKey : IEquatable<WeightKey>
    {
        public ChunkTag prev2;
        public ChunkTag prev1;
        public ChunkTag next;
        public DifficultyBand band;

        public WeightKey(ChunkTag prev2, ChunkTag prev1, ChunkTag next, DifficultyBand band)
        {
            this.prev2 = prev2;
            this.prev1 = prev1;
            this.next = next;
            this.band = band;
        }

        public bool Equals(WeightKey other)
        {
            return prev2 == other.prev2 && prev1 == other.prev1 &&
                   next == other.next && band == other.band;
        }

        public override int GetHashCode()
        {
            return ((int)prev2 * 1000) + ((int)prev1 * 100) + ((int)next * 10) + (int)band;
        }

        public override bool Equals(object obj)
        {
            return obj is WeightKey other && Equals(other);
        }
    }

    // Learned weights
    private Dictionary<WeightKey, float> learnedWeights = new Dictionary<WeightKey, float>();

    // Baseline (hand-tuned) weights — never modified at runtime
    private Dictionary<WeightKey, float> baselineWeights = new Dictionary<WeightKey, float>();

    // Serialization format for JSON persistence
    [Serializable]
    private class SerializedEntry
    {
        public int prev2;
        public int prev1;
        public int next;
        public int band;
        public float weight;
    }

    [Serializable]
    private class SerializedTable
    {
        public int version = 1;
        public string savedAtUtc;
        public int entryCount;
        public List<SerializedEntry> entries = new List<SerializedEntry>();
    }

    public MarkovWeightTable()
    {
        InitializeBaselineWeights();
        ResetToBaseline();
    }

    public float GetWeight(ChunkTag prev2, ChunkTag prev1, ChunkTag next, DifficultyBand band)
    {
        WeightKey key = new WeightKey(prev2, prev1, next, band);

        if (learnedWeights.TryGetValue(key, out float weight))
            return weight;

        if (baselineWeights.TryGetValue(key, out float baseline))
            return baseline;

        WeightKey generalKey = new WeightKey(ChunkTag.Rest, prev1, next, band);
        if (learnedWeights.TryGetValue(generalKey, out float generalWeight))
            return generalWeight;

        if (baselineWeights.TryGetValue(generalKey, out float generalBaseline))
            return generalBaseline;

        return DefaultWeight;
    }

    public void UpdateWeight(ChunkTag prev2, ChunkTag prev1, ChunkTag next, DifficultyBand band,
                             float qualityScore, float learningRate)
    {
        WeightKey key = new WeightKey(prev2, prev1, next, band);

        float current = GetWeight(prev2, prev1, next, band);
        float updated = current + (learningRate * qualityScore);
        updated = Mathf.Clamp(updated, MinWeight, MaxWeight);

        learnedWeights[key] = updated;
    }

    public void DecayTowardBaseline(float decayRate)
    {
        if (decayRate <= 0f) return;

        List<WeightKey> keys = new List<WeightKey>(learnedWeights.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            WeightKey key = keys[i];
            float learned = learnedWeights[key];

            float baseline = GetBaselineWeightOrDefault(key);

            float decayed = Mathf.Lerp(learned, baseline, decayRate);
            learnedWeights[key] = decayed;
        }
    }

    public void ResetToBaseline()
    {
        learnedWeights = new Dictionary<WeightKey, float>(baselineWeights);
    }

    public int LearnedEntryCount => learnedWeights.Count;

    // --- Persistence ---

    public static string GetSaveDirectoryPath()
    {
        return Path.Combine(Application.persistentDataPath, "MarkovWeights");
    }

    public static string GetSaveFilePath()
    {
        return Path.Combine(GetSaveDirectoryPath(), "learned_weights.json");
    }

    public bool TrySave(out string message)
    {
        try
        {
            string directory = GetSaveDirectoryPath();
            Directory.CreateDirectory(directory);

            SerializedTable table = new SerializedTable
            {
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                entryCount = learnedWeights.Count
            };

            foreach (var kvp in learnedWeights)
            {
                table.entries.Add(new SerializedEntry
                {
                    prev2 = (int)kvp.Key.prev2,
                    prev1 = (int)kvp.Key.prev1,
                    next = (int)kvp.Key.next,
                    band = (int)kvp.Key.band,
                    weight = kvp.Value
                });
            }

            string json = JsonUtility.ToJson(table, true);
            File.WriteAllText(GetSaveFilePath(), json);
            message = GetSaveFilePath();
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    public bool TryLoad(out string message)
    {
        try
        {
            string path = GetSaveFilePath();
            if (!File.Exists(path))
            {
                message = "no_saved_weights";
                return false;
            }

            string json = File.ReadAllText(path);
            SerializedTable table = JsonUtility.FromJson<SerializedTable>(json);

            if (table == null || table.entries == null)
            {
                message = "invalid_saved_data";
                return false;
            }

            learnedWeights.Clear();
            // Start from baseline so any keys not in the save file retain baseline values
            foreach (var kvp in baselineWeights)
                learnedWeights[kvp.Key] = kvp.Value;

            for (int i = 0; i < table.entries.Count; i++)
            {
                SerializedEntry entry = table.entries[i];
                WeightKey key = new WeightKey(
                    (ChunkTag)entry.prev2,
                    (ChunkTag)entry.prev1,
                    (ChunkTag)entry.next,
                    (DifficultyBand)entry.band);

                learnedWeights[key] = Mathf.Clamp(entry.weight, MinWeight, MaxWeight);
            }

            message = $"loaded {table.entries.Count} entries from {path}";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    // --- Baseline weight initialization (extracted from LevelGenerator.GetTransitionWeight) ---

    private void InitializeBaselineWeights()
    {
        baselineWeights = new Dictionary<WeightKey, float>();

        // === LOW BAND ===

        // Spikes ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Rest, DifficultyBand.Low, 4.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Safe, DifficultyBand.Low, 3.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Gap, DifficultyBand.Low, 1.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Vertical, DifficultyBand.Low, 0.1f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Precision, DifficultyBand.Low, 0.1f);

        // Precision ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Rest, DifficultyBand.Low, 4.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Safe, DifficultyBand.Low, 3.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Gap, DifficultyBand.Low, 1.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Vertical, DifficultyBand.Low, 0.1f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Spikes, DifficultyBand.Low, 0.1f);

        // Vertical ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Safe, DifficultyBand.Low, 3.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Rest, DifficultyBand.Low, 2.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Gap, DifficultyBand.Low, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Precision, DifficultyBand.Low, 0.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Spikes, DifficultyBand.Low, 0.5f);

        // Gap ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Safe, DifficultyBand.Low, 3.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Rest, DifficultyBand.Low, 2.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Precision, DifficultyBand.Low, 0.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Vertical, DifficultyBand.Low, 0.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Spikes, DifficultyBand.Low, 0.5f);

        // Rest/Safe ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Safe, DifficultyBand.Low, 2.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Gap, DifficultyBand.Low, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Vertical, DifficultyBand.Low, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Rest, DifficultyBand.Low, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Spikes, DifficultyBand.Low, 0.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Precision, DifficultyBand.Low, 0.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Safe, DifficultyBand.Low, 2.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Gap, DifficultyBand.Low, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Vertical, DifficultyBand.Low, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Rest, DifficultyBand.Low, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Spikes, DifficultyBand.Low, 0.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Precision, DifficultyBand.Low, 0.5f);

        // === MEDIUM BAND ===

        // Spikes ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Safe, DifficultyBand.Medium, 3.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Rest, DifficultyBand.Medium, 2.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Gap, DifficultyBand.Medium, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Vertical, DifficultyBand.Medium, 1.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Precision, DifficultyBand.Medium, 0.25f);

        // Precision ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Safe, DifficultyBand.Medium, 2.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Rest, DifficultyBand.Medium, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Gap, DifficultyBand.Medium, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Vertical, DifficultyBand.Medium, 1.25f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Spikes, DifficultyBand.Medium, 1.0f);

        // Vertical ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Safe, DifficultyBand.Medium, 2.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Rest, DifficultyBand.Medium, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Gap, DifficultyBand.Medium, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Precision, DifficultyBand.Medium, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Spikes, DifficultyBand.Medium, 0.75f);

        // Gap ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Safe, DifficultyBand.Medium, 2.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Rest, DifficultyBand.Medium, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Precision, DifficultyBand.Medium, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Vertical, DifficultyBand.Medium, 1.25f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Spikes, DifficultyBand.Medium, 0.75f);

        // Rest/Safe ->
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Gap, DifficultyBand.Medium, 2.2f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Safe, DifficultyBand.Medium, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Vertical, DifficultyBand.Medium, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Spikes, DifficultyBand.Medium, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Precision, DifficultyBand.Medium, 1.2f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Rest, DifficultyBand.Medium, 1.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Gap, DifficultyBand.Medium, 2.2f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Safe, DifficultyBand.Medium, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Vertical, DifficultyBand.Medium, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Spikes, DifficultyBand.Medium, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Precision, DifficultyBand.Medium, 1.2f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Rest, DifficultyBand.Medium, 1.0f);

        // === HIGH BAND ===

        // 2-step overrides: Spikes -> Rest -> X
        SetBaseline(ChunkTag.Spikes, ChunkTag.Rest, ChunkTag.Spikes, DifficultyBand.High, 2.0f);
        SetBaseline(ChunkTag.Spikes, ChunkTag.Rest, ChunkTag.Precision, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Spikes, ChunkTag.Rest, ChunkTag.Gap, DifficultyBand.High, 1.5f);
        SetBaseline(ChunkTag.Spikes, ChunkTag.Rest, ChunkTag.Safe, DifficultyBand.High, 1.0f);
        SetBaseline(ChunkTag.Spikes, ChunkTag.Rest, ChunkTag.Rest, DifficultyBand.High, 0.75f);

        // 2-step overrides: Vertical -> Gap -> X
        SetBaseline(ChunkTag.Vertical, ChunkTag.Gap, ChunkTag.Precision, DifficultyBand.High, 2.0f);
        SetBaseline(ChunkTag.Vertical, ChunkTag.Gap, ChunkTag.Spikes, DifficultyBand.High, 1.5f);
        SetBaseline(ChunkTag.Vertical, ChunkTag.Gap, ChunkTag.Safe, DifficultyBand.High, 1.0f);

        // Spikes -> (High, general prev2)
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Safe, DifficultyBand.High, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Gap, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Rest, DifficultyBand.High, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Precision, DifficultyBand.High, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Spikes, ChunkTag.Vertical, DifficultyBand.High, 1.0f);

        // Precision -> (High)
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Safe, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Gap, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Vertical, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Rest, DifficultyBand.High, 1.25f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Precision, ChunkTag.Spikes, DifficultyBand.High, 1.25f);

        // Vertical -> (High)
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Gap, DifficultyBand.High, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Precision, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Safe, DifficultyBand.High, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Spikes, DifficultyBand.High, 1.25f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Vertical, ChunkTag.Rest, DifficultyBand.High, 1.0f);

        // Gap -> (High)
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Precision, DifficultyBand.High, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Vertical, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Safe, DifficultyBand.High, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Spikes, DifficultyBand.High, 1.25f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Gap, ChunkTag.Rest, DifficultyBand.High, 1.0f);

        // Rest/Safe -> (High)
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Gap, DifficultyBand.High, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Vertical, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Spikes, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Precision, DifficultyBand.High, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Safe, DifficultyBand.High, 1.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Rest, ChunkTag.Rest, DifficultyBand.High, 0.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Gap, DifficultyBand.High, 2.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Vertical, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Spikes, DifficultyBand.High, 1.75f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Precision, DifficultyBand.High, 1.5f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Safe, DifficultyBand.High, 1.0f);
        SetBaseline(ChunkTag.Rest, ChunkTag.Safe, ChunkTag.Rest, DifficultyBand.High, 0.75f);
    }

    private void SetBaseline(ChunkTag prev2, ChunkTag prev1, ChunkTag next, DifficultyBand band, float weight)
    {
        WeightKey key = new WeightKey(prev2, prev1, next, band);
        baselineWeights[key] = weight;
    }

    private float GetBaselineWeightOrDefault(WeightKey key)
    {
        if (baselineWeights.TryGetValue(key, out float baseline))
            return baseline;

        WeightKey generalKey = new WeightKey(ChunkTag.Rest, key.prev1, key.next, key.band);
        if (baselineWeights.TryGetValue(generalKey, out float generalBaseline))
            return generalBaseline;

        return DefaultWeight;
    }

    // --- Utility ---

    public static DifficultyBand GetBandForDifficulty(float difficulty)
    {
        if (difficulty <= 3.5f) return DifficultyBand.Low;
        if (difficulty <= 6.5f) return DifficultyBand.Medium;
        return DifficultyBand.High;
    }
}
