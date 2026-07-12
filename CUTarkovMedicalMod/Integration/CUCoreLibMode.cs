using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using CUCoreLib.Registries;
using CUCoreLib.Saving;
using CUTarkovMedicalMod.Framework;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace CUTarkovMedicalMod.Integration;

/// <summary>
/// CUCoreLib 模式下的医疗效果保存提供者。
/// 包装 Eff.Ser()/Res() 为 IBodySaveProvider，通过 CUCoreLib 非破坏性 SaveCoordinator 持久化。
/// </summary>
public sealed class MedicalEffectSaveProvider : IBodySaveProvider
{
    public int GetVersion() => 1;

    public JToken Capture(Body body)
    {
        var json = Eff.Ser();
        return string.IsNullOrEmpty(json) ? null! : JToken.Parse(json);
    }

    public void Restore(Body body, JToken payload, int version, SaveRestoreContext context)
    {
        if (payload == null) return;
        var json = payload.ToString(Newtonsoft.Json.Formatting.None);
        if (!string.IsNullOrEmpty(json))
            Eff.Res(json);
    }
}

/// <summary>
/// CUCoreLib 模式。
/// - 跳过 QoLSaveFix（CUCoreLib 通过非破坏性 SaveCoordinator 接管存档）
/// - 注册 MedicalEffectSaveProvider 持久化医疗效果
/// - OnItemsSetup 将医疗物品注册到 CUCoreLib ItemRegistry（防止存档加载 NRE）
/// </summary>
public sealed class CUCoreLibMode : IIntegrationMode
{
    /// <summary>
    /// 医疗物品 ID 快照。Initialize 时捕获（此时 WeaponMod 尚未加载，
    /// CustomItemPrefabs 仅含医疗物品），OnItemsSetup 仅遍历此快照，
    /// 避免与 WeaponMod 重复注册武器物品到 CUCoreLib ItemRegistry。
    /// </summary>
    private HashSet<string>? _medicalItemIds;

    public void Initialize(Harmony harmony)
    {
        // 快照医疗物品 ID（此时 WeaponMod 尚未加载，CustomItemPrefabs 仅含医疗物品）
        _medicalItemIds = new HashSet<string>(
            Framework.ConsoleSpawnPatch.CustomItemPrefabs.Keys,
            StringComparer.OrdinalIgnoreCase);
        Plugin.Log.LogInfo($"[CUCoreLib] Snapshot {_medicalItemIds.Count} medical item IDs.");

        // 当 QoL 也安装时，额外注册 QoLSaveFix（仅 Prefix+Postfix，不含 Transpiler）。
        //
        // 原因：CUCoreLib 的 CustomInstantiate.GetOrCreateTemplate 通过 ChooseTemplateId 选择基础预制体，
        // 但我们的 CloneItemInfo 创建的是 plain ItemInfo（非 LiquidItemInfo），导致 capacity=0、
        // category="ModStim"，ChooseTemplateId 返回 "bandage" 而非 "syringe"。
        // "bandage" 没有 WaterContainerItem 组件，存档加载时恢复组件数据会报
        // "Error occured during loading item 'propital(Clone) (Item)'"。
        //
        // QoLSaveFix 的 Prefix 将自定义物品 ID 替换为原版基础预制体 ID（如 propital->syringe），
        // 使游戏直接加载原版 syringe 预制体（含 WaterContainerItem），Postfix 再转换回自定义物品。
        // Transpiler 跳过以避免与 CUCoreLib 的 Transpiler 冲突（两者都拦截同一处 Resources.Load）。
        if (QoLSaveFix.HasQoL())
        {
            QoLSaveFix.Register(harmony, includeTranspiler: false);
            Plugin.Log.LogInfo("[CUCoreLib] QoL detected - registered QoLSaveFix (Prefix+Postfix only) for item save/load.");
        }

        try
        {
            SaveRegistry.RegisterBodyProvider("cutarkovmedical.effects", new MedicalEffectSaveProvider());
            Plugin.Log.LogInfo("[CUCoreLib] Registered medical effect save provider.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CUCoreLib] Failed to register save provider: {ex.Message}");
        }
    }

    public void OnItemsSetup()
    {
        // 将已注册到 GlobalItems 的医疗物品同步注册到 CUCoreLib ItemRegistry。
        // 仅遍历快照中的医疗物品 ID，武器物品由 WeaponMod 的 WeaponCUCoreLibMode 负责注册。
        // CustomInstantiate.GetOrCreateTemplate(id) 从 RegisteredItems 查找；
        // 未注册的自定义物品 ID 在存档加载时返回 null -> NRE。
        if (Item.GlobalItems == null || _medicalItemIds == null) return;

        var registered = 0;
        foreach (var itemId in _medicalItemIds)
        {
            try
            {
                if (!Item.GlobalItems.ContainsKey(itemId)) continue;
                var info = Item.GlobalItems[itemId];
                if (info == null) continue;

                ItemRegistry.Register(itemId, info, null);
                registered++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[CUCoreLib] Failed to register item '{itemId}': {ex.Message}");
            }
        }

        if (registered > 0)
            Plugin.Log.LogInfo($"[CUCoreLib] Registered {registered} medical items with ItemRegistry.");
    }
}

/// <summary>
/// 根据运行时是否安装 CUCoreLib 选择集成模式。
/// </summary>
public static class IntegrationModeFactory
{
    public static IIntegrationMode Create()
    {
        var hasCUCoreLib = IsCUCoreLibPresent();
        Plugin.Log.LogInfo($"[IntegrationModeFactory] CUCoreLib present: {hasCUCoreLib}");
        if (hasCUCoreLib)
            return CreateCUCoreLibMode();
        return new LegacyMode();
    }

    private static bool IsCUCoreLibPresent()
    {
        try
        {
            return Chainloader.PluginInfos.ContainsKey("net.cucorelib");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 隔离 CUCoreLib 类型的实例化，确保未安装 CUCoreLib 时不会触发程序集加载。
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IIntegrationMode CreateCUCoreLibMode()
    {
        return new CUCoreLibMode();
    }
}
