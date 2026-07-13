using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using CUCoreLib.Data;
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
/// - 跳过 QoLSaveFix（CUCoreLib 通过 CustomInstantiate 原生接管存档加载）
/// - 注册 MedicalEffectSaveProvider 持久化医疗效果
/// - OnItemsSetup 构建 CustomItemInfo 注册到 CUCoreLib ItemRegistry
/// </summary>
public sealed class CUCoreLibMode : IIntegrationMode
{
    public void Initialize(Harmony harmony)
    {
        // CUCoreLib 通过 CustomInstantiate.GetOrCreateTemplate 原生处理存档加载：
        //   拦截 Resources.Load(customId) -> 从 RegisteredItems 查找 -> CreateTemplate -> ChooseTemplateId
        // 不再需要 QoLSaveFix 的 ID 替换 hack。

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
        // 将已注册到 GlobalItems 的医疗物品构建为 CustomItemInfo 并注册到 CUCoreLib ItemRegistry。
        //
        // 单次注射器（capacity=0, LiquidId=null, BasePrefab=Syringe）：
        //   capacity=0 使 ApplyCustomItemComponents 不会重算 condition（def.capacity > 0f 为 false），
        //   避免 condition 被设为 0 导致物品消失。
        //   但 capacity=0 会使 ChooseTemplateId 返回 "bandage"（无 WaterContainerItem），导致 IndexOutOfRangeException。
        //   修复：设置 defaultContents=[{water, 0}]，使 ChooseTemplateId 因 defaultContents.Count > 0 返回 "waterbottle"。
        //   IsLiquidContainer 也返回 true，但 condition 重算因 capacity=0 被跳过。
        //
        // 多剂量/液体物品（capacity>0, LiquidId!=null）：正常设置 capacity/defaultContents。
        // 绷带/工具（capacity=0, BasePrefab=Bruisekit）：ChooseTemplateId 返回 "bandage"，正确。
        //
        // 不设置 Syringe/Bandage/Tool 属性，保留各 ItemSystem 自定义的 useAction/useLimbAction 委托。
        if (Item.GlobalItems == null) return;

        var registered = 0;
        foreach (var def in MedicalItemDefinitions.All)
        {
            try
            {
                if (!Item.GlobalItems.ContainsKey(def.ItemId)) continue;
                var plainInfo = Item.GlobalItems[def.ItemId];
                if (plainInfo == null) continue;

                // 构建 CustomItemInfo：浅拷贝 plain ItemInfo 的所有公共实例字段
                var customInfo = new CustomItemInfo();
                foreach (var field in GetPublicInstanceFields(plainInfo.GetType()))
                    field.SetValue(customInfo, field.GetValue(plainInfo));

                // 覆盖 capacity/defaultContents
                customInfo.capacity = def.Capacity;
                customInfo.autoFill = false;

                if (def.LiquidId != null && def.Capacity > 0)
                {
                    // 多剂量/液体物品：正常设置
                    customInfo.defaultContents = new List<LiquidStack> { new(def.LiquidId, def.Capacity) };
                }
                else if (def.BasePrefab == BasePrefabType.Syringe && def.Capacity == 0 && def.LiquidId == null)
                {
                    // 单次注射器：用 dummy liquid 触发 "waterbottle" 模板，避免 "bandage" 导致的 IndexOutOfRangeException
                    // amount=0 + capacity=0 -> condition 不被重算，保持 ConfigureSpawnedItem 设的 1.0
                    customInfo.defaultContents = new List<LiquidStack> { new("water", 0f) };
                }
                else
                {
                    customInfo.defaultContents = null;
                }

                // 设置图标（CUCoreLib CreateTemplate 会将其应用到 SpriteRenderer）
                var icon = def.ResolveIcon();

                // 注册到 CUCoreLib ItemRegistry
                // 不设 Syringe/Bandage/Tool -> ApplyMedicalActions 不覆盖自定义委托
                ItemRegistry.Register(def.ItemId, customInfo, icon);
                registered++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[CUCoreLib] Failed to register item '{def.ItemId}': {ex.Message}");
            }
        }

        if (registered > 0)
            Plugin.Log.LogInfo($"[CUCoreLib] Registered {registered} medical items with ItemRegistry (CustomItemInfo).");
    }

    /// <summary>
    /// 遍历类型层级（从 type 到 ItemInfo）获取所有公共实例字段，去重。
    /// 与 CUCoreLib ItemRegistry.ToCustomItemInfo 的 GetPublicInstanceFields 逻辑一致。
    /// </summary>
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
