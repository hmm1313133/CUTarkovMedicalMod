using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 跨楼层/存档持久化：确保自定义物品在存档加载后不丢失。
///
/// 根本原因（通过反编译 IL 分析确认）：
/// SaveSystem.TryLoadGame() 使用 Resources.Load(savedItem.id) 来恢复物品。
/// 自定义ID（如 "etg_c"）在 Resources 中没有对应预制体，返回 null，物品被跳过。
///
/// 修复策略（不拦截 Resources.Load，避免破坏 PatchAll）：
/// 1. 在 TryLoadGame 前读取存档，记录哪些 slot 装有自定义物品
/// 2. 在 TryLoadGame 后手动创建丢失的自定义物品并放入对应 slot
/// </summary>
public static class CustomItemReconfigurator
{
    /// <summary>
    /// 自定义物品ID -> 基础游戏物品ID 的映射
    /// </summary>
    private static readonly Dictionary<string, string> CustomToBaseMap = new()
    {
        { EtgCItemSystem.EtgItemKey, EtgCItemSystem.EtgBaseGameItemId },
        { ZagustinItemSystem.ItemKey, ZagustinItemSystem.BaseGameItemId },
        { MorphineItemSystem.ItemKey, MorphineItemSystem.BaseGameItemId },
        { SJ12ItemSystem.ItemKey, SJ12ItemSystem.BaseGameItemId },
        { MuleItemSystem.ItemKey, MuleItemSystem.BaseGameItemId },
        { PropitalItemSystem.ItemKey, PropitalItemSystem.BaseGameItemId },
        { SJ6ItemSystem.ItemKey, SJ6ItemSystem.BaseGameItemId },
        { PnbItemSystem.ItemKey, PnbItemSystem.BaseGameItemId },
        { Sj1ItemSystem.ItemKey, Sj1ItemSystem.BaseGameItemId },
        { ObdolbosItemSystem.ItemKey, ObdolbosItemSystem.BaseGameItemId },
        { Sj9ItemSystem.ItemKey, Sj9ItemSystem.BaseGameItemId },
        { BluebloodItemSystem.ItemKey, BluebloodItemSystem.BaseGameItemId },
        { Xtg12ItemSystem.ItemKey, Xtg12ItemSystem.BaseGameItemId },
        { MildronateItemSystem.ItemKey, MildronateItemSystem.BaseGameItemId },
        { TwoATwoBTGItemSystem.ItemKey, TwoATwoBTGItemSystem.BaseGameItemId },
        { Obdolbos2ItemSystem.ItemKey, Obdolbos2ItemSystem.BaseGameItemId },
        { GrizzlyKitItemSystem.ItemKey, GrizzlyKitItemSystem.BaseGameItemId },
        { AfakKitItemSystem.ItemKey, AfakKitItemSystem.BaseGameItemId },
        { IfakKitItemSystem.ItemKey, IfakKitItemSystem.BaseGameItemId },
        { SalewaKitItemSystem.ItemKey, SalewaKitItemSystem.BaseGameItemId },
        { AI2ItemSystem.ItemKey, AI2ItemSystem.BaseGameItemId },
        { GoldenStarItemSystem.ItemKey, GoldenStarItemSystem.BaseGameItemId },
        { VaselineItemSystem.ItemKey, VaselineItemSystem.BaseGameItemId },
        { LibatineItemSystem.ItemKey, LibatineItemSystem.BaseGameItemId },
        { IbuprofenItemSystem.ItemKey, IbuprofenItemSystem.BaseGameItemId },
        { MultiToolItemSystem.ItemKey, MultiToolItemSystem.BaseGameItemId },
        { CmsKitItemSystem.ItemKey, CmsKitItemSystem.BaseGameItemId },
    };

    /// <summary>
    /// 存档加载时记录的 slot -> customId 映射（由 Prefix 读取存档填充）
    /// </summary>
    private static List<(int slot, string customId, float condition, bool favourited)>? _pendingRestorations;

    // ===== 存档读取 =====

