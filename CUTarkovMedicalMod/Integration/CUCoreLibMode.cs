using System;
using System.Collections.Generic;
using System.Reflection;
using CUCoreLib.Data;
using CUCoreLib.Registries;
using CUCoreLib.Saving;
using CUTarkovMedicalMod.Framework;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CUTarkovMedicalMod.Integration;

/// <summary>
/// 医疗效果保存提供者。
/// 包装 Eff.Ser()/Res() 为 IBodySaveProvider，通过 CUCoreLib 非破坏性 SaveCoordinator 持久化。
/// </summary>
public sealed class MedicalEffectSaveProvider : IBodySaveProvider
{
    public int GetVersion() => 1;

    public JToken Capture(Body body)
    {
        // 多人模式下仅保存本地玩家的效果，客户端效果由 MultiPlayerStateSaveProvider 处理
        // 防止 CUCoreLib 将非本地玩家的效果嵌入存档后，在加载时被错误应用到其他身体
        if (KrokMpHelper.IsMultiplayer && body != LocalBody)
            return null!;
        var json = Eff.Ser(body);
        return string.IsNullOrEmpty(json) ? null! : JToken.Parse(json);
    }

    public void Restore(Body body, JToken payload, int version, SaveRestoreContext context)
    {
        if (payload == null) return;
        // 多人模式下仅恢复到本地玩家身体
        // 防止从客户端存档文件恢复时，将效果数据错误应用到主机身体
        if (KrokMpHelper.IsMultiplayer && body != LocalBody)
        {
            Plugin.Log.LogWarning("[MedicalEffectSaveProvider] Skipping restore on non-local body in multiplayer.");
            return;
        }
        var json = payload.ToString(Newtonsoft.Json.Formatting.None);
        if (!string.IsNullOrEmpty(json))
            Eff.Res(json, body);
    }

    private static Body? LocalBody
    {
        get
        {
            try { return PlayerCamera.main?.body; } catch { return null; }
        }
    }
}

