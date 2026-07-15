using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 让游戏控制台 spawn 命令支持 mod 自定义物品。
///
/// CUCoreLib 通过 Prefix 补丁拦截 Utils.Create，当 Resources.Load 找不到物品 ID 时，
/// 从 ItemRegistry 创建模板（CustomInstantiate.InstantiateReturn）。
/// 医疗模组在 OnItemsSetup 中将所有自定义物品注册到 ItemRegistry，
/// 因此 vanilla spawn 命令通过 Utils.Create 即可正确生成自定义物品，
/// 同时确保 KrokMP 多人模式下的物品网络同步正常工作。
///
/// 本类仅负责：
/// 1. Patch RegisterSpawnEntities - 将自定义物品 ID 加入 spawn 命令的自动补全
/// 2. 提供 CustomItemPrefabs 映射和 ConfigureCustomItem 供其他系统（起始装备/世界掉落）使用
/// </summary>
public static class ConsoleSpawnPatch
{
    /// <summary>自定义物品 ID -> 基础预制体名 的映射</summary>
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
    /// 注意：此方法仅用于调试用途，不经过 Utils.Create/CUCoreLib 模板系统，
    /// 因此在多人模式中不会同步。控制台 spawn 命令由 CUCoreLib 的 Utils.Create
    /// prefix 自动处理。
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

    /// <summary>
    /// 外部物品配置器（供其他mod注册自定义物品配置逻辑）。
    /// 返回 true 表示已处理，false 表示不是该mod的物品。
    /// </summary>
    internal static Func<Item, MedicalGrantRequest, bool>? ExternalItemConfigurer { get; set; }

    internal static void ConfigureCustomItem(Item item, MedicalGrantRequest request)
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
        else if (ExternalItemConfigurer != null && ExternalItemConfigurer(item, request))
            return; // handled by external mod (e.g. weapon mod)
        else
        {
            item.id = request.ItemKey;
            item.SetCondition(1f);
        }
    }
}

/// <summary>
/// 在 RegisterSpawnEntities 后注入自定义物品 ID 到 spawn 命令的自动补全。
/// CUCoreLib 的 ConsolePatch 也会注入 ItemRegistry 中的物品到 spawn 和 cuspawn 的自动补全，
/// 此 Postfix 作为补充确保所有自定义物品（包括武器模组通过 CustomItemPrefabs 注册的）都出现在补全列表中。
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

            // argAutofill 类型是 Dictionary<int, List<string>>，key 0 = 第一个参数的候选项
            var autofill = spawnCmd.argAutofill;
            if (autofill == null)
            {
                autofill = new Dictionary<int, List<string>>();
                spawnCmd.argAutofill = autofill;
            }

            if (!autofill.TryGetValue(0, out var candidates))
            {
                candidates = new List<string>();
                autofill[0] = candidates;
            }

            foreach (var itemId in ConsoleSpawnPatch.CustomItemPrefabs.Keys)
            {
                if (!candidates.Contains(itemId))
                    candidates.Add(itemId);
            }
            Plugin.Log.LogInfo($"[ConsoleSpawn] Added {ConsoleSpawnPatch.CustomItemPrefabs.Count} custom items to spawn autofill.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[ConsoleSpawn] RegisterSpawnEntities postfix failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Utils.Create Postfix 后备：当 CUCoreLib 的 prefix 未处理自定义物品时（例如客户端
/// ItemRegistry 未注册或 ChooseTemplateId 失败），从基础预制体手动创建并配置。
///
/// 调用链：
/// 1. CUCoreLib UtilsCreatePatches.CreateItemFallback (Prefix)：
///    - Resources.Load(id) != null -> return true (vanilla 处理)
///    - ItemRegistry.RegisteredItems.ContainsKey(id) -> CustomInstantiate, return false
///    - 否则 -> return true (vanilla 运行，Instantiate(null) = 报错)
/// 2. vanilla Utils.Create：Instantiate(Resources.Load(id)) -> null + Unity 报错
/// 3. 本 Postfix：__result == null 且 id 是自定义物品 -> 从基础预制体创建
/// </summary>
[HarmonyPatch(typeof(Utils), nameof(Utils.Create), typeof(string), typeof(Vector2), typeof(float))]
public static class UtilsCreateFallbackPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(string id, Vector2 pos, float rot, ref GameObject __result)
    {
        if (__result != null) return;
        if (string.IsNullOrEmpty(id)) return;
        if (!ConsoleSpawnPatch.CustomItemPrefabs.TryGetValue(id, out var prefabName)) return;

        try
        {
            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[Utils.Create Fallback] Base prefab '{prefabName}' not found for '{id}'.");
                return;
            }

            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0f, 0f, rot));
            var item = go.GetComponent<Item>();
            if (item != null)
            {
                var request = new MedicalGrantRequest(id, id, 1, "ConsoleSpawn", prefabName);
                ConsoleSpawnPatch.ConfigureCustomItem(item, request);
            }
            go.SetActive(true);
            __result = go;
            Plugin.Log.LogInfo($"[Utils.Create Fallback] Created custom item '{id}' from prefab '{prefabName}'.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Utils.Create Fallback] Failed for '{id}': {ex.Message}");
        }
    }
}
