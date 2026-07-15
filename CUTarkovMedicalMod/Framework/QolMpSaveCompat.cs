using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// QoL Unknown 多人存档兼容补丁 + CUCoreLib 存档路径修复 + KrokMP 物品网络同步。
///
/// 三个核心问题及修复：
///
/// 1. CUCoreLib SaveCoordinator.GetSavePath() 使用 Application.persistentDataPath（vanilla 路径），
///    但 KrokMP 将 SaveSystem.SaveGame() 重定向到 mp_save/save.sv。
///    -> Prefix 补丁 GetSavePath()，在多人模式返回 mp_save/save.sv
///
/// 2. CUCoreLib 的 CustomInstantiate.InstantiateReturn 和我们的 UtilsCreateFallbackPatch 创建的物品
///    不会调用 KrokMP 的 NetObjectRegistry._RegisterGO，客户端看不到。
///    -> Postfix 补丁 Utils.Create，对结果调用 KrokMP 网络注册
///
/// 3. QoL 的 ApplySavedItems 使用 Resources.Load(id) 创建物品，自定义物品返回 null。
///    -> Prefix 替换为 Utils.Create
/// </summary>
public static class QolMpSaveCompat
{
    private static bool _installed;
    private static Type? _bundleType;

    // KrokMP 网络注册反射缓存
    private static Type? _netObjectRegistryType;
    private static MethodInfo? _newGoMethod;
    private static MethodInfo? _objectCanBeIgnoredMethod;
    private static bool _krokMpNetResolved;

    // QoL KrokoshaMpCompat 反射缓存
    private static Type? _qolMpCompatType;
    private static MethodInfo? _qolRegisterAndSyncItemTree;

    /// <summary>
    /// 安装兼容补丁。
    /// </summary>
    public static void Install(Harmony harmony)
    {
        if (_installed) return;

        var patched = 0;

        // === 修复 1: CUCoreLib 存档路径 ===
        patched += PatchCuCoreLibSavePath(harmony);

        // === 修复 2: Utils.Create 物品网络同步 ===
        patched += PatchUtilsCreateNetworkSync(harmony);

        // === 修复 3: QoL ApplySavedItems ===
        _bundleType = AccessTools.TypeByName("QoL_Unknown.KrokoshaMpSaveBundle");
        if (_bundleType != null)
        {
            // 3a. Prefix ApplySavedItems - 用 Utils.Create 替代 Resources.Load
            var applyMethod = AccessTools.Method(_bundleType, "ApplySavedItems",
                new[] { typeof(Body), typeof(JObject) });
            if (applyMethod != null)
            {
                harmony.Patch(applyMethod,
                    prefix: new HarmonyMethod(typeof(QolMpSaveCompat), nameof(ApplySavedItems_Prefix)));
                patched++;
            }

            // 3b. Postfix TryApplyClientInventoryPayload - 恢复医疗效果
            var tryApplyMethod = AccessTools.Method(_bundleType, "TryApplyClientInventoryPayload",
                new[] { typeof(Body), typeof(string), typeof(string).MakeByRefType() });
            if (tryApplyMethod != null)
            {
                harmony.Patch(tryApplyMethod,
                    postfix: new HarmonyMethod(typeof(QolMpSaveCompat), nameof(TryApplyClientInventoryPayload_Postfix)));
                patched++;
            }

            // 3c. Postfix CaptureLocalPlayerSnapshotJson - 注入医疗效果
            var captureMethod = AccessTools.Method(_bundleType, "CaptureLocalPlayerSnapshotJson", Type.EmptyTypes);
            if (captureMethod != null)
            {
                harmony.Patch(captureMethod,
                    postfix: new HarmonyMethod(typeof(QolMpSaveCompat), nameof(CaptureLocalPlayerSnapshotJson_Postfix)));
                patched++;
            }

            Plugin.Log.LogInfo($"[QolMpSaveCompat] Patched QoL KrokoshaMpSaveBundle ({patched} patches).");
        }
        else
        {
            Plugin.Log.LogInfo("[QolMpSaveCompat] KrokoshaMpSaveBundle type not found, QoL patches skipped.");
        }

        // 预解析 KrokMP/QoL 网络注册方法
        ResolveKrokMpNetworkMethods();
        ResolveQolMpCompatMethods();

        _installed = true;
        Plugin.Log.LogInfo($"[QolMpSaveCompat] Installed {patched} patches total.");
    }

    // ===== 修复 1: CUCoreLib 存档路径 =====

