using System.Collections.Generic;
using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 阻止迷你桶（minibarrel）在世界生成时被填入模组自定义液体。
///
/// 原版 WorldGeneration.DistributeMiniBarrels() 从 Liquids.Registry 随机选一个液体填入 minibarrel。
/// 模组注册了 ai2_liquid / goldenstar_liquid / vaseline_liquid / libatine_liquid / ibuprofen_liquid，
/// 这些不应该出现在随机迷你桶中（它们是专用药品液体）。
///
/// 通过 Postfix 拦截 WaterContainerItem.AddLiquid：如果目标是 minibarrel 且液体是模组自定义液体，
/// 则立即将其从 stack 中移除，实现静默过滤。
/// </summary>
[HarmonyPatch(typeof(WaterContainerItem), nameof(WaterContainerItem.AddLiquid))]
public static class MiniBarrelLiquidFilterPatch
{
    /// <summary>模组自定义液体ID集合</summary>
    private static readonly HashSet<string> ModLiquidIds = new()
    {
        AI2ItemSystem.LiquidId,           // ai2_liquid
        GoldenStarItemSystem.LiquidId,    // goldenstar_liquid
        VaselineItemSystem.LiquidId,      // vaseline_liquid
        LibatineItemSystem.LiquidId,      // libatine_liquid
        IbuprofenItemSystem.LiquidId,     // ibuprofen_liquid
    };

    [HarmonyPostfix]
    public static void Postfix(WaterContainerItem __instance, string liquidId, float amount)
    {
        // 只处理模组自定义液体
        if (!ModLiquidIds.Contains(liquidId))
            return;

        // 只处理 minibarrel
        var item = __instance.GetComponent<Item>();
        if (item == null || item.id != "minibarrel")
            return;

        // AddLiquid 已经把液体加入了 stack，这里移除它
        if (__instance.stack != null && __instance.stack.Count > 0)
        {
            __instance.stack.RemoveAll(s => ModLiquidIds.Contains(s.liquidId));
            __instance.UpdateCondition();
            Plugin.Log.LogInfo($"[MiniBarrelFilter] Removed mod liquid '{liquidId}' ({amount}ml) from minibarrel.");
        }
    }
}
