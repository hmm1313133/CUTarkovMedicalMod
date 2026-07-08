using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 让游戏控制台 spawn 命令支持 mod 自定义物品。
///
/// 问题：控制台 spawn 命令通过 Resources.Load(itemId) 加载预制体，
/// 但自定义物品没有 Resources 预制体，所以 spawn 失败。
///
/// 修复：
/// 1. Patch RegisterSpawnEntities — 将自定义物品 ID 加入 spawn 命令的自动补全
/// 2. Patch TryExecuteCommand — 拦截 spawn <自定义物品ID>，用 mod 逻辑生成物品
/// </summary>
public static class ConsoleSpawnPatch
{
    /// <summary>自定义物品 ID → 基础预制体名 的映射</summary>
    internal static readonly Dictionary<string, string> CustomItemPrefabs = new(StringComparer.OrdinalIgnoreCase)
    {
        // 针剂类（syringe 预制体）
        { EtgCItemSystem.EtgItemKey, "syringe" },
        { ZagustinItemSystem.ItemKey, "syringe" },
        { MorphineItemSystem.ItemKey, "syringe" },
        { SJ12ItemSystem.ItemKey, "syringe" },
        { MuleItemSystem.ItemKey, "syringe" },
        { PropitalItemSystem.ItemKey, "syringe" },
        { SJ6ItemSystem.ItemKey, "syringe" },
        { PnbItemSystem.ItemKey, "syringe" },
        { Sj1ItemSystem.ItemKey, "syringe" },
        { ObdolbosItemSystem.ItemKey, "syringe" },
        { Sj9ItemSystem.ItemKey, "syringe" },
        { BluebloodItemSystem.ItemKey, "syringe" },
        { Xtg12ItemSystem.ItemKey, "syringe" },
        { MildronateItemSystem.ItemKey, "syringe" },
        { TwoATwoBTGItemSystem.ItemKey, "syringe" },
        { Obdolbos2ItemSystem.ItemKey, "syringe" },
        // 药品类（bruisekit 预制体）
        { AI2ItemSystem.ItemKey, "syringe" },
        { GoldenStarItemSystem.ItemKey, "bruisekit" },
        { VaselineItemSystem.ItemKey, "bruisekit" },
        { LibatineItemSystem.ItemKey, "bruisekit" },
        { IbuprofenItemSystem.ItemKey, "bruisekit" },
        { GrizzlyKitItemSystem.ItemKey, "bruisekit" },
        { AfakKitItemSystem.ItemKey, "bruisekit" },
        { IfakKitItemSystem.ItemKey, "bruisekit" },
        { SalewaKitItemSystem.ItemKey, "bruisekit" },
        { MultiToolItemSystem.ItemKey, "bruisekit" },
        { CmsKitItemSystem.ItemKey, "bruisekit" },
    };

    /// <summary>判断是否为自定义物品 ID</summary>
    public static bool IsCustomItemKey(string itemId)
        => CustomItemPrefabs.ContainsKey(itemId);

