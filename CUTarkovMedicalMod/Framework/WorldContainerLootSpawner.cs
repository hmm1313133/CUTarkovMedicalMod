using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 世界容器战利品刷新系统。
/// 
/// 分类规则：
/// - 注射针剂类（etg, blueblood, mildronate, morphine, mule, obdolbos, obdolbos2, pnb,
///   propital, sj1, sj6, sj9, sj12, 2a2btg, xtg12, zagustin）→ 仅医疗箱刷新
/// - 药品类（ai2, goldenstar, vaseline, libatine, ibuprofen, grizzlykit, afak, ifak,
///   salewa, multitool, cms）→ 医疗箱、尸体、物资箱刷新
/// 
/// 机制：
/// - 医疗箱（medcrate）被破坏时：17% 掉 1~2 针剂，或 20% 掉 1~2 药品（互斥）
/// - 物资箱（containercrate）被破坏时：15% 掉 1~3 药品
/// - 尸体（corpse）生成时：10% 掉 1 药品
/// </summary>

/// <summary>
/// 医疗箱和物资箱的战利品掉落（BuildingEntity 被破坏时触发）。
/// </summary>
[HarmonyPatch(typeof(BuildingEntity), nameof(BuildingEntity.Update))]
public static class WorldContainerLootSpawner
{
    private static readonly HashSet<int> _processed = new();

    /// <summary>针剂权重表（仅医疗箱，权重总和=100）</summary>
    private static readonly (string itemKey, int weight)[] WeightedStims =
    {
        (EtgCItemSystem.EtgItemKey, 4),
        (BluebloodItemSystem.ItemKey, 5),
        (MildronateItemSystem.ItemKey, 7),
        (MorphineItemSystem.ItemKey, 9),
        (MuleItemSystem.ItemKey, 5),
        (ObdolbosItemSystem.ItemKey, 6),
        (Obdolbos2ItemSystem.ItemKey, 7),
        (PnbItemSystem.ItemKey, 9),
        (PropitalItemSystem.ItemKey, 6),
        (Sj1ItemSystem.ItemKey, 8),
        (SJ6ItemSystem.ItemKey, 7),
        (Sj9ItemSystem.ItemKey, 5),
        (SJ12ItemSystem.ItemKey, 7),
        (TwoATwoBTGItemSystem.ItemKey, 6),
        (Xtg12ItemSystem.ItemKey, 6),
        (ZagustinItemSystem.ItemKey, 3),
    };

    /// <summary>药品权重表（医疗箱、物资箱、尸体，权重总和=100）</summary>
    private static readonly (string itemKey, int weight)[] WeightedMedicines =
    {
        (AI2ItemSystem.ItemKey, 9),
        (GoldenStarItemSystem.ItemKey, 14),
        (VaselineItemSystem.ItemKey, 14),
        (LibatineItemSystem.ItemKey, 11),
        (IbuprofenItemSystem.ItemKey, 9),
        (GrizzlyKitItemSystem.ItemKey, 4),
        (AfakKitItemSystem.ItemKey, 6),
        (IfakKitItemSystem.ItemKey, 10),
        (SalewaKitItemSystem.ItemKey, 8),
        (MultiToolItemSystem.ItemKey, 5),
        (CmsKitItemSystem.ItemKey, 10),
    };

    private static readonly int TotalStimWeight;
    private static readonly int TotalMedicineWeight;
    private static Dictionary<string, System.Action<Item>>? _configurators;
    private static bool _configuratorsBuilt;

    static WorldContainerLootSpawner()
    {
        TotalStimWeight = 0;
        foreach (var (_, w) in WeightedStims)
            TotalStimWeight += w;

        TotalMedicineWeight = 0;
        foreach (var (_, w) in WeightedMedicines)
            TotalMedicineWeight += w;
    }

