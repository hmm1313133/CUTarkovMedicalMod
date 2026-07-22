using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 医疗箱专属针剂刷新系统。
/// 当医疗箱被破坏时，30% 概率随机掉 0~3 根模组针剂（按权重分布）。
/// </summary>
[HarmonyPatch(typeof(BuildingEntity), nameof(BuildingEntity.Update))]
public static class MedcrateStimSpawner
{
    /// <summary>权重配置：总权重 95</summary>
    private static readonly (string itemKey, int weight)[] WeightedStims =
    {
        (MorphineItemSystem.ItemKey, 12),
        (Sj1ItemSystem.ItemKey, 6),
        (PropitalItemSystem.ItemKey, 7),
        (SJ6ItemSystem.ItemKey, 6),
        (Sj9ItemSystem.ItemKey, 5),
        (EtgCItemSystem.EtgItemKey, 4),
        (MildronateItemSystem.ItemKey, 6),
        (PnbItemSystem.ItemKey, 7),
        (ObdolbosItemSystem.ItemKey, 5),
        (Obdolbos2ItemSystem.ItemKey, 4),
        (SJ12ItemSystem.ItemKey, 8),
        (BluebloodItemSystem.ItemKey, 5),
        (MuleItemSystem.ItemKey, 5),
        (ZagustinItemSystem.ItemKey, 5),
        (Xtg12ItemSystem.ItemKey, 5),
        (TwoATwoBTGItemSystem.ItemKey, 5),
    };

    private static readonly int TotalWeight;
    private static Dictionary<string, System.Action<Item>>? _configurators;
    private static bool _configuratorsBuilt;
    private static readonly HashSet<int> _processed = new();

    static MedcrateStimSpawner()
    {
        TotalWeight = 0;
        foreach (var (_, w) in WeightedStims)
            TotalWeight += w;
    }

    [HarmonyPrefix]
    private static void Prefix(BuildingEntity __instance)
    {
        // 多人模式下仅主机执行医疗箱针剂生成
        if (!KrokMpHelper.ShouldSpawnLoot) return;

        if (__instance.health >= 0.5f) return;

        int instanceId = __instance.GetInstanceID();
        if (_processed.Contains(instanceId)) return;
        _processed.Add(instanceId);

        string goName = __instance.gameObject.name.ToLowerInvariant();
        string buildingId = (__instance.id ?? "").ToLowerInvariant();
        if (!goName.Contains("medcrate") && buildingId != "medcrate") return;

        Plugin.Log.LogInfo($"[MedcrateStimSpawner] Medcrate detected (name='{__instance.gameObject.name}', id='{__instance.id}'), rolling 30%...");

        if (Random.value > 0.3f)
        {
            Plugin.Log.LogInfo("[MedcrateStimSpawner] Roll failed, no stims dropped.");
            return;
        }

        int count = Random.Range(0, 4);
        Plugin.Log.LogInfo($"[MedcrateStimSpawner] Roll success! Dropping {count} stim(s).");
        for (int i = 0; i < count; i++)
        {
            string key = PickWeighted();
            if (string.IsNullOrEmpty(key)) continue;
            Plugin.Log.LogInfo($"[MedcrateStimSpawner] Spawning stim: {key}");
            SpawnItem(key, __instance.transform.position);
        }
    }

    private static string PickWeighted()
    {
        int roll = Random.Range(1, TotalWeight + 1);
        int accum = 0;
        foreach (var (key, weight) in WeightedStims)
        {
            accum += weight;
            if (roll <= accum) return key;
        }
        return WeightedStims[WeightedStims.Length - 1].itemKey;
    }

    private static void SpawnItem(string itemKey, Vector3 position)
    {
        try
        {
            BuildConfigurators();

            Vector2 pos = position + new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(1f, 3f));
            float rot = Random.Range(0f, 360f);

            // 使用 Utils.Create 代替 Resources.Load + Instantiate，
            // 使 CUCoreLib 拦截创建自定义物品，并触发 KrokMP 网络注册
            var go = Utils.Create(itemKey, pos, rot);
            if (go == null)
            {
                Plugin.Log.LogWarning($"[MedcrateStimSpawner] Utils.Create failed for '{itemKey}'");
                return;
            }

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
            Plugin.Log.LogInfo($"[MedcrateStimSpawner] Spawned {itemKey} at {pos}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[MedcrateStimSpawner] Failed to spawn '{itemKey}': {ex.Message}");
        }
    }

    private static void BuildConfigurators()
    {
        if (_configuratorsBuilt) return;
        _configuratorsBuilt = true;
        _configurators = new Dictionary<string, System.Action<Item>>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var (itemKey, _) in WeightedStims)
        {
            var capKey = itemKey; // capture for closure
            var req = new MedicalGrantRequest(capKey, capKey, 1, "MedcrateSpawn");
            _configurators[capKey] = (item) => ConfigureItem(capKey, req, item);
        }
    }

    private static void ConfigureItem(string itemKey, MedicalGrantRequest req, Item item)
    {
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
        else
        {
            item.id = itemKey;
            item.SetCondition(1f);
        }
    }
}