    /// <summary>
    /// 生成自定义物品到玩家附近。
    /// </summary>
    public static bool TrySpawnCustomItem(string itemId)
    {
        if (!CustomItemPrefabs.TryGetValue(itemId, out var prefabName))
            return false;

        try
        {
            var body = WorldGeneration.world?.body;
            if (body == null)
            {
                Plugin.Log.LogWarning("[ConsoleSpawn] No body found.");
                return false;
            }

            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[ConsoleSpawn] Prefab '{prefabName}' not found for '{itemId}'.");
                return false;
            }

            var go = UnityEngine.Object.Instantiate(prefab);
            var item = go.GetComponent<Item>();
            if (item == null)
            {
                UnityEngine.Object.Destroy(go);
                return false;
            }

            // 配置自定义物品
            var request = new MedicalGrantRequest(itemId, itemId, 1, "ConsoleSpawn", prefabName);
            ConfigureCustomItem(item, request);

            // 放到玩家附近
            go.transform.position = body.transform.position + new Vector3(0.5f, 0f, 0f);
            go.AddComponent<FreshItemDrop>();

            Plugin.Log.LogInfo($"[ConsoleSpawn] Spawned custom item '{itemId}' at {go.transform.position}.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ConsoleSpawn] Failed to spawn '{itemId}': {ex.Message}");
            return false;
        }
    }

    private static void ConfigureCustomItem(Item item, MedicalGrantRequest request)
    {
        if (EtgCItemSystem.IsEtgRequest(request))
            EtgCItemSystem.ConfigureSpawnedItem(item, request);
        else if (ZagustinItemSystem.IsZagustinRequest(request))
            ZagustinItemSystem.ConfigureSpawnedItem(item, request);
        else if (MorphineItemSystem.IsMorphineRequest(request))
            MorphineItemSystem.ConfigureSpawnedItem(item, request);
        else if (SJ12ItemSystem.IsSJ12Request(request))
            SJ12ItemSystem.ConfigureSpawnedItem(item, request);
        else if (MuleItemSystem.IsMuleRequest(request))
            MuleItemSystem.ConfigureSpawnedItem(item, request);
        else if (PropitalItemSystem.IsPropitalRequest(request))
            PropitalItemSystem.ConfigureSpawnedItem(item, request);
        else if (SJ6ItemSystem.IsSJ6Request(request))
            SJ6ItemSystem.ConfigureSpawnedItem(item, request);
        else if (PnbItemSystem.IsPnbRequest(request))
            PnbItemSystem.ConfigureSpawnedItem(item, request);
        else if (Sj1ItemSystem.IsSj1Request(request))
            Sj1ItemSystem.ConfigureSpawnedItem(item, request);
        else if (ObdolbosItemSystem.IsObdolbosRequest(request))
            ObdolbosItemSystem.ConfigureSpawnedItem(item, request);
        else if (Sj9ItemSystem.IsSj9Request(request))
            Sj9ItemSystem.ConfigureSpawnedItem(item, request);
        else if (BluebloodItemSystem.IsBluebloodRequest(request))
            BluebloodItemSystem.ConfigureSpawnedItem(item, request);
        else if (Xtg12ItemSystem.IsXtg12Request(request))
            Xtg12ItemSystem.ConfigureSpawnedItem(item, request);
        else if (MildronateItemSystem.IsMildronateRequest(request))
            MildronateItemSystem.ConfigureSpawnedItem(item, request);
        else if (TwoATwoBTGItemSystem.IsTwoATwoBTGRequest(request))
            TwoATwoBTGItemSystem.ConfigureSpawnedItem(item, request);
        else if (Obdolbos2ItemSystem.IsObdolbos2Request(request))
            Obdolbos2ItemSystem.ConfigureSpawnedItem(item, request);
        else if (AI2ItemSystem.IsAi2Request(request))
            AI2ItemSystem.ConfigureSpawnedItem(item, request);
        else if (GoldenStarItemSystem.IsGoldenStarRequest(request))
            GoldenStarItemSystem.ConfigureSpawnedItem(item, request);
        else if (VaselineItemSystem.IsVaselineRequest(request))
            VaselineItemSystem.ConfigureSpawnedItem(item, request);
        else if (LibatineItemSystem.IsLibatineRequest(request))
            LibatineItemSystem.ConfigureSpawnedItem(item, request);
        else if (IbuprofenItemSystem.IsIbuprofenRequest(request))
            IbuprofenItemSystem.ConfigureSpawnedItem(item, request);
        else if (GrizzlyKitItemSystem.IsGrizzlyKitRequest(request))
            GrizzlyKitItemSystem.ConfigureSpawnedItem(item, request);
        else if (AfakKitItemSystem.IsAfakKitRequest(request))
            AfakKitItemSystem.ConfigureSpawnedItem(item, request);
        else if (IfakKitItemSystem.IsIfakKitRequest(request))
            IfakKitItemSystem.ConfigureSpawnedItem(item, request);
        else if (SalewaKitItemSystem.IsSalewaKitRequest(request))
            SalewaKitItemSystem.ConfigureSpawnedItem(item, request);
        else if (MultiToolItemSystem.IsMultiToolRequest(request))
            MultiToolItemSystem.ConfigureSpawnedItem(item, request);
        else if (CmsKitItemSystem.IsCmsRequest(request))
            CmsKitItemSystem.ConfigureSpawnedItem(item, request);
        else
        {
            item.id = request.ItemKey;
            item.SetCondition(1f);
        }
    }
}