    [HarmonyPrefix]
    private static void Prefix(BuildingEntity __instance)
    {
        if (__instance.health >= 0.5f) return;

        int instanceId = __instance.GetInstanceID();
        if (_processed.Contains(instanceId)) return;
        _processed.Add(instanceId);

        string goName = __instance.gameObject.name.ToLowerInvariant();
        string buildingId = (__instance.id ?? "").ToLowerInvariant();

        bool isMedcrate = goName.Contains("medcrate") || buildingId == "medcrate";
        bool isContainerCrate = goName.Contains("containercrate") || buildingId == "containercrate";

        if (!isMedcrate && !isContainerCrate) return;

        BuildConfigurators();

        if (isMedcrate)
        {
            // 医疗箱：17% 掉 1~2 针剂
            bool droppedStims = false;
            if (Random.value <= 0.17f)
            {
                droppedStims = true;
                int count = Random.Range(1, 3);
                Plugin.Log.LogInfo($"[WorldLoot] Medcrate broken, dropping {count} stim(s).");
                for (int i = 0; i < count; i++)
                {
                    string key = PickWeighted(WeightedStims, TotalStimWeight);
                    SpawnItem(key, __instance.transform.position, "syringe");
                }
            }

            // 医疗箱：20% 掉 1~2 药品（与针剂互斥，刷到针剂则不刷药品）
            if (!droppedStims && Random.value <= 0.2f)
            {
                int count = Random.Range(1, 3);
                Plugin.Log.LogInfo($"[WorldLoot] Medcrate broken, dropping {count} medicine(s).");
                for (int i = 0; i < count; i++)
                {
                    string key = PickWeighted(WeightedMedicines, TotalMedicineWeight);
                    string prefab = GetPrefabForKey(key);
                    SpawnItem(key, __instance.transform.position, prefab);
                }
            }
        }
        else if (isContainerCrate)
        {
            // 物资箱：15% 掉 1~3 药品
            if (Random.value <= 0.15f)
            {
                int count = Random.Range(1, 4);
                Plugin.Log.LogInfo($"[WorldLoot] Containercrate broken, dropping {count} medicine(s).");
                for (int i = 0; i < count; i++)
                {
                    string key = PickWeighted(WeightedMedicines, TotalMedicineWeight);
                    string prefab = GetPrefabForKey(key);
                    SpawnItem(key, __instance.transform.position, prefab);
                }
            }
        }
    }

    private static string PickWeighted((string itemKey, int weight)[] table, int totalWeight)
    {
        int roll = Random.Range(1, totalWeight + 1);
        int accum = 0;
        foreach (var (key, weight) in table)
        {
            accum += weight;
            if (roll <= accum) return key;
        }
        return table[table.Length - 1].itemKey;
    }

    private static string GetPrefabForKey(string itemKey)
    {
        // 药品类物品基于 bruisekit 预制体
        return "bruisekit";
    }

