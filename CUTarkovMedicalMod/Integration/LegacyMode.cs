using System;
using System.Collections.Generic;
using System.Reflection;
using CUTarkovMedicalMod.Framework;
using HarmonyLib;

namespace CUTarkovMedicalMod.Integration;

/// <summary>
/// 非 CUCoreLib 模式（遗留模式）。
///
/// 类似 CUCoreLib 的注册机制：
/// - 使用 MedicalItemDefinitions 作为中央物品定义注册表
/// - OnItemsSetup 后处理 GlobalItems：将需要 capacity 的 plain ItemInfo 升级为 LiquidItemInfo
/// - 始终注册 QoLSaveFix（不依赖 QoL 是否安装），提供存档保存/加载支持
///   - Prefix: 将自定义物品 ID 替换为基础预制体 ID（使 Resources.Load 成功）
///   - Postfix: 将基础预制体物品转换回自定义物品
///   - Transpiler: 拦截 Resources.Load 作为安全网
/// </summary>
public sealed class LegacyMode : IIntegrationMode
{
    public void Initialize(Harmony harmony)
    {
        // 始终注册 QoLSaveFix，不依赖 QoL 是否安装。
        // QoLSaveFix 提供完整的存档保存/加载支持（类似 CUCoreLib 的 CustomInstantiate）。
        // 当 QoL 也安装时，Prefix 会在 QoL 的 Prefix 之后运行（after 属性保证）。
        QoLSaveFix.Register(harmony, includeTranspiler: true);
        Plugin.Log.LogInfo("[LegacyMode] Registered QoLSaveFix (always-on, with transpiler).");
    }

    public void OnItemsSetup()
    {
        // 后处理 GlobalItems：确保有液体的物品具有正确的 capacity。
        //
        // 单次注射器（capacity=0, 无 LiquidId）无需升级，保持 plain ItemInfo，
        // 由 useAction 委托处理效果，不依赖 LiquidItemInfo。
        //
        // 已有 LiquidItemInfo 的物品（AI2, GoldenStar 等）无需升级。
        // capacity=0 的物品（绷带/工具）无需升级。
        if (Item.GlobalItems == null) return;

        var upgraded = 0;
        foreach (var def in MedicalItemDefinitions.All)
        {
            try
            {
                if (!Item.GlobalItems.ContainsKey(def.ItemId)) continue;
                var info = Item.GlobalItems[def.ItemId];
                if (info == null) continue;

                // 需要 capacity 但当前不是 LiquidItemInfo -> 升级
                if (def.Capacity > 0 && info is not LiquidItemInfo)
                {
                    var liquidInfo = UpgradeToLiquidItemInfo(info, def);
                    Item.GlobalItems[def.ItemId] = liquidInfo;
                    upgraded++;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[LegacyMode] Failed to upgrade item '{def.ItemId}': {ex.Message}");
            }
        }

        if (upgraded > 0)
            Plugin.Log.LogInfo($"[LegacyMode] Upgraded {upgraded} items to LiquidItemInfo with proper capacity.");
    }

    /// <summary>
    /// 将 plain ItemInfo 升级为 LiquidItemInfo，浅拷贝所有公共字段并设置 capacity/defaultContents。
    /// </summary>
    private static LiquidItemInfo UpgradeToLiquidItemInfo(ItemInfo source, MedicalItemDef def)
    {
        var liquidInfo = new LiquidItemInfo();

        // 浅拷贝所有公共实例字段（包括 useAction, useLimbAction 等委托）
        foreach (var field in GetPublicInstanceFields(source.GetType()))
            field.SetValue(liquidInfo, field.GetValue(source));

        // 设置 LiquidItemInfo 特有字段
        liquidInfo.capacity = def.Capacity;
        if (def.LiquidId != null && def.Capacity > 0)
            liquidInfo.defaultContents = new List<LiquidStack> { new(def.LiquidId, def.Capacity) };
        liquidInfo.autoFill = false;

        liquidInfo.SetTags();
        return liquidInfo;
    }

    private static IEnumerable<FieldInfo> GetPublicInstanceFields(Type type)
    {
        var seen = new HashSet<string>();
        for (var current = type;
             current != null && typeof(ItemInfo).IsAssignableFrom(current);
             current = current.BaseType)
            foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (seen.Add(field.Name))
                    yield return field;
    }
}