    internal static void ReadCustomItemsFromSave()
    {
        _pendingRestorations = null;

        try
        {
            var savePath = Application.persistentDataPath + "/save.sv";
            if (!File.Exists(savePath))
            {
                Plugin.Log.LogInfo("[Reconfigurator] No save file found, skipping restoration.");
                return;
            }

            var bytes = File.ReadAllBytes(savePath);
            var json = SaveSystem.Unzip(bytes);
            var root = JObject.Parse(json);

            var itemsToken = root["items"];
            if (itemsToken == null) return;

            var items = itemsToken.ToObject<SavedItem[]>();
            if (items == null) return;

            var pending = new List<(int, string, float, bool)>();
            foreach (var saved in items)
            {
                if (saved == null || string.IsNullOrEmpty(saved.id)) continue;
                if (!CustomToBaseMap.ContainsKey(saved.id)) continue;

                pending.Add((saved.slot, saved.id, saved.condition, saved.favourited));
                Plugin.Log.LogInfo($"[Reconfigurator] Save has custom item: slot={saved.slot}, id={saved.id}, cond={saved.condition}");
            }

            _pendingRestorations = pending.Count > 0 ? pending : null;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Reconfigurator] ReadCustomItemsFromSave failed: {ex.Message}");
        }
    }

    private sealed class SavedItem
    {
        public string id = "";
        public float condition;
        public int slot;
        public string wearSlot = "";
        public bool favourited;
    }

    // ===== 存档加载后恢复 =====

    /// <summary>
    /// 在 TryLoadGame 完成后，根据 _pendingRestorations 恢复丢失的自定义物品。
    /// TryLoadGame 使用 Resources.Load(id) 加载物品，自定义ID无预制体导致物品丢失。
    /// 这里手动创建基础预制体实例，设置自定义ID并配置。
    /// </summary>
    private static void ApplyPendingRestorations()
    {
        if (_pendingRestorations == null || _pendingRestorations.Count == 0) return;

        try
        {
            var body = GetPlayerBody();
            if (body == null)
            {
                Plugin.Log.LogWarning("[Reconfigurator] Body not found for restoration.");
                return;
            }

            int restored = 0;
            foreach (var (slot, customId, condition, favourited) in _pendingRestorations)
            {
                if (slot < 0) continue; // 跳过穿戴物品

                // 检查 slot 是否已有正确的自定义物品（可能 TryLoadGame 成功加载了）
                if (body.HoldingItem(slot))
                {
                    var existing = body.GetItem(slot);
                    if (existing != null && existing.id == customId)
                    {
                        // 物品已存在且ID正确，只需重新配置标记和图标
                        ReconfigureItem(existing);
                        Plugin.Log.LogInfo($"[Reconfigurator] Slot {slot} already has '{customId}', reconfigured.");
                        restored++;
                        continue;
                    }

                    // slot 被其他物品占用（可能是基础ID的物品），检查是否需要替换
                    if (existing != null && CustomToBaseMap.TryGetValue(customId, out var baseId) && existing.id == baseId)
                    {
                        // slot 里是基础预制体（如 syringe），恢复自定义ID
                        existing.id = customId;
                        existing.condition = condition;
                        existing.favourited = favourited;
                        ReconfigureItem(existing);
                        Plugin.Log.LogInfo($"[Reconfigurator] Restored '{customId}' in slot {slot} (was base '{baseId}').");
                        restored++;
                        continue;
                    }

                    // slot 被其他物品占用，检查是否是容器内的子物品
                    var container = existing?.GetComponent<Container>();
                    if (container != null)
                    {
                        // 容器内可能有丢失的子物品，跳过（后续会处理）
                        Plugin.Log.LogInfo($"[Reconfigurator] Slot {slot} has container, checking children for '{customId}'.");
                        if (TryRestoreInContainer(container, customId, condition, favourited, body))
                        {
                            restored++;
                            continue;
                        }
                    }
                }

                // slot 为空或被不相关物品占用 - 创建新物品
                var created = CreateCustomItem(customId, condition, favourited, body);
                if (created != null)
                {
                    // 尝试放入 slot
                    try
                    {
                        body.PickUpItem(created, slot, true);
                        Plugin.Log.LogInfo($"[Reconfigurator] Created and placed '{customId}' in slot {slot}.");
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[Reconfigurator] Failed to place '{customId}' in slot {slot}: {ex.Message}");
                    }
                }
            }

            Plugin.Log.LogInfo($"[Reconfigurator] Restored {restored}/{_pendingRestorations.Count} custom items from save.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Reconfigurator] ApplyPendingRestorations failed: {ex.Message}");
        }
        finally
        {
            _pendingRestorations = null;
        }
    }

    /// <summary>
    /// 尝试在容器中恢复自定义物品（如 medkit 中的针剂）。
    /// </summary>
    private static bool TryRestoreInContainer(Container container, string customId, float condition, bool favourited, Body body)
    {
        try
        {
            // 检查容器子物品中是否已有该自定义物品
            for (int i = 0; i < container.transform.childCount; i++)
            {
                var child = container.transform.GetChild(i);
                var item = child.GetComponent<Item>();
                if (item == null) continue;

                if (item.id == customId)
                {
                    ReconfigureItem(item);
                    return true;
                }

                // 如果是基础ID，替换为自定义ID
                if (CustomToBaseMap.TryGetValue(customId, out var baseId) && item.id == baseId)
                {
                    item.id = customId;
                    item.condition = condition;
                    item.favourited = favourited;
                    ReconfigureItem(item);
                    return true;
                }
            }

            // 容器中没有该物品 - 创建新的并装入
            var created = CreateCustomItem(customId, condition, favourited, body);
            if (created != null)
            {
                created.transform.position = container.transform.position;
                container.LoadItem(created);
                Plugin.Log.LogInfo($"[Reconfigurator] Created '{customId}' and loaded into container.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Reconfigurator] TryRestoreInContainer failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// 创建自定义物品实例：加载基础预制体 -> 设置自定义ID -> 配置标记和图标。
    /// </summary>
    private static Item? CreateCustomItem(string customId, float condition, bool favourited, Body body)
    {
        try
        {
            if (!CustomToBaseMap.TryGetValue(customId, out var baseId)) return null;

            var prefab = Resources.Load<GameObject>(baseId);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[Reconfigurator] Base prefab '{baseId}' not found for '{customId}'.");
                return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab, body.transform.position, Quaternion.identity);
            var item = go.GetComponent<Item>();
            if (item == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }

            // 设置自定义ID和属性
            item.id = customId;
            item.condition = condition;
            item.favourited = favourited;

            // 配置标记和图标
            ReconfigureItem(item);

            return item;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Reconfigurator] CreateCustomItem('{customId}') failed: {ex.Message}");
            return null;
        }
    }

    // ===== 单个物品重新配置 =====

    public static void ReconfigureItem(Item item)
    {
        if (item == null) return;

        var id = item.id;
        if (string.IsNullOrEmpty(id)) return;

        try
        {
            var request = new MedicalGrantRequest(id, id, 1, "Reconfigure", null);

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
            else if (GrizzlyKitItemSystem.IsGrizzlyKitRequest(request))
                GrizzlyKitItemSystem.ConfigureSpawnedItem(item, request);
            else if (AfakKitItemSystem.IsAfakKitRequest(request))
                AfakKitItemSystem.ConfigureSpawnedItem(item, request);
            else if (IfakKitItemSystem.IsIfakKitRequest(request))
                IfakKitItemSystem.ConfigureSpawnedItem(item, request);
            else if (SalewaKitItemSystem.IsSalewaKitRequest(request))
                SalewaKitItemSystem.ConfigureSpawnedItem(item, request);
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
            else if (MultiToolItemSystem.IsMultiToolRequest(request))
                MultiToolItemSystem.ConfigureSpawnedItem(item, request);
            else if (CmsKitItemSystem.IsCmsRequest(request))
                CmsKitItemSystem.ConfigureSpawnedItem(item, request);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Reconfigurator] Failed to reconfigure item '{id}': {ex.Message}");
        }
    }

    public static bool IsCustomItemId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        return CustomToBaseMap.ContainsKey(id);
    }

    // ===== 楼层切换后批量重新配置 =====

    private static int _pendingReconfigureFrames;
    private static bool _reconfigureScheduled;

    public static void ScheduleReconfigure(int delayFrames = 60)
    {
        _pendingReconfigureFrames = delayFrames;
        _reconfigureScheduled = true;
    }

    public static void Tick(ManualLogSource log)
    {
        if (!_reconfigureScheduled) return;

        if (_pendingReconfigureFrames > 0)
        {
            _pendingReconfigureFrames--;
            return;
        }

        _reconfigureScheduled = false;
        ReconfigureAllInventoryItems(log);
    }

    public static void ReconfigureAllInventoryItems(ManualLogSource log)
    {
        try
        {
            var body = GetPlayerBody();
            if (body == null)
            {
                log.LogWarning("[Reconfigurator] Player body not found, skipping reconfiguration.");
                return;
            }

            // 首先处理存档恢复（slot -> customId 映射）
            ApplyPendingRestorations();

            var items = body.GetAllItemsThorough();
            if (items == null || items.Count == 0)
            {
                log.LogInfo("[Reconfigurator] No items found in inventory.");
                return;
            }

            int reconfigured = 0;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (!IsCustomItemId(item.id)) continue;

                ReconfigureItem(item);
                reconfigured++;
            }

            if (reconfigured > 0)
                log.LogInfo($"[Reconfigurator] Reconfigured {reconfigured} custom item(s) after floor transition.");
        }
        catch (Exception ex)
        {
            log.LogWarning($"[Reconfigurator] ReconfigureAllInventoryItems failed: {ex.Message}");
        }
    }

    private static Body? GetPlayerBody()
    {
        try
        {
            var world = WorldGeneration.world;
            if (world != null && world.body != null)
                return world.body;
        }
        catch { }

        try
        {
            if (PlayerCamera.main != null && PlayerCamera.main.body != null)
                return PlayerCamera.main.body;
        }
        catch { }

        return null;
    }

    public static void EnsureAllCustomItemsRegistered()
    {
        EtgCItemSystem.EnsureRegisteredInItemTable();
        ZagustinItemSystem.EnsureRegisteredInItemTable();
        MorphineItemSystem.EnsureRegisteredInItemTable();
        SJ12ItemSystem.EnsureRegisteredInItemTable();
        MuleItemSystem.EnsureRegisteredInItemTable();
        PropitalItemSystem.EnsureRegisteredInItemTable();
        SJ6ItemSystem.EnsureRegisteredInItemTable();
        Sj1ItemSystem.EnsureRegisteredInItemTable();
        PnbItemSystem.EnsureRegisteredInItemTable();
        ObdolbosItemSystem.EnsureRegisteredInItemTable();
        Sj9ItemSystem.EnsureRegisteredInItemTable();
        BluebloodItemSystem.EnsureRegisteredInItemTable();
        Xtg12ItemSystem.EnsureRegisteredInItemTable();
        MildronateItemSystem.EnsureRegisteredInItemTable();
        TwoATwoBTGItemSystem.EnsureRegisteredInItemTable();
        Obdolbos2ItemSystem.EnsureRegisteredInItemTable();
        GrizzlyKitItemSystem.EnsureRegisteredInItemTable();
        AfakKitItemSystem.EnsureRegisteredInItemTable();
        IfakKitItemSystem.EnsureRegisteredInItemTable();
        SalewaKitItemSystem.EnsureRegisteredInItemTable();
        AI2ItemSystem.EnsureRegisteredInItemTable();
        GoldenStarItemSystem.EnsureRegisteredInItemTable();
        VaselineItemSystem.EnsureRegisteredInItemTable();
        LibatineItemSystem.EnsureRegisteredInItemTable();
        IbuprofenItemSystem.EnsureRegisteredInItemTable();
        MultiToolItemSystem.EnsureRegisteredInItemTable();
        CmsKitItemSystem.EnsureRegisteredInItemTable();
    }
}

/// <summary>
/// 拦截 SaveSystem.TryLoadGame：
/// - Prefix: 读取存档，记录哪些 slot 有自定义物品
/// - Postfix: 延迟恢复丢失的自定义物品
/// </summary>
[HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.TryLoadGame))]
public static class SaveSystemTryLoadPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        Plugin.Log.LogInfo("[SaveSystem.TryLoadGame] Prefix: reading custom items from save...");
        CustomItemReconfigurator.ReadCustomItemsFromSave();
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        Plugin.Log.LogInfo("[SaveSystem.TryLoadGame] Postfix: scheduling restoration...");
        CustomItemReconfigurator.ScheduleReconfigure(120);
    }
}

/// <summary>
/// 拦截 WorldGeneration.Start，在楼层切换/新游戏时触发物品重新配置。
/// </summary>
[HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.Start))]
public static class FloorTransitionReconfigurePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        CustomItemReconfigurator.ScheduleReconfigure(120);
        Plugin.Log.LogInfo("[Reconfigurator] WorldGeneration.Start detected, scheduled item reconfiguration.");
    }
}