    private static void SpawnItem(string itemKey, Vector3 position, string prefabName)
    {
        try
        {
            Vector2 pos = position + new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(1f, 3f));
            float rot = Random.Range(0f, 360f);

            var prefab = Resources.Load(prefabName) as GameObject;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[WorldLoot] Prefab '{prefabName}' not found for '{itemKey}'");
                return;
            }

            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, 0f, rot));
            var item = go.GetComponent<Item>();
            if (item == null)
            {
                Object.Destroy(go);
                return;
            }

            if (_configurators != null && _configurators.TryGetValue(itemKey, out var configurator))
            {
                configurator(item);
            }
            else
            {
                item.id = itemKey;
                item.SetCondition(1f);
            }

            go.AddComponent<FreshItemDrop>();
            Plugin.Log.LogInfo($"[WorldLoot] Spawned {itemKey} at {pos}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[WorldLoot] Failed to spawn '{itemKey}': {ex.Message}");
        }
    }

    private static void BuildConfigurators()
    {
        if (_configuratorsBuilt) return;
        _configuratorsBuilt = true;
        _configurators = new Dictionary<string, System.Action<Item>>(System.StringComparer.OrdinalIgnoreCase);

        // 针剂
        foreach (var (itemKey, _) in WeightedStims)
        {
            var capKey = itemKey;
            var req = new MedicalGrantRequest(capKey, capKey, 1, "WorldContainerLoot");
            _configurators[capKey] = (item) => ConfigureItem(capKey, req, item);
        }

        // 药品
        foreach (var (itemKey, _) in WeightedMedicines)
        {
            var capKey = itemKey;
            var req = new MedicalGrantRequest(capKey, capKey, 1, "WorldContainerLoot");
            _configurators[capKey] = (item) => ConfigureItem(capKey, req, item);
        }
    }

    private static void ConfigureItem(string itemKey, MedicalGrantRequest req, Item item)
    {
        // 针剂
        if (PropitalItemSystem.IsPropitalRequest(req))
            PropitalItemSystem.ConfigureSpawnedItem(item, req);
        else if (PnbItemSystem.IsPnbRequest(req))
            PnbItemSystem.ConfigureSpawnedItem(item, req);
        else if (MuleItemSystem.IsMuleRequest(req))
            MuleItemSystem.ConfigureSpawnedItem(item, req);
        else if (SJ6ItemSystem.IsSJ6Request(req))
            SJ6ItemSystem.ConfigureSpawnedItem(item, req);
        else if (SJ12ItemSystem.IsSJ12Request(req))
            SJ12ItemSystem.ConfigureSpawnedItem(item, req);
        else if (Sj1ItemSystem.IsSj1Request(req))
            Sj1ItemSystem.ConfigureSpawnedItem(item, req);
        else if (Sj9ItemSystem.IsSj9Request(req))
            Sj9ItemSystem.ConfigureSpawnedItem(item, req);
        else if (EtgCItemSystem.IsEtgRequest(req))
            EtgCItemSystem.ConfigureSpawnedItem(item, req);
        else if (ZagustinItemSystem.IsZagustinRequest(req))
            ZagustinItemSystem.ConfigureSpawnedItem(item, req);
        else if (BluebloodItemSystem.IsBluebloodRequest(req))
            BluebloodItemSystem.ConfigureSpawnedItem(item, req);
        else if (Xtg12ItemSystem.IsXtg12Request(req))
            Xtg12ItemSystem.ConfigureSpawnedItem(item, req);
        else if (MildronateItemSystem.IsMildronateRequest(req))
            MildronateItemSystem.ConfigureSpawnedItem(item, req);
        else if (TwoATwoBTGItemSystem.IsTwoATwoBTGRequest(req))
            TwoATwoBTGItemSystem.ConfigureSpawnedItem(item, req);
        else if (MorphineItemSystem.IsMorphineRequest(req))
            MorphineItemSystem.ConfigureSpawnedItem(item, req);
        else if (ObdolbosItemSystem.IsObdolbosRequest(req))
            ObdolbosItemSystem.ConfigureSpawnedItem(item, req);
        else if (Obdolbos2ItemSystem.IsObdolbos2Request(req))
            Obdolbos2ItemSystem.ConfigureSpawnedItem(item, req);
        // 药品
        else if (AI2ItemSystem.IsAi2Request(req))
            AI2ItemSystem.ConfigureSpawnedItem(item, req);
        else if (GoldenStarItemSystem.IsGoldenStarRequest(req))
            GoldenStarItemSystem.ConfigureSpawnedItem(item, req);
        else if (VaselineItemSystem.IsVaselineRequest(req))
            VaselineItemSystem.ConfigureSpawnedItem(item, req);
        else if (LibatineItemSystem.IsLibatineRequest(req))
            LibatineItemSystem.ConfigureSpawnedItem(item, req);
        else if (IbuprofenItemSystem.IsIbuprofenRequest(req))
            IbuprofenItemSystem.ConfigureSpawnedItem(item, req);
        else if (GrizzlyKitItemSystem.IsGrizzlyKitRequest(req))
            GrizzlyKitItemSystem.ConfigureSpawnedItem(item, req);
        else if (AfakKitItemSystem.IsAfakKitRequest(req))
            AfakKitItemSystem.ConfigureSpawnedItem(item, req);
        else if (IfakKitItemSystem.IsIfakKitRequest(req))
            IfakKitItemSystem.ConfigureSpawnedItem(item, req);
        else if (SalewaKitItemSystem.IsSalewaKitRequest(req))
            SalewaKitItemSystem.ConfigureSpawnedItem(item, req);
        else if (MultiToolItemSystem.IsMultiToolRequest(req))
            MultiToolItemSystem.ConfigureSpawnedItem(item, req);
        else if (CmsKitItemSystem.IsCmsRequest(req))
            CmsKitItemSystem.ConfigureSpawnedItem(item, req);
        else
        {
            item.id = itemKey;
            item.SetCondition(1f);
        }
    }
}

/// <summary>
/// 尸体战利品刷新（CorpseScript.Start 时触发）。
/// 尸体生成时，在其附近掉落 0~2 药品。
/// </summary>
[HarmonyPatch(typeof(CorpseScript), nameof(CorpseScript.Start))]
public static class CorpseLootSpawner
{
    private static readonly (string itemKey, int weight)[] WeightedMedicines =
    {
        (AI2ItemSystem.ItemKey, 9),
        (GoldenStarItemSystem.ItemKey, 14),
        (VaselineItemSystem.ItemKey, 14),
        (LibatineItemSystem.ItemKey, 11),
        (IbuprofenItemSystem.ItemKey, 9),
        (GrizzlyKitItemSystem.ItemKey, 4),
        (AfakKitItemSystem.ItemKey, 6),
        (IfakKitItemSystem.ItemKey, 10),
        (SalewaKitItemSystem.ItemKey, 8),
        (MultiToolItemSystem.ItemKey, 5),
        (CmsKitItemSystem.ItemKey, 10),
    };

