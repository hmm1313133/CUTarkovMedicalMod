using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 限制自定义液体只能存在于原始物品容器中。
/// WaterContainerItem.AddLiquid Prefix：如果液体不属于该容器，返回 0（不添加）。
/// </summary>
[HarmonyPatch(typeof(WaterContainerItem), "AddLiquid")]
public static class LiquidContainerRestrictPatch
{
    private static readonly Dictionary<string, string> LiquidToItem = new()
    {
        { "ai2_liquid",       "ai2" },
        { "goldenstar_liquid", "goldenstar" },
        { "vaseline_liquid",   "vaseline" },
        { "libatine_liquid",   "libatine" },
        { "ibuprofen_liquid",  "ibuprofen" },
    };

    /// <summary>
    /// 返回 true 继续原版逻辑，返回 false 跳过（且 __result=0 表示没有液体被添加）。
    /// </summary>
    [HarmonyPrefix]
    public static bool Prefix(WaterContainerItem __instance, string liquidId, float amount, ref float __result)
    {
        // 不是自定义液体，放行
        if (!LiquidToItem.TryGetValue(liquidId, out var expectedItemId))
            return true;

        // 检查容器是否是原始物品
        var item = __instance.GetComponent<Item>();
        string containerItemId = item != null ? item.id : "";

        if (!string.Equals(containerItemId, expectedItemId, System.StringComparison.OrdinalIgnoreCase))
        {
            // 液体装到了错误的容器 → 销毁液体（返回 0 = 不添加）
            Plugin.Log.LogWarning($"[LiquidRestrict] Blocked '{liquidId}' in '{containerItemId}' (expected '{expectedItemId}'). Liquid destroyed.");
            __result = 0f;
            return false;
        }

        return true;
    }
}
