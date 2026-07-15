using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CUTarkovMedicalMod.Integration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 桥接 KrokMP 的 PlayerSavedState，为断线重连的玩家保存/恢复医疗效果。
///
/// KrokMP 的 PlayerSavedState 是服务器内存中的临时数据，
/// 玩家断开时创建，重连时恢复，场景切换时清空。
/// 它保存了位置、健康、背包物品，但不保存医疗效果（EffectController）。
/// 本类通过 side-car Dictionary 镜像 PlayerSavedState 的生命周期，
/// 在玩家断开时序列化医疗效果，重连时恢复到新 Body。
///
/// 仅在主机端运行（PlayerSavedState 是服务器内存数据）。
/// </summary>
public static class KrokMpStateBridge
{
    private static readonly Dictionary<string, string> _savedEffects = new();
    private static bool _installed;

    private static Type? _netPlayerType;
    private static Type? _netBodyType;
    private static FieldInfo? _npBodyField;
    private static MethodInfo? _npGetPersistentId;
    // NetBody.is_player 和 NetBody.plr 是属性（property），不是字段
    private static PropertyInfo? _nbIsPlayerProp;
    private static PropertyInfo? _nbPlrProp;
    private static MethodInfo? _plrGetPersistentId;

    /// <summary>
    /// 安装 Harmony 补丁。仅在 KrokMP 已安装时生效。
    /// </summary>
    public static void Install(Harmony harmony)
    {
        if (_installed) return;
        if (!KrokMpHelper.IsKrokMpInstalled) return;

        _netPlayerType = AccessTools.TypeByName("KrokoshaCasualtiesMP.NetPlayer");
        _netBodyType = AccessTools.TypeByName("KrokoshaCasualtiesMP.NetBody");

        if (_netPlayerType == null)
        {
            Plugin.Log.LogWarning("[KrokMpStateBridge] NetPlayer type not found, skipping.");
            return;
        }

        // NetPlayer.body 是字段
        _npBodyField = AccessTools.Field(_netPlayerType, "body");
        _npGetPersistentId = AccessTools.Method(_netPlayerType, "GetPersistentId");

        if (_netBodyType != null)
        {
            // NetBody.is_player 和 NetBody.plr 是属性（property），不是字段！
            _nbIsPlayerProp = AccessTools.Property(_netBodyType, "is_player");
            _nbPlrProp = AccessTools.Property(_netBodyType, "plr");
        }

        var ok = _npBodyField != null && _npGetPersistentId != null
            && _nbIsPlayerProp != null && _nbPlrProp != null;
        if (!ok)
        {
            Plugin.Log.LogWarning($"[KrokMpStateBridge] Reflection failed: npBody={_npBodyField != null}, npGetPid={_npGetPersistentId != null}, nbIsPlayer={_nbIsPlayerProp != null}, nbPlr={_nbPlrProp != null}");
        }

        // Patch NetPlayer.OnDestroy (Prefix - body 还未被置 null)
        var onDestroy = AccessTools.Method(_netPlayerType, "OnDestroy");
        if (onDestroy != null)
        {
            harmony.Patch(onDestroy,
                prefix: new HarmonyMethod(typeof(KrokMpStateBridge), nameof(NetPlayer_OnDestroy_Prefix)));
            Plugin.Log.LogInfo("[KrokMpStateBridge] Patched NetPlayer.OnDestroy (Prefix).");
        }

        // Patch Body.Start (Postfix, 低优先级确保在 KrokMP 的 Postfix 之后执行)
        var bodyStart = AccessTools.Method(typeof(Body), "Start");
        if (bodyStart != null)
        {
            harmony.Patch(bodyStart,
                postfix: new HarmonyMethod(typeof(KrokMpStateBridge), nameof(Body_Start_Postfix)) { priority = Priority.Low });
            Plugin.Log.LogInfo("[KrokMpStateBridge] Patched Body.Start (Postfix, low priority).");
        }

        // 场景加载时清空（与 server_lastplayerstates.Clear() 同步）
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        _installed = true;
        Plugin.Log.LogInfo("[KrokMpStateBridge] Installed. Medical effects will be saved/restored on player disconnect/reconnect.");
    }

    private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (_savedEffects.Count > 0)
        {
            Plugin.Log.LogInfo($"[KrokMpStateBridge] Scene loaded, clearing {_savedEffects.Count} saved effect entries.");
        }
        _savedEffects.Clear();

