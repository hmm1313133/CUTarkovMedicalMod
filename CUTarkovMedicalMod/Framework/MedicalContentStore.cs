using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BepInEx;
using BepInEx.Logging;

namespace CUTarkovMedicalMod.Framework;

[DataContract]
public sealed class MedicalContentFile
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = 1;

    [DataMember(Order = 2)]
    public List<MedicalContentItemEntry> Items { get; set; } = new();
}

[DataContract]
public sealed class MedicalContentItemEntry
{
    [DataMember(Order = 1)]
    public string Key { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public MedicalItemCategory Category { get; set; }

    [DataMember(Order = 4)]
    public int Weight { get; set; } = 1;

    [DataMember(Order = 5)]
    public int MinCount { get; set; } = 1;

    [DataMember(Order = 6)]
    public int MaxCount { get; set; } = 1;

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public string? GameItemId { get; set; }

    [DataMember(Order = 8)]
    public bool Enabled { get; set; } = true;
}

public static class MedicalContentStore
{
    private const string DefaultFileName = "CUTarkovMedicalMod.medical-content.json";

    public static string DefaultContentFilePath => Path.Combine(Paths.ConfigPath, DefaultFileName);

    public static MedicalContentFile LoadOrCreateDefault(
        string filePath,
        IReadOnlyList<MedicalItemDefinition> defaults,
        ManualLogSource? log = null)
    {
        if (defaults == null)
        {
            throw new ArgumentNullException(nameof(defaults));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? Paths.ConfigPath);

        if (!File.Exists(filePath))
        {
            var initial = CreateDefaultFile(defaults);
            Save(filePath, initial);
            log?.LogInfo($"Created medical content file at '{filePath}'.");
            return initial;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var serializer = new DataContractJsonSerializer(typeof(MedicalContentFile));
            var loaded = serializer.ReadObject(stream) as MedicalContentFile;
            if (loaded == null)
            {
                throw new InvalidDataException("The medical content file did not contain a valid object.");
            }

            Normalize(loaded, defaults);
            return loaded;
        }
        catch (Exception ex)
        {
            log?.LogWarning($"Failed to read medical content file '{filePath}'. Falling back to defaults. Reason: {ex.Message}");
            var fallback = CreateDefaultFile(defaults);
            Save(filePath, fallback);
            return fallback;
        }
    }

    public static void Save(string filePath, MedicalContentFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? Paths.ConfigPath);

        using var stream = File.Create(filePath);
        var serializer = new DataContractJsonSerializer(typeof(MedicalContentFile));
        serializer.WriteObject(stream, file);
    }

    public static IReadOnlyList<MedicalItemDefinition> ToDefinitions(MedicalContentFile file)
    {
        var result = new List<MedicalItemDefinition>();
        foreach (var entry in file.Items)
        {
            if (!entry.Enabled)
            {
                continue;
            }

            var key = string.IsNullOrWhiteSpace(entry.Key) ? entry.DisplayName : entry.Key.Trim();
            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? key : entry.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            result.Add(new MedicalItemDefinition(
                key,
                displayName,
                entry.Category,
                Math.Max(1, entry.Weight),
                Math.Max(0, Math.Min(entry.MinCount, entry.MaxCount)),
                Math.Max(Math.Max(0, entry.MinCount), entry.MaxCount),
                entry.GameItemId,
                entry.Enabled));
        }

        return result;
    }

    public static MedicalContentFile CreateDefaultFile(IReadOnlyList<MedicalItemDefinition> defaults)
    {
        return new MedicalContentFile
        {
            SchemaVersion = 1,
            Items = defaults.Select(x => new MedicalContentItemEntry
            {
                Key = x.Key,
                DisplayName = x.DisplayName,
                Category = x.Category,
                Weight = x.Weight,
                MinCount = x.MinCount,
                MaxCount = x.MaxCount,
                GameItemId = x.GameItemId,
                Enabled = true
            }).ToList()
        };
    }

    private static void Normalize(MedicalContentFile file, IReadOnlyList<MedicalItemDefinition> defaults)
    {
        file.SchemaVersion = Math.Max(1, file.SchemaVersion);

        if (file.Items.Count == 0)
        {
            file.Items.AddRange(CreateDefaultFile(defaults).Items);
            return;
        }

        foreach (var entry in file.Items)
        {
            entry.Key = entry.Key.Trim();
            entry.DisplayName = entry.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                entry.DisplayName = entry.Key;
            }

            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                entry.Key = entry.DisplayName;
            }

            if (entry.Weight < 1)
            {
                entry.Weight = 1;
            }

            if (entry.MinCount < 0)
            {
                entry.MinCount = 0;
            }

            if (entry.MaxCount < entry.MinCount)
            {
                entry.MaxCount = entry.MinCount;
            }
        }

        var defaultsByKey = new Dictionary<string, MedicalItemDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in defaults)
        {
            defaultsByKey[def.Key] = def;
        }

        foreach (var entry in file.Items)
        {
            if (!string.IsNullOrWhiteSpace(entry.GameItemId))
            {
                continue;
            }

            if (defaultsByKey.TryGetValue(entry.Key, out var def) && !string.IsNullOrWhiteSpace(def.GameItemId))
            {
                entry.GameItemId = def.GameItemId;
            }
        }

        // Keep existing user entries intact, but append any newly introduced defaults.
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in file.Items)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key))
            {
                existingKeys.Add(entry.Key);
            }
        }

        foreach (var def in defaults)
        {
            if (existingKeys.Contains(def.Key))
            {
                continue;
            }

            file.Items.Add(new MedicalContentItemEntry
            {
                Key = def.Key,
                DisplayName = def.DisplayName,
                Category = def.Category,
                Weight = def.Weight,
                MinCount = def.MinCount,
                MaxCount = def.MaxCount,
                GameItemId = def.GameItemId,
                Enabled = def.Enabled
            });
        }
    }
}

