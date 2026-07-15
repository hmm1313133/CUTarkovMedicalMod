using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 修复 KrokMP 同步含自定义液体的 WaterContainerItem 时的 KeyNotFoundException。
///
/// KrokMP 的 Item_SetupItems_Listener.Postfix() 在游戏初始化时遍历 Liquids.Registry，
/// 构建 LiquidIdRegistry (Dictionary&lt;string,byte&gt;) 和 LiquidNetIdToId (string[]) 两个映射表。
/// 如果自定义液体在 Postfix 运行后才注册（模组加载顺序问题），这两个表不包含自定义液体 ID，
/// 导致 PackData2 查找 LiquidIdRegistry 时抛出 KeyNotFoundException。
///
/// 修复方案：在模组注册液体后，手动将自定义液体添加到这两个映射表。
/// </summary>
public static class KrokMpLiquidSyncPatch
{
    private static bool _installed;
    private static bool _registered;
    private static int _suppressedCount;

    private static Type? _listenerType;
    private static FieldInfo? _liquidIdRegistryField;
    private static FieldInfo? _liquidNetIdToIdField;

    private static readonly HashSet<string> CustomLiquidIds = new()
    {
        "ai2_liquid",
        "goldenstar_liquid",
        "vaseline_liquid",
        "libatine_liquid",
        "ibuprofen_liquid",
    };

    public static void Install(Harmony harmony)
    {
        if (_installed) return;
        if (!KrokMpHelper.IsKrokMpInstalled) return;

        try
        {
            _listenerType = AccessTools.TypeByName("KrokoshaCasualtiesMP.Item_SetupItems_Listener");
            if (_listenerType == null)
            {
                Plugin.Log.LogWarning("[KrokMpLiquidSync] Item_SetupItems_Listener type not found.");
                return;
            }

            _liquidIdRegistryField = AccessTools.Field(_listenerType, "LiquidIdRegistry");
            _liquidNetIdToIdField = AccessTools.Field(_listenerType, "LiquidNetIdToId");

            if (_liquidIdRegistryField == null || _liquidNetIdToIdField == null)
            {
                Plugin.Log.LogWarning($"[KrokMpLiquidSync] Fields not found: LiquidIdRegistry={_liquidIdRegistryField != null}, LiquidNetIdToId={_liquidNetIdToIdField != null}");
                return;
            }

            // Finalizer 作为后备：如果注册失败，至少吞掉异常让物品同步继续
            var packDataType = AccessTools.TypeByName("KrokoshaCasualtiesMP.NewCoolerObjectPacketWriteReadSystem");
            if (packDataType != null)
            {
                var method = AccessTools.Method(packDataType, "PackData2");
                if (method != null)
                {
                    var finalizer = new HarmonyMethod(typeof(KrokMpLiquidSyncPatch), nameof(PackData2_Finalizer));
                    harmony.Patch(method, finalizer: finalizer);
                }
            }

            _installed = true;
            Plugin.Log.LogInfo("[KrokMpLiquidSync] Installed. Will register custom liquids after KrokMP initializes.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KrokMpLiquidSync] Install failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 尝试注册自定义液体到 KrokMP 的 LiquidIdRegistry 和 LiquidNetIdToId。
    /// 从 EffectBackup.Tick() 调用，确保 KrokMP 已完成初始化。
    /// </summary>
    public static void TryRegisterCustomLiquids()
    {
        if (_registered || !_installed || _listenerType == null) return;

        try
        {
            var liquidIdRegistry = _liquidIdRegistryField!.GetValue(null) as IDictionary;
            var liquidNetIdToId = _liquidNetIdToIdField!.GetValue(null) as Array;

            if (liquidIdRegistry == null || liquidNetIdToId == null)
                return; // KrokMP 尚未初始化，下次重试

            // 检查是否已包含自定义液体
            bool needRegister = false;
            foreach (var id in CustomLiquidIds)
            {
                if (!liquidIdRegistry.Contains(id))
                {
                    needRegister = true;
                    break;
                }
            }

            if (!needRegister)
            {
                _registered = true;
                return;
            }

            // KrokMP 使用 byte 作为网络 ID（0-255）。
            // 找到当前最大 byte ID，在其后追加自定义液体。
            byte maxId = 0;
            foreach (DictionaryEntry entry in liquidIdRegistry)
            {
                if (entry.Value is byte b && b > maxId) maxId = b;
            }

            // 需要扩展 LiquidNetIdToId 数组以容纳新液体
            var oldArray = liquidNetIdToId;
            var newLength = (int)maxId + 1 + CustomLiquidIds.Count;
            if (newLength <= oldArray.Length)
            {
                // 数组已足够大，直接写入
                newLength = oldArray.Length;
            }

            var newArray = Array.CreateInstance(typeof(string), newLength);
            // 复制旧数据
            for (int i = 0; i < oldArray.Length && i < newLength; i++)
                newArray.SetValue(oldArray.GetValue(i), i);

            // 注册自定义液体
            byte nextId = (byte)(maxId + 1);
            int registered = 0;
            foreach (var id in CustomLiquidIds)
            {
                if (liquidIdRegistry.Contains(id)) continue;
                if (nextId > 254) break; // byte 上限

                liquidIdRegistry[id] = nextId;
                if (nextId < newArray.Length)
                    newArray.SetValue(id, nextId);
                nextId++;
                registered++;
                Plugin.Log.LogInfo($"[KrokMpLiquidSync] Registered liquid '{id}' -> netId={nextId - 1}");
            }

            // 更新 LiquidNetIdToId 数组（如果扩展了）
            if (newArray.Length != oldArray.Length)
            {
                _liquidNetIdToIdField.SetValue(null, newArray);
                Plugin.Log.LogInfo($"[KrokMpLiquidSync] Expanded LiquidNetIdToId from {oldArray.Length} to {newArray.Length} entries.");
            }

            _registered = true;
            Plugin.Log.LogInfo($"[KrokMpLiquidSync] Registered {registered} custom liquids in KrokMP (total liquids now: {liquidIdRegistry.Count}).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[KrokMpLiquidSync] TryRegisterCustomLiquids failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Harmony Finalizer：作为后备，捕获 PackData2 中的 KeyNotFoundException。
    /// 如果 TryRegisterCustomLiquids 成功，这个 Finalizer 不会被触发。
    /// </summary>
    private static Exception? PackData2_Finalizer(Exception __exception)
    {
        if (__exception is KeyNotFoundException)
        {
            _suppressedCount++;
            if (_suppressedCount <= 3 || _suppressedCount % 100 == 0)
                Plugin.Log.LogWarning($"[KrokMpLiquidSync] Suppressed KeyNotFoundException in PackData2 (total={_suppressedCount}). Liquid content may not sync correctly.");
            return null;
        }
        return __exception;
    }
}