/// <summary>
/// 拦截控制台 TryExecuteCommand，处理 spawn <自定义物品ID>。
/// </summary>
[HarmonyPatch(typeof(ConsoleScript), nameof(ConsoleScript.TryExecuteCommand))]
public static class ConsoleTryExecuteCommandPatch
{
    private static readonly FieldInfo? CommandsField =
        AccessTools.Field(typeof(ConsoleScript), "Commands");

    [HarmonyPrefix]
    private static bool Prefix(ConsoleScript __instance, string[] args, bool addToLog)
    {
        if (args == null || args.Length < 2)
            return true; // 让原方法处理

        string? cmd = args[0]?.ToLowerInvariant();
        if (cmd != "spawn")
            return true; // 非 spawn 命令，让原方法处理

        string itemId = args[1];
        if (!ConsoleSpawnPatch.IsCustomItemKey(itemId))
            return true; // 非自定义物品，让原方法处理

        // 拦截：用 mod 逻辑生成自定义物品
        bool success = ConsoleSpawnPatch.TrySpawnCustomItem(itemId);

        // 记录到控制台日志
        try
        {
            var logMethod = AccessTools.Method(typeof(ConsoleScript), "LogToConsole");
            logMethod?.Invoke(__instance, new object[] { success ? $"Spawned: {itemId}" : $"Failed to spawn: {itemId}" });
        }
        catch { }

        return false; // 跳过原方法
    }
}

/// <summary>
/// 在 RegisterSpawnEntities 后注入自定义物品 ID 到 spawn 命令的自动补全。
/// </summary>
[HarmonyPatch(typeof(ConsoleScript), nameof(ConsoleScript.RegisterSpawnEntities))]
public static class ConsoleRegisterSpawnEntitiesPatch
{
    [HarmonyPostfix]
    private static void Postfix(ConsoleScript __instance)
    {
        try
        {
            var commandsField = AccessTools.Field(typeof(ConsoleScript), "Commands");
            if (commandsField == null) return;

            var commands = commandsField.GetValue(__instance) as List<Command>;
            if (commands == null) return;

            // 找到 spawn 命令
            Command? spawnCmd = null;
            foreach (var cmd in commands)
            {
                if (string.Equals(cmd.name, "spawn", StringComparison.OrdinalIgnoreCase))
                {
                    spawnCmd = cmd;
                    break;
                }
            }
            if (spawnCmd == null) return;

            // 将自定义物品 ID 加入 argAutofill
            var autofillField = AccessTools.Field(typeof(Command), "argAutofill");
            if (autofillField == null) return;

            var autofill = autofillField.GetValue(spawnCmd) as Dictionary<string, List<string>>;
            if (autofill == null) return;

            // argAutofill 的 key 通常是参数名（如 "item"），value 是候选项列表
            foreach (var kv in autofill)
            {
                var candidates = kv.Value;
                foreach (var itemId in ConsoleSpawnPatch.CustomItemPrefabs.Keys)
                {
                    if (!candidates.Contains(itemId))
                        candidates.Add(itemId);
                }
                Plugin.Log.LogInfo($"[ConsoleSpawn] Added {ConsoleSpawnPatch.CustomItemPrefabs.Count} custom items to spawn autofill '{kv.Key}'.");
                break; // 只处理第一个参数的 autofill
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ConsoleSpawn] RegisterSpawnEntities postfix failed: {ex.Message}");
        }
    }
}
