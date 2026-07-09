using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 拦截 Recipe.resultSprite getter —— 为自定义弹药提供正确的图标。
///
/// 原版逻辑（非液体结果）：
///   Resources.Load&lt;GameObject&gt;(result.id).GetComponent&lt;SpriteRenderer&gt;().sprite
/// 自定义弹药 ID 不在游戏 Resources 中，Resources.Load 返回 null，
/// 导致 NullRefException 或显示错误图标（如植物纤维袋）。
///
/// 此 Prefix 对自定义弹药 ID 跳过原版逻辑，直接返回我们的自定义 Sprite。
/// </summary>
[HarmonyPatch(typeof(Recipe), "resultSprite", MethodType.Getter)]
public static class RecipeSpritePatch
{
    // 自定义弹药 ID → 缓存 Sprite 的映射
    private static readonly Dictionary<string, Sprite> _spriteCache = new();

    // 所有自定义弹药 ItemKey 列表
    private static readonly HashSet<string> CustomAmmoIds = new()
    {
        Ammo338UCWItemSystem.ItemKey,
        Ammo76251BPZItemSystem.ItemKey,
        Ammo76239SPItemSystem.ItemKey,
        Ammo12g85ItemSystem.ItemKey,
        Ammo50CopperItemSystem.ItemKey,
        Ammo45FMJItemSystem.ItemKey,
        Ammo919PSOItemSystem.ItemKey,
        Ammo55645FMJItemSystem.ItemKey,
        Ammo5728SB193ItemSystem.ItemKey,
    };

    // 所有自定义弹匣 ItemKey 列表
    private static readonly HashSet<string> CustomMagIds = new()
    {
        AXMCMagItemSystem.ItemKey,
        DVL10MagItemSystem.ItemKey,
        AKMMagItemSystem.ItemKey,
        DeagleMagItemSystem.ItemKey,
        Glock17MagItemSystem.ItemKey,
        M4A1MagItemSystem.ItemKey,
        P90MagItemSystem.ItemKey,
    };

    // 弹匣 ItemKey → 图标文件名的映射（文件名不含扩展名）
    private static readonly Dictionary<string, string> MagIconFileMap = new()
    {
        { AXMCMagItemSystem.ItemKey, "axmc_magazine" },
        { DVL10MagItemSystem.ItemKey, "dvl10_magazine" },
        { AKMMagItemSystem.ItemKey, "akm_magazine" },
        { DeagleMagItemSystem.ItemKey, "deagle_magazine" },
        { Glock17MagItemSystem.ItemKey, "glock_magazine" },
        { M4A1MagItemSystem.ItemKey, "m4a1_magazine" },
        { P90MagItemSystem.ItemKey, "p90_magazine" },
    };

    [HarmonyPrefix]
    public static bool Prefix(Recipe __instance, ref ValueTuple<Sprite, Color> __result)
    {
        var resultId = __instance.result?.id;
        if (resultId == null)
            return true; // 无结果ID，走原版逻辑

        bool isCustomAmmo = CustomAmmoIds.Contains(resultId);
        bool isCustomMag = CustomMagIds.Contains(resultId);

        if (!isCustomAmmo && !isCustomMag)
            return true; // 不是自定义弹药或弹匣，走原版逻辑

        // 弹匣图标文件名可能与 ItemKey 不同，需映射
        string iconFileName = isCustomMag && MagIconFileMap.TryGetValue(resultId, out var magIcon)
            ? magIcon : resultId;

        var sprite = GetOrLoadSprite(iconFileName);
        if (sprite != null)
        {
            __result = new ValueTuple<Sprite, Color>(sprite, Color.white);
            return false; // 跳过原版逻辑，返回我们的自定义图标
        }

        // 自定义图标加载失败，也跳过原版（避免 NullRefException）
        // 使用原版同类物品的图标作为 fallback
        var fallbackSprite = GetFallbackSprite(resultId);
        __result = new ValueTuple<Sprite, Color>(fallbackSprite ?? GetBaseGameSprite(resultId), Color.white);
        return false;
    }

    /// <summary>
    /// 从插件 Assets 目录加载自定义弹药图标（PNG/WebP）
    /// </summary>
    private static Sprite GetOrLoadSprite(string ammoId)
    {
        if (_spriteCache.TryGetValue(ammoId, out var cached))
            return cached;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                              ?? BepInEx.Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", $"{ammoId}.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", $"{ammoId}.webp");
                if (!File.Exists(iconPath))
                {
                    Plugin.Log.LogWarning($"[RecipeSprite] No icon file found for '{ammoId}'");
                    return null;
                }
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                Plugin.Log.LogWarning($"[RecipeSprite] Failed to load image for '{ammoId}'");
                return null;
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            var sprite = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            sprite.name = $"{ammoId}-recipe-icon";

            _spriteCache[ammoId] = sprite;
            Plugin.Log.LogInfo($"[RecipeSprite] Loaded custom icon for '{ammoId}' ({texture.width}x{texture.height})");
            return sprite;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[RecipeSprite] Failed to load icon for '{ammoId}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取 fallback Sprite（自定义图标加载失败时使用）
    /// </summary>
    private static Sprite GetFallbackSprite(string ammoId)
    {
        // 尝试从缓存的其他弹药图标中选一个
        foreach (var kvp in _spriteCache)
        {
            if (kvp.Value != null)
                return kvp.Value;
        }
        return null;
    }

    /// <summary>
    /// 获取原版同类弹药的 Sprite 作为最后 fallback
    /// 使用 Resources.Load 加载原版弹药预制体（这些在游戏 Resources 中存在）
    /// </summary>
    private static Sprite GetBaseGameSprite(string ammoId)
    {
        try
        {
            // 根据弹药 ID 确定原版弹药预制体 ID
            var baseId = GetBaseGameItemId(ammoId);
            if (baseId == null) return null;

            var prefab = Resources.Load<GameObject>(baseId);
            if (prefab == null) return null;

            var sr = prefab.GetComponent<SpriteRenderer>();
            return sr?.sprite;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 根据自定义物品 ID 返回对应的原版预制体 ID（用于 fallback 图标）
    /// </summary>
    private static string GetBaseGameItemId(string itemId)
    {
        // 弹匣 → "riflemagazine" 或 "smallmagazine"
        if (CustomMagIds.Contains(itemId))
        {
            // 手枪弹匣（glock17, deagle）用 smallmagazine，步枪弹匣用 riflemagazine
            switch (itemId)
            {
                case Glock17MagItemSystem.ItemKey: return "smallmagazine";
                case DeagleMagItemSystem.ItemKey: return "smallmagazine";
                default: return "riflemagazine";
            }
        }

        // 所有步枪弹 → "556round", 霰弹 → "12gauge", 手枪弹 → "9mmround"
        switch (itemId)
        {
            case Ammo12g85ItemSystem.ItemKey: return "12gauge";
            // 以下全部基于 Rifle (556round) 预制体
            case Ammo338UCWItemSystem.ItemKey: return "556round";
            case Ammo76251BPZItemSystem.ItemKey: return "556round";
            case Ammo76239SPItemSystem.ItemKey: return "556round";
            case Ammo50CopperItemSystem.ItemKey: return "556round";
            case Ammo45FMJItemSystem.ItemKey: return "556round";
            case Ammo919PSOItemSystem.ItemKey: return "556round";
            case Ammo55645FMJItemSystem.ItemKey: return "556round";
            case Ammo5728SB193ItemSystem.ItemKey: return "556round";
            default: return "riflemagazine"; // 通用 fallback
        }
    }
}