    private static int PatchCuCoreLibSavePath(Harmony harmony)
    {
        try
        {
            var saveCoordinatorType = AccessTools.TypeByName("CUCoreLib.Saving.SaveCoordinator")
                ?? AccessTools.TypeByName("SaveCoordinator");
            if (saveCoordinatorType == null) return 0;

            var getSavePathMethod = AccessTools.Method(saveCoordinatorType, "GetSavePath");
            if (getSavePathMethod == null) return 0;

            harmony.Patch(getSavePathMethod,
                prefix: new HarmonyMethod(typeof(QolMpSaveCompat), nameof(GetSavePath_Prefix)));
            Plugin.Log.LogInfo("[QolMpSaveCompat] Patched CUCoreLib SaveCoordinator.GetSavePath for multiplayer save path.");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[QolMpSaveCompat] Failed to patch GetSavePath: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// CUCoreLib SaveCoordinator.GetSavePath() Prefix：
    /// 在多人模式下返回 mp_save/save.sv 而非 vanilla 路径。
    ///
    /// 注意：QoL 的 BeginVanillaSaveSnapshot 会临时将 KrokMP 的 savedatapathreplacement
    /// 重置为空字符串，使 SaveSystem.SaveGame 写入 vanilla 路径。
    /// 此时 CUCoreLib 的 EmbedIntoSaveFile 也应该写入 vanilla 路径，
    /// 否则会向 mp_save/save.sv 注入数据，干扰 QoL 的客户端快照。
    /// </summary>
    private static bool GetSavePath_Prefix(ref string __result)
    {
        if (!KrokMpHelper.IsKrokMpInstalled || !KrokMpHelper.IsMultiplayer)
            return true; // 单机：放行原始方法

        // 检查 KrokMP 的 savedatapathreplacement 是否被临时重置（QoL BeginVanillaSaveSnapshot）
        var replacement = GetKrokMpSaveDataPathReplacement();
        if (!string.IsNullOrEmpty(replacement))
        {
            // KrokMP 路径重定向处于活跃状态 -> 使用 mp_save 路径
            __result = Path.Combine(replacement, "save.sv");
        }
        else
        {
            // 路径重定向被临时禁用（QoL vanilla snapshot）-> 使用 vanilla 路径
            __result = Path.Combine(Application.persistentDataPath, "save.sv");
        }
        return false;
    }

    private static string? GetKrokMpSaveDataPathReplacement()
    {
        try
        {
            var savesystemPatchType = AccessTools.TypeByName("KrokoshaCasualtiesMP.SavesystemPatch");
            if (savesystemPatchType == null) return null;

            var field = AccessTools.Field(savesystemPatchType, "savedatapathreplacement");
            if (field == null) return null;

            return field.GetValue(null) as string;
        }
        catch { return null; }
    }

    // ===== 修复 2: Utils.Create 物品网络同步 =====

    private static int PatchUtilsCreateNetworkSync(Harmony harmony)
    {
        try
        {
            var createMethod = AccessTools.Method(typeof(Utils), nameof(Utils.Create),
                new[] { typeof(string), typeof(Vector2), typeof(float) });
            if (createMethod == null) return 0;

            harmony.Patch(createMethod,
                postfix: new HarmonyMethod(typeof(QolMpSaveCompat), nameof(UtilsCreate_NetworkSync_Postfix)));
            Plugin.Log.LogInfo("[QolMpSaveCompat] Patched Utils.Create for KrokMP network sync.");
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[QolMpSaveCompat] Failed to patch Utils.Create: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Utils.Create Postfix：在主机端创建物品后注册到 KrokMP 网络，使客户端能看到。
    ///
    /// KrokMP 的 NetObjectRegistry.NewGO(GameObject) 是注册物品到网络同步的正确入口：
    /// - 服务器端调用 Server_EnsureItemIsNetworkRegistered -> NewCoolerObjectPacketWriteReadSystem.Server_RegisterObject
    /// - 会自动分配 syncId、创建 GOSyncPacket、发送同步包到所有客户端
    ///
    /// 无论物品由 CUCoreLib prefix 创建还是 UtilsCreateFallbackPatch 创建，
    /// 此 Postfix 都会执行，对非 null 的 __result 调用网络注册。
    /// </summary>
    [HarmonyPriority(Priority.Low)]
    private static void UtilsCreate_NetworkSync_Postfix(string id, ref GameObject __result)
    {
        if (__result == null) return;
        if (!KrokMpHelper.IsKrokMpInstalled || !KrokMpHelper.IsMultiplayer) return;
        if (!KrokMpHelper.IsHost && !KrokMpHelper.IsServer) return;

        // 调用 KrokMP NetObjectRegistry.NewGO 注册到网络同步
        RegisterWithKrokMp(__result);
    }

    /// <summary>
    /// 调用 KrokMP NetObjectRegistry.NewGO(GameObject) 注册物品到网络。
    /// NewGO 在服务器端调用 Server_EnsureItemIsNetworkRegistered，
    /// 自动分配 syncId 并发送 GOSyncPacket 到所有客户端。
    /// </summary>
    private static void RegisterWithKrokMp(GameObject go)
    {
        // 优先使用 QoL 的 RegisterAndSyncItemTree（会递归处理子物品）
        if (_qolRegisterAndSyncItemTree != null)
        {
            try
            {
                var item = go.GetComponent<Item>();
                if (item != null)
                {
                    _qolRegisterAndSyncItemTree.Invoke(null, new object[] { item });
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[QolMpSaveCompat] QoL RegisterAndSyncItemTree failed: {ex.Message}");
            }
        }

        // 回退：直接调用 KrokMP NetObjectRegistry.NewGO
        if (_netObjectRegistryType == null || _newGoMethod == null) return;

        try
        {
            // 检查物品是否已被忽略
            if (_objectCanBeIgnoredMethod != null)
            {
                var ignored = (bool)_objectCanBeIgnoredMethod.Invoke(null, new object[] { go });
                if (ignored) return;
            }

            _newGoMethod.Invoke(null, new object[] { go });
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[QolMpSaveCompat] NetObjectRegistry.NewGO failed for '{go.name}': {ex.Message}");
        }
    }

    // ===== 修复 3: QoL ApplySavedItems =====

    /// <summary>
    /// 替换 QoL 的 ApplySavedItems，使用 Utils.Create 替代 Resources.Load + Instantiate。
    /// </summary>
    private static bool ApplySavedItems_Prefix(Body body, JObject save)
    {
        try
        {
            // 多人模式下：仅恢复本地玩家的物品，防止客户端快照数据被恢复到主机身体
            if (KrokMpHelper.IsMultiplayer && body != PlayerCamera.main?.body)
            {
                Plugin.Log.LogInfo("[QolMpSaveCompat] Skipping ApplySavedItems for non-local body in multiplayer.");
                return false;
            }

            var items = save["items"];
            var savedItems = items?.ToObject<SavedItem[]>() ?? Array.Empty<SavedItem>();
            var itemComponents = save["itemComponents"] as JArray;

            for (var i = 0; i < savedItems.Length; i++)
            {
                var saved = savedItems[i];
                if (string.IsNullOrEmpty(saved.id)) continue;

                try
                {
                    var go = Utils.Create(saved.id, body.transform.position, 0f);
                    if (go == null)
                    {
                        Plugin.Log.LogWarning($"[QolMpSaveCompat] Failed to create item '{saved.id}' during restore.");
                        continue;
                    }

                    var item = go.GetComponent<Item>();
                    if (item == null) continue;

                    item.condition = saved.condition;
                    item.favourited = saved.favourited;

                    if (saved.slot >= 0)
                    {
                        if (body.HoldingItem(saved.slot))
                        {
                            var container = body.GetItem(saved.slot)?.GetComponent<Container>();
                            container?.LoadItem(item);
                        }
                        else
                        {
                            body.PickUpItem(item, saved.slot, true);
                        }
                    }
                    else
                    {
                        body.AutoPickUpItem(item);
                    }

                    if (itemComponents != null && i < itemComponents.Count)
                        ApplyItemComponents(go, itemComponents[i] as JObject);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[QolMpSaveCompat] Failed to restore item '{saved.id}': {ex.Message}");
                }
            }

            TryRegisterAndSyncItems(body);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[QolMpSaveCompat] ApplySavedItems prefix failed: {ex}");
        }
        return false;
    }

    private static void ApplyItemComponents(GameObject? go, JObject? components)
    {
        if (go == null || components == null) return;

        foreach (var prop in components.Properties())
        {
            var type = Type.GetType(prop.Name);
            if (type == null) continue;

            var comp = go.GetComponent(type);
            if (comp == null) continue;

            var compObj = prop.Value as JObject;
            if (compObj == null) continue;

            foreach (var fieldProp in compObj.Properties())
            {
                var field = type.GetField(fieldProp.Name);
                if (field != null)
                    field.SetValue(comp, fieldProp.Value.ToObject(field.FieldType));
            }
        }
    }

    private static void TryRegisterAndSyncItems(Body body)
    {
        if (_qolRegisterAndSyncItemTree == null) return;
        try
        {
            for (var i = 0; i < body.slots.Length; i++)
            {
                if (!body.HoldingItem(i)) continue;
                var item = body.GetItem(i);
                if (item != null)
                    _qolRegisterAndSyncItemTree.Invoke(null, new object[] { item });
            }
        }
        catch { }
    }

    // ===== 医疗效果持久化 =====

    private static void TryApplyClientInventoryPayload_Postfix(Body body, string payload)
    {
        try
        {
            if (body == null || string.IsNullOrEmpty(payload)) return;

            // 多人模式下：仅主机端跳过本地 body 的效果恢复（防止客户端快照交叉恢复到主机身体）
            // 客户端端不跳过——客户端需要从 QoL 快照恢复自己的医疗效果
            if (KrokMpHelper.IsMultiplayer && KrokMpHelper.IsHost && body == PlayerCamera.main?.body)
            {
                Plugin.Log.LogInfo("[QolMpSaveCompat] Skipping client inventory payload effects on host local body in multiplayer.");
                return;
            }

            var save = JObject.Parse(payload);
            var effects = save["cutarkov_effects"];
            if (effects == null) return;

            var json = effects.ToString(Formatting.None);
            if (!string.IsNullOrEmpty(json))
            {
                Eff.Res(json, body);
                Plugin.Log.LogInfo("[QolMpSaveCompat] Restored medical effects after client inventory restore.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[QolMpSaveCompat] Failed to restore effects: {ex.Message}");
        }
    }

    private static void CaptureLocalPlayerSnapshotJson_Postfix(ref string __result)
    {
        try
        {
            if (string.IsNullOrEmpty(__result)) return;

            // 仅在多人模式下注入效果，且仅注入本地玩家（主机）的效果
            // 防止客户端快照被注入主机效果后，回档时交叉恢复到主机身体
            if (!KrokMpHelper.IsMultiplayer) return;

            var body = PlayerCamera.main?.body;
            if (body == null) return;

            var effectsJson = Eff.Ser(body);
            if (string.IsNullOrEmpty(effectsJson)) return;

            var save = JObject.Parse(__result);
            save["cutarkov_effects"] = JToken.Parse(effectsJson);
            __result = save.ToString(Formatting.None);

            Plugin.Log.LogInfo("[QolMpSaveCompat] Injected medical effects into local player snapshot.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[QolMpSaveCompat] Failed to inject effects into snapshot: {ex.Message}");
        }
    }

    // ===== 反射初始化 =====

    private static void ResolveKrokMpNetworkMethods()
    {
        try
        {
            _netObjectRegistryType = AccessTools.TypeByName("KrokoshaCasualtiesMP.NetObjectRegistry");
            if (_netObjectRegistryType == null) return;

            // NewGO(GameObject) - 服务器端注册物品到网络同步
            _newGoMethod = AccessTools.Method(_netObjectRegistryType, "NewGO",
                new[] { typeof(GameObject) });

            // ObjectCanBeIgnoredForNetwork(GameObject) - 检查物品是否可忽略
            _objectCanBeIgnoredMethod = AccessTools.Method(_netObjectRegistryType, "ObjectCanBeIgnoredForNetwork",
                new[] { typeof(GameObject) });

            _krokMpNetResolved = _newGoMethod != null;

            if (_krokMpNetResolved)
                Plugin.Log.LogInfo("[QolMpSaveCompat] KrokMP NetObjectRegistry.NewGO resolved for network sync.");
            else
                Plugin.Log.LogWarning("[QolMpSaveCompat] KrokMP NetObjectRegistry.NewGO not found.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[QolMpSaveCompat] Failed to resolve KrokMP network methods: {ex.Message}");
        }
    }

    private static void ResolveQolMpCompatMethods()
    {
        try
        {
            _qolMpCompatType = AccessTools.TypeByName("QoL_Unknown.KrokoshaMpCompat");
            if (_qolMpCompatType != null)
            {
                _qolRegisterAndSyncItemTree = AccessTools.Method(_qolMpCompatType, "RegisterAndSyncItemTree",
                    new[] { typeof(Item) });
                if (_qolRegisterAndSyncItemTree != null)
                    Plugin.Log.LogInfo("[QolMpSaveCompat] QoL KrokoshaMpCompat.RegisterAndSyncItemTree resolved.");
            }
        }
        catch { }
    }
}