        // 如果没有加载存档（_pendingDataWasLoaded=false），说明是新游戏或换层。
        // 换层时 EffectBackup 需要保留（用于恢复效果）。
        // 新游戏时 EffectBackup 应该清空（防止旧效果残留）。
        // 但 OnSceneLoaded 无法区分新游戏和换层。
        // 解决方案：不在 OnSceneLoaded 中清空，改为在 Body_Start_Postfix 中检查。
        _pendingDataWasLoaded = false;
    }

    private static bool _pendingDataWasLoaded;

    /// <summary>
    /// 标记存档数据已加载（由 MultiPlayerStateSaveProvider.Restore 调用）。
    /// </summary>
    public static void MarkPendingDataLoaded() => _pendingDataWasLoaded = true;

    /// <summary>
    /// NetPlayer.OnDestroy Prefix：在 body 被置 null 之前，序列化医疗效果。
    /// </summary>
    private static void NetPlayer_OnDestroy_Prefix(object __instance)
    {
        try
        {
            if (!KrokMpHelper.IsServer && !KrokMpHelper.IsHost) return;
            if (!KrokMpHelper.IsMultiplayer) return;
            if (_npBodyField == null || _npGetPersistentId == null) return;

            var body = _npBodyField.GetValue(__instance) as Body;
            if (body == null)
            {
                Plugin.Log.LogInfo("[KrokMpStateBridge] OnDestroy: body is null, skipping.");
                return;
            }

            var pid = _npGetPersistentId.Invoke(__instance, null) as string;
            if (string.IsNullOrEmpty(pid) || pid == null) return;

            var json = Eff.Ser(body);
            _savedEffects[pid] = json ?? "";

            Plugin.Log.LogInfo($"[KrokMpStateBridge] Saved medical effects for player {pid} (json length={json?.Length ?? 0}).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KrokMpStateBridge] OnDestroy Prefix failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Body.Start Postfix：在 KrokMP 恢复 PlayerSavedState 后，恢复医疗效果。
    /// 使用低优先级确保在 KrokMP 的 Body_Start_MultiplayerPatch.Postfix 之后执行。
    /// 使用协程延迟 2 帧确保 NetBody.plr 已被赋值。
    /// </summary>
    [HarmonyPriority(Priority.Low)]
    private static void Body_Start_Postfix(Body __instance)
    {
        try
        {
            if (!KrokMpHelper.IsServer && !KrokMpHelper.IsHost) return;
            if (!KrokMpHelper.IsMultiplayer) return;
            if (_netBodyType == null) return;
            if (_savedEffects.Count == 0 && MultiPlayerStateSaveProvider.PendingPlayerData.Count == 0 && !EffectBackup.HasBackups) return;

            var netBody = __instance.GetComponent(_netBodyType);
            if (netBody == null) return;

            // is_player 是属性，用 PropertyInfo.GetValue
            if (_nbIsPlayerProp != null && !(bool)_nbIsPlayerProp.GetValue(netBody)!)
                return;

            // 主机自己的 Body 不需要在此处理（主机由原版/QoL 存档系统管理）。
            if (__instance == PlayerCamera.main?.body)
            {
                if (!_pendingDataWasLoaded && MultiPlayerStateSaveProvider.PendingPlayerData.Count == 0)
                {
                    // 没有加载存档 → 真正的新游戏，清空 EffectBackup 防止旧数据残留
                    EffectBackup.ClearAllBackups();
                    Plugin.Log.LogInfo("[KrokMpStateBridge] Host body started without save data — clearing EffectBackup (new game).");
                }
                return;
            }

            // plr 是属性，用 PropertyInfo.GetValue
            if (_nbPlrProp == null) return;
            var plr = _nbPlrProp.GetValue(netBody);
            if (plr == null)
            {
                // plr 可能尚未赋值，用协程延迟重试
                CoroutineRunner.Run(DelayedRestoreEffects(__instance));
                return;
            }

            RestoreEffectsForPlayer(__instance, plr);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KrokMpStateBridge] Body.Start Postfix failed: {ex.Message}");
        }
    }

    private static IEnumerator DelayedRestoreEffects(Body body)
    {
        // 等待 5 帧，确保 KrokMP 已完成 NetBody.plr 赋值和 PlayerSavedState.Apply
        yield return null;
        yield return null;
        yield return null;
        yield return null;
        yield return null;

        try
        {
            if (_netBodyType == null || _nbPlrProp == null) yield break;

            var netBody = body.GetComponent(_netBodyType);
            if (netBody == null) yield break;

            var plr = _nbPlrProp.GetValue(netBody);
            if (plr == null) yield break;

            RestoreEffectsForPlayer(body, plr);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KrokMpStateBridge] DelayedRestoreEffects failed: {ex.Message}");
        }
    }

    private static void RestoreInventory(Body body, JArray inventory, string pid)
    {
        // 两阶段恢复：
        // 1. 先恢复顶层物品到 body slot（建立 Container）
        // 2. 再恢复 Container 子物品（LoadItem）
        var containersBySlot = new Dictionary<int, Container>();

        // 第一阶段：顶层物品
        foreach (var token in inventory)
        {
            var itemData = token as JObject;
            if (itemData == null) continue;

            var inContainer = itemData["inContainer"]?.Value<bool>() ?? false;
            if (inContainer) continue; // 第二阶段处理

            var id = itemData["id"]?.ToString();
            if (string.IsNullOrEmpty(id)) continue;

            try
            {
                var go = Utils.Create(id, body.transform.position, 0f);
                if (go == null)
                {
                    Plugin.Log.LogWarning($"[KrokMpStateBridge] Failed to create item '{id}' for inventory restore.");
                    continue;
                }

                var item = go.GetComponent<Item>();
                if (item == null) continue;

                item.condition = itemData["condition"]?.Value<float>() ?? 1f;
                item.favourited = itemData["favourited"]?.Value<bool>() ?? false;

                // 弹匣子弹数
                var ammo = item.GetComponent<AmmoScript>();
                if (ammo != null && ammo.itemType == AmmoScript.AmmoItemType.Magazine)
                    ammo.rounds = itemData["ammoRounds"]?.Value<int>() ?? 0;

                // 枪械弹匣状态恢复
                var gun = item.GetComponent<GunScript>();
                if (gun != null && itemData["gunHasMag"] != null)
                {
                    gun.hasMag = itemData["gunHasMag"]?.Value<bool>() ?? false;
                    gun.roundsInMag = itemData["gunRoundsInMag"]?.Value<int>() ?? 0;
                }

                var slot = itemData["slot"]?.Value<int>() ?? -1;
                if (slot >= 0 && slot < body.slots.Length && !body.HoldingItem(slot))
                {
                    body.PickUpItem(item, slot, true);

                    var container = item.GetComponent<Container>();
                    if (container != null)
                        containersBySlot[slot] = container;
                }
                else
                {
                    body.AutoPickUpItem(item);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[KrokMpStateBridge] Failed to restore top-level item '{id}': {ex.Message}");
            }
        }

        // 第二阶段：Container 子物品
        foreach (var token in inventory)
        {
            var itemData = token as JObject;
            if (itemData == null) continue;

            var inContainer = itemData["inContainer"]?.Value<bool>() ?? false;
            if (!inContainer) continue;

            var id = itemData["id"]?.ToString();
            if (string.IsNullOrEmpty(id)) continue;

            var slot = itemData["slot"]?.Value<int>() ?? -1;
            if (!containersBySlot.TryGetValue(slot, out var container)) continue;

            try
            {
                var go = Utils.Create(id, body.transform.position, 0f);
                if (go == null) continue;

                var item = go.GetComponent<Item>();
                if (item == null) continue;

                item.condition = itemData["condition"]?.Value<float>() ?? 1f;
                item.favourited = itemData["favourited"]?.Value<bool>() ?? false;

                // 弹匣子弹数
                var ammo = item.GetComponent<AmmoScript>();
                if (ammo != null && ammo.itemType == AmmoScript.AmmoItemType.Magazine)
                    ammo.rounds = itemData["ammoRounds"]?.Value<int>() ?? 0;

                // Container.LoadItem 有距离检查，先把物品定位到容器位置
                item.transform.position = container.transform.position;
                container.LoadItem(item);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[KrokMpStateBridge] Failed to restore container item '{id}': {ex.Message}");
            }
        }

        Plugin.Log.LogInfo($"[KrokMpStateBridge] Restored inventory for player {pid} ({inventory.Count} items).");
    }

    private static void RestoreEffectsForPlayer(Body body, object plr)
    {
        try
        {
            if (_plrGetPersistentId == null)
                _plrGetPersistentId = AccessTools.Method(plr.GetType(), "GetPersistentId");
            if (_plrGetPersistentId == null) return;

            var pid = _plrGetPersistentId.Invoke(plr, null) as string;
            if (string.IsNullOrEmpty(pid) || pid == null) return;

            var restored = false;

            // 优先检查临时断线重连数据（同一服务器会话内的内存暂存）
            if (_savedEffects.TryGetValue(pid, out var json) && !string.IsNullOrEmpty(json))
            {
                Eff.Res(json, body);
                _savedEffects.Remove(pid);
                Plugin.Log.LogInfo($"[KrokMpStateBridge] Restored medical effects for player {pid} (from temp reconnect).");
                restored = true;
            }

            // 然后检查持久化存档数据（CUCoreLib 全局存档提供者暂存）
            if (MultiPlayerStateSaveProvider.TryConsumePending(pid, out var data))
            {
                string? restoredEffectsJson = null;
                // 恢复医疗效果（如果临时数据已恢复则跳过效果部分）
                var effectsToken = data["effects"];
                if (!restored && effectsToken != null && effectsToken.HasValues)
                {
                    restoredEffectsJson = effectsToken.ToString(Newtonsoft.Json.Formatting.None);
                    if (!string.IsNullOrEmpty(restoredEffectsJson))
                        Eff.Res(restoredEffectsJson, body);
                    Plugin.Log.LogInfo($"[KrokMpStateBridge] Restored medical effects for player {pid} (from persistent save).");
                }

                // 恢复健康状态
                var health = data["health"] as JObject;
                if (health != null)
                {
                    body.brainHealth = health["brainHealth"]?.Value<float>() ?? body.brainHealth;
                    body.heartRate = health["heartRate"]?.Value<float>() ?? body.heartRate;
                    body.bloodOxygen = health["bloodOxygen"]?.Value<float>() ?? body.bloodOxygen;
                    body.temperature = health["temperature"]?.Value<float>() ?? body.temperature;
                    body.radiationSickness = health["radiationSickness"]?.Value<float>() ?? body.radiationSickness;
                    Plugin.Log.LogInfo($"[KrokMpStateBridge] Restored health for player {pid}.");
                }

                // 恢复技能
                var skills = data["skills"] as JObject;
                if (skills != null && body.skills != null)
                {
                    body.skills.STR = skills["STR"]?.Value<int>() ?? body.skills.STR;
                    body.skills.RES = skills["RES"]?.Value<int>() ?? body.skills.RES;
                    body.skills.INT = skills["INT"]?.Value<int>() ?? body.skills.INT;
                    Plugin.Log.LogInfo($"[KrokMpStateBridge] Restored skills for player {pid}.");
                }

                // 恢复位置（仅同层）
                var pos = data["position"] as JObject;
                var savedDepth = data["biomeDepth"]?.Value<int>() ?? -1;
                if (pos != null && savedDepth == (WorldGeneration.world?.biomeDepth ?? -2))
                {
                    var x = pos["x"]?.Value<float>() ?? 0;
                    var y = pos["y"]?.Value<float>() ?? 0;
                    var z = pos["z"]?.Value<float>() ?? 0;
                    // 添加偏移避免与主机重叠
                    x += UnityEngine.Random.Range(-2f, 2f);
                    y += UnityEngine.Random.Range(1f, 3f);
                    body.transform.position = new Vector3(x, y, z);
                    Plugin.Log.LogInfo($"[KrokMpStateBridge] Restored position for player {pid} to ({x:F1}, {y:F1}, {z:F1}).");
                }

                // 恢复背包（inventory）
                var inventory = data["inventory"] as JArray;
                if (inventory != null && inventory.Count > 0)
                    RestoreInventory(body, inventory, pid);

                restored = true;

                // 通过网络将存档效果发送给客户端，使客户端本地 Body 也能恢复
                if (!string.IsNullOrEmpty(restoredEffectsJson))
                    EffectSyncNetwork.BroadcastEffects(pid, restoredEffectsJson);
            }

            if (!restored)
            {
                // 3. EffectBackup 后备恢复（换层、或回档时 Restore() 未调用）
                var backup = EffectBackup.TryGetBackup(pid);
                if (!string.IsNullOrEmpty(backup))
                {
                    Eff.Res(backup, body);
                    Plugin.Log.LogInfo($"[KrokMpStateBridge] Restored medical effects for player {pid} (from EffectBackup fallback).");
                    restored = true;
                    EffectSyncNetwork.BroadcastEffects(pid, backup);
                }
            }

            if (!restored)
                Plugin.Log.LogInfo($"[KrokMpStateBridge] No saved effects found for player {pid}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KrokMpStateBridge] RestoreEffectsForPlayer failed: {ex.Message}");
        }
    }
}

/// <summary>
/// 隐藏的 MonoBehaviour，用于启动协程。
/// </summary>
internal sealed class CoroutineRunner : MonoBehaviour
{
    private static CoroutineRunner? _instance;

    public static void Run(IEnumerator routine)
    {
        if (_instance == null)
        {
            var go = new GameObject("[KrokMpStateBridge CoroutineRunner]");
            _instance = go.AddComponent<CoroutineRunner>();
            DontDestroyOnLoad(go);
        }
        _instance.StartCoroutine(routine);
    }
}