    private static readonly int TotalMedicineWeight;
    private static Dictionary<string, System.Action<Item>>? _configurators;
    private static bool _configuratorsBuilt;

    static CorpseLootSpawner()
    {
        TotalMedicineWeight = 0;
        foreach (var (_, w) in WeightedMedicines)
            TotalMedicineWeight += w;
    }

    [HarmonyPostfix]
    private static void Postfix(CorpseScript __instance)
    {
        try
        {
            // 尸体：10% 掉 1 药品
            if (Random.value > 0.1f) return;

            int count = 1;

            Plugin.Log.LogInfo($"[WorldLoot] Corpse spawned, dropping {count} medicine(s).");

            BuildConfigurators();

            for (int i = 0; i < count; i++)
            {
                string key = PickWeighted();
                SpawnItem(key, __instance.transform.position);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[WorldLoot] Corpse loot failed: {ex.Message}");
        }
    }

    private static string PickWeighted()
    {
        int roll = Random.Range(1, TotalMedicineWeight + 1);
        int accum = 0;
        foreach (var (key, weight) in WeightedMedicines)
        {
            accum += weight;
            if (roll <= accum) return key;
        }
        return WeightedMedicines[WeightedMedicines.Length - 1].itemKey;
    }

    private static void SpawnItem(string itemKey, Vector3 position)
    {
        try
        {
            Vector2 pos = position + new Vector3(Random.Range(-1f, 1f), Random.Range(0.5f, 2f));
            float rot = Random.Range(0f, 360f);

            var prefab = Resources.Load("bruisekit") as GameObject;
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[WorldLoot] bruisekit prefab not found for '{itemKey}'");
                return;
            }

            var go = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, 0f, rot));
            var item = go.GetComponent<Item>();
            if (item == null)
            {
                Object.Destroy(go);
                return;
            }

            if (_configurators != null && _configurators.TryGetValue(itemKey, out var configurator))
            {
                configurator(item);
            }
            else
            {
                item.id = itemKey;
                item.SetCondition(1f);
            }

            go.AddComponent<FreshItemDrop>();
            Plugin.Log.LogInfo($"[WorldLoot] Spawned {itemKey} at {pos} (from corpse)");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[WorldLoot] Failed to spawn '{itemKey}' from corpse: {ex.Message}");
        }
    }

    private static void BuildConfigurators()
    {
        if (_configuratorsBuilt) return;
        _configuratorsBuilt = true;
        _configurators = new Dictionary<string, System.Action<Item>>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var (itemKey, _) in WeightedMedicines)
        {
            var capKey = itemKey;
            var req = new MedicalGrantRequest(capKey, capKey, 1, "CorpseLoot");
            _configurators[capKey] = (item) => ConfigureItem(capKey, req, item);
        }
    }

    private static void ConfigureItem(string itemKey, MedicalGrantRequest req, Item item)
    {
        if (AI2ItemSystem.IsAi2Request(req))
            AI2ItemSystem.ConfigureSpawnedItem(item, req);
        else if (GoldenStarItemSystem.IsGoldenStarRequest(req))
            GoldenStarItemSystem.ConfigureSpawnedItem(item, req);
        else if (VaselineItemSystem.IsVaselineRequest(req))
            VaselineItemSystem.ConfigureSpawnedItem(item, req);
        else if (LibatineItemSystem.IsLibatineRequest(req))
            LibatineItemSystem.ConfigureSpawnedItem(item, req);
        else if (IbuprofenItemSystem.IsIbuprofenRequest(req))
            IbuprofenItemSystem.ConfigureSpawnedItem(item, req);
        else if (GrizzlyKitItemSystem.IsGrizzlyKitRequest(req))
            GrizzlyKitItemSystem.ConfigureSpawnedItem(item, req);
        else if (AfakKitItemSystem.IsAfakKitRequest(req))
            AfakKitItemSystem.ConfigureSpawnedItem(item, req);
        else if (IfakKitItemSystem.IsIfakKitRequest(req))
            IfakKitItemSystem.ConfigureSpawnedItem(item, req);
        else if (SalewaKitItemSystem.IsSalewaKitRequest(req))
            SalewaKitItemSystem.ConfigureSpawnedItem(item, req);
        else if (MultiToolItemSystem.IsMultiToolRequest(req))
            MultiToolItemSystem.ConfigureSpawnedItem(item, req);
        else if (CmsKitItemSystem.IsCmsRequest(req))
            CmsKitItemSystem.ConfigureSpawnedItem(item, req);
        else
        {
            item.id = itemKey;
            item.SetCondition(1f);
        }
    }
}