/// <summary>
/// CUCoreLib 集成模式。
/// - 注册 MedicalEffectSaveProvider 持久化医疗效果
/// - OnItemsSetup 构建 CustomItemInfo 注册到 CUCoreLib ItemRegistry
/// </summary>
public sealed class CUCoreLibMode
{
    public void Initialize(Harmony harmony)
    {
        // CUCoreLib 通过 CustomInstantiate.GetOrCreateTemplate 原生处理存档加载：
        //   拦截 Resources.Load(customId) -> 从 RegisteredItems 查找 -> CreateTemplate -> ChooseTemplateId

        try
        {
            SaveRegistry.RegisterBodyProvider("cutarkovmedical.effects", new MedicalEffectSaveProvider());
            Plugin.Log.LogInfo("[CUCoreLib] Registered medical effect save provider.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CUCoreLib] Failed to register body save provider: {ex.Message}");
        }

        try
        {
            SaveRegistry.RegisterGlobalProvider("cutarkovmedical.mpplayers", new MultiPlayerStateSaveProvider());
            Plugin.Log.LogInfo("[CUCoreLib] Registered multiplayer player state save provider.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CUCoreLib] Failed to register global save provider: {ex.Message}");
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
        var fallback = 0;
        foreach (var def in MedicalItemDefinitions.All)
        {
            try
            {
                CustomItemInfo customInfo;
                var icon = def.ResolveIcon();

                // 调整图标 PPU 以匹配基础预制体的世界尺寸。
                // 图标纹理多为 16x16 像素，原始 PPU=32 使世界尺寸仅 0.5 单位（过小）。
                // 通过加载基础预制体的 SpriteRenderer.sprite，计算正确 PPU 使世界尺寸与基础物品一致。
                icon = AdjustIconToWorldSize(icon, def.BasePrefab);

                if (Item.GlobalItems.ContainsKey(def.ItemId))
                {
                    var plainInfo = Item.GlobalItems[def.ItemId];
                    if (plainInfo == null) continue;

                    // 正常路径：从 GlobalItems 浅拷贝构建 CustomItemInfo
                    customInfo = new CustomItemInfo();
                    foreach (var field in GetPublicInstanceFields(plainInfo.GetType()))
                        field.SetValue(customInfo, field.GetValue(plainInfo));
                    registered++;
                }
                else
                {
                    // Fallback：GlobalItems 中没有该物品（EnsureRegisteredInItemTable 可能失败）
                    // 创建最小化 CustomItemInfo，仅确保 CUCoreLib 能创建模板
                    Plugin.Log.LogWarning($"[CUCoreLib] Item '{def.ItemId}' not in GlobalItems, creating fallback CustomItemInfo.");
                    customInfo = new CustomItemInfo();
                    fallback++;
                }

                // 覆盖 capacity/defaultContents
                customInfo.capacity = def.Capacity;
                customInfo.autoFill = false;

                if (def.LiquidId != null && def.Capacity > 0)
                {
                    customInfo.defaultContents = new List<LiquidStack> { new(def.LiquidId, def.Capacity) };
                }
                else if (def.BasePrefab == BasePrefabType.Syringe && def.Capacity == 0 && def.LiquidId == null)
                {
                    customInfo.defaultContents = new List<LiquidStack> { new("water", 0f) };
                }
                else
                {
                    customInfo.defaultContents = null;
                }

                ItemRegistry.Register(def.ItemId, customInfo, icon);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[CUCoreLib] Failed to register item '{def.ItemId}': {ex.Message}");
            }
        }

        Plugin.Log.LogInfo($"[CUCoreLib] Registered {registered} medical items with ItemRegistry ({fallback} fallback).");
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

    /// <summary>
    /// 调整图标精灵的 PPU，使其在世界中的尺寸与基础预制体一致。
    /// CUCoreLib 创建物品时使用注册的图标精灵作为 SpriteRenderer.sprite，
    /// 世界尺寸 = 纹理尺寸 / PPU。原始图标 PPU=32 + 16x16 纹理 = 0.5 单位（过小）。
    /// 此方法加载基础预制体（syringe/bruisekit），读取其精灵的 PPU 和尺寸，
    /// 计算使自定义图标世界尺寸匹配基础物品的 PPU。
    /// </summary>
    private static Sprite? AdjustIconToWorldSize(Sprite? icon, BasePrefabType basePrefab)
    {
        if (icon == null || icon.texture == null) return icon;

        try
        {
            var prefabName = basePrefab == BasePrefabType.Syringe ? "syringe" : "bruisekit";
            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null) return icon;

            var baseSr = prefab.GetComponent<SpriteRenderer>();
            if (baseSr == null || baseSr.sprite == null) return icon;

            var baseSprite = baseSr.sprite;
            var basePpu = baseSprite.pixelsPerUnit > 0f ? baseSprite.pixelsPerUnit : 32f;
            var baseRect = baseSprite.rect;
            var tex = icon.texture;

            // 计算使自定义图标世界尺寸 = 基础物品世界尺寸 * 放大倍数 的 PPU
            // world_size = texture_size / PPU => PPU = texture_size / desired_world_size
            // desired_world_size = base_texture_size / base_PPU * WorldSizeMultiplier
            // => PPU = texture_size * base_PPU / (base_texture_size * WorldSizeMultiplier)
            // => PPU = base_PPU * dominantScale / WorldSizeMultiplier
            const float WorldSizeMultiplier = 2.5f;
            var widthScale = baseRect.width > 0f ? tex.width / baseRect.width : 1f;
            var heightScale = baseRect.height > 0f ? tex.height / baseRect.height : 1f;
            var dominantScale = Mathf.Max(widthScale, heightScale);
            var correctPpu = basePpu * dominantScale / WorldSizeMultiplier;

            Plugin.Log.LogInfo($"[CUCoreLib] AdjustIcon '{icon.name}': tex={tex.width}x{tex.height}, base={baseRect.width}x{baseRect.height}@{basePpu}PPU, scale={dominantScale:F3}, correctPpu={correctPpu:F1}, worldSize={tex.width / correctPpu:F2}x{tex.height / correctPpu:F2}");

            var adjusted = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), correctPpu);
            adjusted.name = icon.name + "_world";
            return adjusted;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CUCoreLib] AdjustIconToWorldSize failed: {ex.Message}");
            return icon;
        }
    }
}
