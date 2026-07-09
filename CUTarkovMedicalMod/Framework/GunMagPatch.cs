using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 自定义弹匣系统 — 为 AXMC (.338 LM) 和 DVL-10 (7.62x51) 提供专用弹匣。
/// 
/// 核心问题：
/// - 原版 GunScript.UnloadMag 硬编码 AmmoTypeToMagazine(Rifle) → "riflemagazine" (30发)
/// - 原版 GunScript.LoadMag 只检查 ammoType == gun.ammoType，任何 Rifle 弹匣都能装进自定义枪
/// 
/// 修复方案：
/// - Harmony Prefix GunScript.LoadMag → 限制自定义枪只接受对应弹匣ID
/// - Harmony Prefix GunScript.UnloadMag → 为自定义枪生成对应弹匣 + 播放自定义音效
/// - 自定义弹匣物品基于 "riflemagazine" prefab 克隆，修改 AmmoScript 字段 + 自定义图标
/// </summary>
public static class AXMCMagItemSystem
{
    public const string ItemKey = "axmc_mag";
    public const string BaseGameItemId = "riflemagazine";
    public const int MaxRounds = 10;

    public static string DisplayName => I18n.Tr("axmc_mag.name");
    public static string Description => I18n.Tr("axmc_mag.desc");

    private static Sprite? _cachedIcon;

    public static bool IsAXMCMagRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置生成的 AXMC 弹匣物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsAXMCMagRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Magazine;
            ammo.ammoType = GunScript.AmmoType.Rifle;
            ammo.maxRounds = MaxRounds;
            ammo.rounds = MaxRounds;

            Plugin.Log.LogInfo($"[AXMC_MAG] Configured AmmoScript: maxRounds={MaxRounds}, rounds={ammo.rounds}");
        }

        // 设置自定义图标
        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        ResizeColliderToSprite(item);

        // 添加标记组件
        var marker = item.gameObject.GetComponent<AXMCMagItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<AXMCMagItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[AXMC_MAG] Configured spawned item '{ItemKey}'.");
    }

    /// <summary>
    /// 在 Item.GlobalItems 注册 AXMC 弹匣的 ItemInfo。
    /// 克隆 riflemagazine 的 ItemInfo，修改名称、描述、重量、价值。
    /// </summary>
    public static bool EnsureRegisteredInItemTable()
    {
        if (Item.GlobalItems.ContainsKey(ItemKey))
            return false;

        try
        {
            if (Item.GlobalItems.TryGetValue(BaseGameItemId, out var source))
            {
                Item.GlobalItems[ItemKey] = CloneItemInfo(source);
                Plugin.Log.LogInfo($"[AXMC_MAG] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[AXMC_MAG] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[AXMC_MAG] Failed to register '{ItemKey}': {ex}");
            return false;
        }
    }

    private static ItemInfo CloneItemInfo(ItemInfo source)
    {
        var clone = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = source.category,
            slotRotation = source.slotRotation,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 0,
            tags = "belttool",
            rec = new Recognition(9),
        };

        // 设置 useAction 为 UnloadRound
        var useMethod = typeof(AXMCMagItemSystem).GetMethod(
            nameof(MagUseAction),
            BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        clone.SetTags();
        return clone;
    }

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "custom",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            value = 0,
            tags = "belttool",
            rec = new Recognition(9),
        };

        var useMethod = typeof(AXMCMagItemSystem).GetMethod(
            nameof(MagUseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        info.SetTags();
        return info;
    }

    /// <summary>
    /// 弹匣使用动作 — 退出一发子弹。
    /// </summary>
    private static void MagUseAction(Body body, Item item)
    {
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
            ammo.UnloadRound();
    }

    // ===== Icon =====

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            // Unity 不支持 WebP 格式，优先使用 PNG
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "axmc_magazine.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "axmc_magazine.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 13.35f);
            _cachedIcon.name = "axmc-mag-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AXMC_MAG] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    private static void ResizeColliderToSprite(Item item)
    {
        var sr = item.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        var col = item.GetComponent<BoxCollider2D>();
        if (col == null) col = item.gameObject.AddComponent<BoxCollider2D>();
        var bounds = sr.sprite.bounds;
        col.size = new Vector2(bounds.size.x, bounds.size.y);
        col.offset = Vector2.zero;
    }
}

public sealed class AXMCMagItemMarker : MonoBehaviour
{
    public string displayName = AXMCMagItemSystem.DisplayName;
    public string description = AXMCMagItemSystem.Description;
}

// ===== DVL-10 弹匣 =====

public static class DVL10MagItemSystem
{
    public const string ItemKey = "dvl10_mag";
    public const string BaseGameItemId = "riflemagazine";
    public const int MaxRounds = 10;

    public static string DisplayName => I18n.Tr("dvl10_mag.name");
    public static string Description => I18n.Tr("dvl10_mag.desc");

    private static Sprite? _cachedIcon;

    public static bool IsDVL10MagRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsDVL10MagRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Magazine;
            ammo.ammoType = GunScript.AmmoType.Rifle;
            ammo.maxRounds = MaxRounds;
            ammo.rounds = MaxRounds;

            Plugin.Log.LogInfo($"[DVL10_MAG] Configured AmmoScript: maxRounds={MaxRounds}, rounds={ammo.rounds}");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<DVL10MagItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<DVL10MagItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[DVL10_MAG] Configured spawned item '{ItemKey}'.");
    }

    public static bool EnsureRegisteredInItemTable()
    {
        if (Item.GlobalItems.ContainsKey(ItemKey))
            return false;

        try
        {
            if (Item.GlobalItems.TryGetValue(BaseGameItemId, out var source))
            {
                Item.GlobalItems[ItemKey] = CloneItemInfo(source);
                Plugin.Log.LogInfo($"[DVL10_MAG] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[DVL10_MAG] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[DVL10_MAG] Failed to register '{ItemKey}': {ex}");
            return false;
        }
    }

    private static ItemInfo CloneItemInfo(ItemInfo source)
    {
        var clone = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = source.category,
            slotRotation = source.slotRotation,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 0,
            tags = "belttool",
            rec = new Recognition(9),
        };

        var useMethod = typeof(DVL10MagItemSystem).GetMethod(
            nameof(MagUseAction),
            BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        clone.SetTags();
        return clone;
    }

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "custom",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            value = 0,
            tags = "belttool",
            rec = new Recognition(9),
        };

        var useMethod = typeof(DVL10MagItemSystem).GetMethod(
            nameof(MagUseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        info.SetTags();
        return info;
    }

    private static void MagUseAction(Body body, Item item)
    {
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
            ammo.UnloadRound();
    }

    // ===== Icon =====

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            // Unity 不支持 WebP 格式，优先使用 PNG
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "dvl10_magazine.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "dvl10_magazine.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 26.7f);
            _cachedIcon.name = "dvl10-mag-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[DVL10_MAG] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    private static void ResizeColliderToSprite(Item item)
    {
        var sr = item.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        var col = item.GetComponent<BoxCollider2D>();
        if (col == null) col = item.gameObject.AddComponent<BoxCollider2D>();
        var bounds = sr.sprite.bounds;
        col.size = new Vector2(bounds.size.x, bounds.size.y);
        col.offset = Vector2.zero;
    }
}

public sealed class DVL10MagItemMarker : MonoBehaviour
{
    public string displayName = DVL10MagItemSystem.DisplayName;
    public string description = DVL10MagItemSystem.Description;
}

// ===== AKM 弹匣 =====

public static class AKMMagItemSystem
{
    public const string ItemKey = "akm_mag";
    public const string BaseGameItemId = "riflemagazine";
    public const int MaxRounds = 30;

    public static string DisplayName => I18n.Tr("akm_mag.name");
    public static string Description => I18n.Tr("akm_mag.desc");

    private static Sprite? _cachedIcon;

    public static bool IsAKMMagRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsAKMMagRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Magazine;
            ammo.ammoType = GunScript.AmmoType.Rifle;
            ammo.maxRounds = MaxRounds;
            ammo.rounds = MaxRounds;

            Plugin.Log.LogInfo($"[AKM_MAG] Configured AmmoScript: maxRounds={MaxRounds}, rounds={ammo.rounds}");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<AKMMagItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<AKMMagItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[AKM_MAG] Configured spawned item '{ItemKey}'.");
    }

    public static bool EnsureRegisteredInItemTable()
    {
        if (Item.GlobalItems.ContainsKey(ItemKey))
            return false;

        try
        {
            if (Item.GlobalItems.TryGetValue(BaseGameItemId, out var source))
            {
                Item.GlobalItems[ItemKey] = CloneItemInfo(source);
                Plugin.Log.LogInfo($"[AKM_MAG] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[AKM_MAG] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[AKM_MAG] Failed to register '{ItemKey}': {ex}");
            return false;
        }
    }

    private static ItemInfo CloneItemInfo(ItemInfo source)
    {
        var clone = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = source.category,
            slotRotation = source.slotRotation,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(AKMMagItemSystem).GetMethod(
            nameof(MagUseAction),
            BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        clone.SetTags();
        return clone;
    }

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "custom",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(AKMMagItemSystem).GetMethod(
            nameof(MagUseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        info.SetTags();
        return info;
    }

    private static void MagUseAction(Body body, Item item)
    {
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
            ammo.UnloadRound();
    }

    // ===== Icon =====

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "akm_magazine.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "akm_magazine.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 26.7f);
            _cachedIcon.name = "akm-mag-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AKM_MAG] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    private static void ResizeColliderToSprite(Item item)
    {
        var sr = item.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        var col = item.GetComponent<BoxCollider2D>();
        if (col == null) col = item.gameObject.AddComponent<BoxCollider2D>();
        var bounds = sr.sprite.bounds;
        col.size = new Vector2(bounds.size.x, bounds.size.y);
        col.offset = Vector2.zero;
    }
}

public sealed class AKMMagItemMarker : MonoBehaviour
{
    public string displayName = AKMMagItemSystem.DisplayName;
    public string description = AKMMagItemSystem.Description;
}

// ===== Deagle Magazine (.50 AE, 7 rounds) =====

public static class DeagleMagItemSystem
{
    public const string ItemKey = "deagle_mag";
    public const string BaseGameItemId = "riflemagazine";
    public const int MaxRounds = 7;

    public static string DisplayName => I18n.Tr("deagle_mag.name");
    public static string Description => I18n.Tr("deagle_mag.desc");

    private static Sprite? _cachedIcon;

    public static bool IsDeagleMagRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsDeagleMagRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Magazine;
            ammo.ammoType = GunScript.AmmoType.Pistol;
            ammo.maxRounds = MaxRounds;
            ammo.rounds = MaxRounds;

            Plugin.Log.LogInfo($"[Deagle_MAG] Configured AmmoScript: maxRounds={MaxRounds}, rounds={ammo.rounds}");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<DeagleMagItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<DeagleMagItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[Deagle_MAG] Configured spawned item '{ItemKey}'.");
    }

    public static bool EnsureRegisteredInItemTable()
    {
        if (Item.GlobalItems.ContainsKey(ItemKey))
            return false;

        try
        {
            if (Item.GlobalItems.TryGetValue(BaseGameItemId, out var source))
            {
                Item.GlobalItems[ItemKey] = CloneItemInfo(source);
                Plugin.Log.LogInfo($"[Deagle_MAG] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[Deagle_MAG] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Deagle_MAG] Failed to register '{ItemKey}': {ex}");
            return false;
        }
    }

    private static ItemInfo CloneItemInfo(ItemInfo source)
    {
        var clone = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = source.category,
            slotRotation = source.slotRotation,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(DeagleMagItemSystem).GetMethod(
            nameof(MagUseAction),
            BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        clone.SetTags();
        return clone;
    }

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "custom",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(DeagleMagItemSystem).GetMethod(
            nameof(MagUseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        info.SetTags();
        return info;
    }

    private static void MagUseAction(Body body, Item item)
    {
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
            ammo.UnloadRound();
    }

    // ===== Icon =====

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "deagle_magazine.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "deagle_magazine.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 26.7f);
            _cachedIcon.name = "deagle-mag-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Deagle_MAG] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    private static void ResizeColliderToSprite(Item item)
    {
        var sr = item.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        var col = item.GetComponent<BoxCollider2D>();
        if (col == null) col = item.gameObject.AddComponent<BoxCollider2D>();
        var bounds = sr.sprite.bounds;
        col.size = new Vector2(bounds.size.x, bounds.size.y);
        col.offset = Vector2.zero;
    }
}

public sealed class DeagleMagItemMarker : MonoBehaviour
{
    public string displayName = DeagleMagItemSystem.DisplayName;
    public string description = DeagleMagItemSystem.Description;
}

// ===== Glock 17 Magazine (9x19, 17 rounds) =====

public static class Glock17MagItemSystem
{
    public const string ItemKey = "glock17_mag";
    public const string BaseGameItemId = "riflemagazine";
    public const int MaxRounds = 17;

    public static string DisplayName => I18n.Tr("glock17_mag.name");
    public static string Description => I18n.Tr("glock17_mag.desc");

    private static Sprite? _cachedIcon;

    public static bool IsGlock17MagRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsGlock17MagRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Magazine;
            ammo.ammoType = GunScript.AmmoType.Pistol;
            ammo.maxRounds = MaxRounds;
            ammo.rounds = MaxRounds;

            Plugin.Log.LogInfo($"[Glock17_MAG] Configured AmmoScript: maxRounds={MaxRounds}, rounds={ammo.rounds}");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<Glock17MagItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Glock17MagItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[Glock17_MAG] Configured spawned item '{ItemKey}'.");
    }

    public static bool EnsureRegisteredInItemTable()
    {
        if (Item.GlobalItems.ContainsKey(ItemKey))
            return false;

        try
        {
            if (Item.GlobalItems.TryGetValue(BaseGameItemId, out var source))
            {
                Item.GlobalItems[ItemKey] = CloneItemInfo(source);
                Plugin.Log.LogInfo($"[Glock17_MAG] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[Glock17_MAG] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Glock17_MAG] Failed to register '{ItemKey}': {ex}");
            return false;
        }
    }

    private static ItemInfo CloneItemInfo(ItemInfo source)
    {
        var clone = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = source.category,
            slotRotation = source.slotRotation,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(Glock17MagItemSystem).GetMethod(
            nameof(MagUseAction),
            BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        clone.SetTags();
        return clone;
    }

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "custom",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(Glock17MagItemSystem).GetMethod(
            nameof(MagUseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        info.SetTags();
        return info;
    }

    private static void MagUseAction(Body body, Item item)
    {
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
            ammo.UnloadRound();
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "glock_magazine.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "glock_magazine.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 26.7f);
            _cachedIcon.name = "glock17-mag-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Glock17_MAG] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    private static void ResizeColliderToSprite(Item item)
    {
        var sr = item.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        var col = item.GetComponent<BoxCollider2D>();
        if (col == null) col = item.gameObject.AddComponent<BoxCollider2D>();
        var bounds = sr.sprite.bounds;
        col.size = new Vector2(bounds.size.x, bounds.size.y);
        col.offset = Vector2.zero;
    }
}

public sealed class Glock17MagItemMarker : MonoBehaviour
{
    public string displayName = Glock17MagItemSystem.DisplayName;
    public string description = Glock17MagItemSystem.Description;
}

// ===== M4A1 Magazine (5.56x45, 30 rounds) =====

public static class M4A1MagItemSystem
{
    public const string ItemKey = "m4a1_mag";
    public const string BaseGameItemId = "riflemagazine";
    public const int MaxRounds = 30;

    public static string DisplayName => I18n.Tr("m4a1_mag.name");
    public static string Description => I18n.Tr("m4a1_mag.desc");

    private static Sprite? _cachedIcon;

    public static bool IsM4A1MagRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsM4A1MagRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Magazine;
            ammo.ammoType = GunScript.AmmoType.Rifle;
            ammo.maxRounds = MaxRounds;
            ammo.rounds = MaxRounds;

            Plugin.Log.LogInfo($"[M4A1_MAG] Configured AmmoScript: maxRounds={MaxRounds}, rounds={ammo.rounds}");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<M4A1MagItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<M4A1MagItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[M4A1_MAG] Configured spawned item '{ItemKey}'.");
    }

    public static bool EnsureRegisteredInItemTable()
    {
        if (Item.GlobalItems.ContainsKey(ItemKey))
            return false;

        try
        {
            if (Item.GlobalItems.TryGetValue(BaseGameItemId, out var source))
            {
                Item.GlobalItems[ItemKey] = CloneItemInfo(source);
                Plugin.Log.LogInfo($"[M4A1_MAG] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[M4A1_MAG] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[M4A1_MAG] Failed to register '{ItemKey}': {ex}");
            return false;
        }
    }

    private static ItemInfo CloneItemInfo(ItemInfo source)
    {
        var clone = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = source.category,
            slotRotation = source.slotRotation,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(M4A1MagItemSystem).GetMethod(
            nameof(MagUseAction),
            BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        clone.SetTags();
        return clone;
    }

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "custom",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(M4A1MagItemSystem).GetMethod(
            nameof(MagUseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        info.SetTags();
        return info;
    }

    private static void MagUseAction(Body body, Item item)
    {
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
            ammo.UnloadRound();
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "m4a1_magazine.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "m4a1_magazine.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 20f);
            _cachedIcon.name = "m4a1-mag-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[M4A1_MAG] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    private static void ResizeColliderToSprite(Item item)
    {
        var sr = item.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        var col = item.GetComponent<BoxCollider2D>();
        if (col == null) col = item.gameObject.AddComponent<BoxCollider2D>();
        var bounds = sr.sprite.bounds;
        col.size = new Vector2(bounds.size.x, bounds.size.y);
        col.offset = Vector2.zero;
    }
}

public sealed class M4A1MagItemMarker : MonoBehaviour
{
    public string displayName = M4A1MagItemSystem.DisplayName;
    public string description = M4A1MagItemSystem.Description;
}

// ===== P90 Magazine (5.7x28, 50 rounds) =====

public static class P90MagItemSystem
{
    public const string ItemKey = "p90_mag";
    public const string BaseGameItemId = "riflemagazine";
    public const int MaxRounds = 50;

    public static string DisplayName => I18n.Tr("p90_mag.name");
    public static string Description => I18n.Tr("p90_mag.desc");

    private static Sprite? _cachedIcon;

    public static bool IsP90MagRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsP90MagRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Magazine;
            ammo.ammoType = GunScript.AmmoType.Rifle;
            ammo.maxRounds = MaxRounds;
            ammo.rounds = MaxRounds;

            Plugin.Log.LogInfo($"[P90_MAG] Configured AmmoScript: maxRounds={MaxRounds}, rounds={ammo.rounds}");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<P90MagItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<P90MagItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[P90_MAG] Configured spawned item '{ItemKey}'.");
    }

    public static bool EnsureRegisteredInItemTable()
    {
        if (Item.GlobalItems.ContainsKey(ItemKey))
            return false;

        try
        {
            if (Item.GlobalItems.TryGetValue(BaseGameItemId, out var source))
            {
                Item.GlobalItems[ItemKey] = CloneItemInfo(source);
                Plugin.Log.LogInfo($"[P90_MAG] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[P90_MAG] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[P90_MAG] Failed to register '{ItemKey}': {ex}");
            return false;
        }
    }

    private static ItemInfo CloneItemInfo(ItemInfo source)
    {
        var clone = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = source.category,
            slotRotation = source.slotRotation,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(P90MagItemSystem).GetMethod(
            nameof(MagUseAction),
            BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        clone.SetTags();
        return clone;
    }

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "custom",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 0.2f,
            scaleWeightWithCondition = false,
            value = 0,
            tags = "belttool",
            rec = new Recognition(10),
        };

        var useMethod = typeof(P90MagItemSystem).GetMethod(
            nameof(MagUseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

        info.SetTags();
        return info;
    }

    private static void MagUseAction(Body body, Item item)
    {
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
            ammo.UnloadRound();
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "p90_magazine.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "p90_magazine.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.3f, 0.5f), 19f);
            _cachedIcon.name = "p90-mag-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[P90_MAG] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    private static void ResizeColliderToSprite(Item item)
    {
        var sr = item.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;
        var col = item.GetComponent<BoxCollider2D>();
        if (col == null) col = item.gameObject.AddComponent<BoxCollider2D>();
        var bounds = sr.sprite.bounds;
        col.size = new Vector2(bounds.size.x, bounds.size.y);
        col.offset = Vector2.zero;
    }
}

public sealed class P90MagItemMarker : MonoBehaviour
{
    public string displayName = P90MagItemSystem.DisplayName;
    public string description = P90MagItemSystem.Description;
}

// ===== Harmony Patches =====

/// <summary>
/// 拦截 GunScript.LoadMag — 限制自定义枪只接受对应弹匣ID。
/// 原版逻辑只检查 ammoType == gun.ammoType，导致 riflemagazine (30发) 可以装进 AXMC/DVL10。
/// </summary>
[HarmonyPatch(typeof(GunScript), nameof(GunScript.LoadMag))]
public static class GunLoadMagPatch
{
    /// <summary>枪ID → 对应弹匣ID 的映射</summary>
    private static readonly Dictionary<string, string> GunToMagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { AXMCItemSystem.ItemKey, AXMCMagItemSystem.ItemKey },
        { DVL10ItemSystem.ItemKey, DVL10MagItemSystem.ItemKey },
        { AKMItemSystem.ItemKey, AKMMagItemSystem.ItemKey },
        { DeagleItemSystem.ItemKey, DeagleMagItemSystem.ItemKey },
        { Glock17ItemSystem.ItemKey, Glock17MagItemSystem.ItemKey },
        { M4A1ItemSystem.ItemKey, M4A1MagItemSystem.ItemKey },
        { P90ItemSystem.ItemKey, P90MagItemSystem.ItemKey },
    };

    /// <summary>自定义枪ID → magin音效文件名的映射</summary>
    private static readonly Dictionary<string, (string fileName, string gunDir)> MagInSoundMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { AXMCItemSystem.ItemKey, ("axmc_magin", "ax") },
        { DVL10ItemSystem.ItemKey, ("dvl10_magin", "dvl10") },
        { AKMItemSystem.ItemKey, ("akm_magin", "akm") },
        { DeagleItemSystem.ItemKey, ("deagle_magin", "deagle") },
        { Glock17ItemSystem.ItemKey, ("glock_magin", "glock") },
        { M4A1ItemSystem.ItemKey, ("m4_magin", "m4") },
        { P90ItemSystem.ItemKey, ("p90_magin", "p90") },
    };

    [HarmonyPrefix]
    public static bool Prefix(GunScript __instance, AmmoScript ammo)
    {
        var item = __instance.GetComponent<Item>();
        if (item == null) return true; // 让原方法处理

        var gunId = item.id;

        // === 口径检查：单发子弹 (Round) ===
        // 阻止错误口径的子弹装入枪膛或直供弹仓
        // 例如：556round 不能装入 AXMC (.338 LM) 或 SKS (7.62x39)
        if (ammo.itemType == AmmoScript.AmmoItemType.Round)
        {
            var roundItem = ammo.GetComponent<Item>();
            var roundId = roundItem?.id ?? "";

            if (!CaliberRegistry.IsCaliberAllowed(gunId, roundId))
            {
                Plugin.Log.LogInfo($"[GunMagPatch] Blocked round '{roundId}' for gun '{gunId}' (caliber mismatch).");
                return false;
            }
        }

        // === 弹匣处理 (AXMC/DVL-10) ===
        if (GunToMagMap.ContainsKey(gunId))
        {

        // 如果弹匣物品是 Magazine 类型，检查是否匹配对应弹匣ID
        if (ammo.itemType == AmmoScript.AmmoItemType.Magazine)
        {
            var ammoItem = ammo.GetComponent<Item>();
            var expectedMagId = GunToMagMap[gunId];

            // 只允许对应弹匣装入
            if (ammoItem == null || ammoItem.id != expectedMagId)
            {
                Plugin.Log.LogInfo($"[GunMagPatch] Blocked wrong magazine '{ammoItem?.id ?? "null"}' for gun '{gunId}', expected '{expectedMagId}'.");
                return false;
            }

            // 对应弹匣可以装 — 执行自定义装弹匣逻辑（替代原方法）
            if (!__instance.hasMag && __instance.feedType == GunScript.FeedType.Mag)
            {
                __instance.hasMag = true;
                __instance.roundsInMag = ammo.rounds;
                // 播放自定义 magin 音效
                if (MagInSoundMap.TryGetValue(gunId, out var soundInfo))
                {
                    var clip = GunMagSoundHelper.TryLoadSound(soundInfo.fileName, soundInfo.gunDir);
                    if (clip != null)
                        Sound.Play(clip, __instance.transform.position, true);
                    else
                        Sound.Play("gunloadmag", __instance.transform.position);
                }
                else
                {
                    Sound.Play("gunloadmag", __instance.transform.position);
                }
                UnityEngine.Object.Destroy(ammo.gameObject);

                Plugin.Log.LogInfo($"[GunMagPatch] Loaded correct magazine '{expectedMagId}' into gun '{gunId}', rounds={ammo.rounds}.");
            }

            return false; // 跳过原方法
        }
        }

        // Round（口径已检查）或原版枪：放行原版逻辑
        return true;
    }
}

/// <summary>
/// 拦截 GunScript.UnloadMag — 为自定义枪生成对应弹匣 + 播放自定义音效。
/// 原版逻辑硬编码 AmmoTypeToMagazine(Rifle) → "riflemagazine" (30发)。
/// </summary>
[HarmonyPatch(typeof(GunScript), nameof(GunScript.UnloadMag))]
public static class GunUnloadMagPatch
{
    /// <summary>枪ID → 对应弹匣ID 的映射</summary>
    private static readonly Dictionary<string, string> GunToMagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { AXMCItemSystem.ItemKey, AXMCMagItemSystem.ItemKey },
        { DVL10ItemSystem.ItemKey, DVL10MagItemSystem.ItemKey },
        { AKMItemSystem.ItemKey, AKMMagItemSystem.ItemKey },
        { DeagleItemSystem.ItemKey, DeagleMagItemSystem.ItemKey },
        { Glock17ItemSystem.ItemKey, Glock17MagItemSystem.ItemKey },
        { M4A1ItemSystem.ItemKey, M4A1MagItemSystem.ItemKey },
        { P90ItemSystem.ItemKey, P90MagItemSystem.ItemKey },
    };

    /// <summary>自定义枪ID → magout音效文件名的映射</summary>
    private static readonly Dictionary<string, (string fileName, string gunDir)> MagOutSoundMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { AXMCItemSystem.ItemKey, ("axmc_magout", "ax") },
        { DVL10ItemSystem.ItemKey, ("dvl10_magout", "dvl10") },
        { AKMItemSystem.ItemKey, ("akm_magout", "akm") },
        { DeagleItemSystem.ItemKey, ("deagle_magout", "deagle") },
        { Glock17ItemSystem.ItemKey, ("glock_magout", "glock") },
        { M4A1ItemSystem.ItemKey, ("m4_magout", "m4") },
        { P90ItemSystem.ItemKey, ("p90_magout", "p90") },
    };

    [HarmonyPrefix]
    public static bool Prefix(GunScript __instance)
    {
        if (!__instance.hasMag || __instance.feedType == GunScript.FeedType.Direct)
            return true; // 让原方法处理

        var item = __instance.GetComponent<Item>();
        if (item == null) return true;

        // 如果不是自定义枪，让原方法处理
        if (!GunToMagMap.ContainsKey(item.id))
            return true;

        // 播放自定义 magout 音效
        if (MagOutSoundMap.TryGetValue(item.id, out var soundInfo))
        {
            var clip = GunMagSoundHelper.TryLoadSound(soundInfo.fileName, soundInfo.gunDir);
            if (clip != null)
                Sound.Play(clip, __instance.transform.position, true);
            else
                Sound.Play("gununloadmag", __instance.transform.position);
        }
        else
        {
            Sound.Play("gununloadmag", __instance.transform.position);
        }

        __instance.hasMag = false;

        // 生成自定义弹匣
        var expectedMagId = GunToMagMap[item.id];
        SpawnCustomMagazine(__instance, expectedMagId, __instance.roundsInMag);

        __instance.roundsInMag = 0;

        Plugin.Log.LogInfo($"[GunMagPatch] Unloaded custom magazine '{expectedMagId}' from gun '{item.id}', rounds={__instance.roundsInMag}.");

        return false; // 跳过原方法
    }

    /// <summary>
    /// 生成自定义弹匣物品到枪附近，自动拾取。
    /// </summary>
    private static void SpawnCustomMagazine(GunScript gun, string magId, int rounds)
    {
        try
        {
            var body = gun.body;
            if (body == null)
            {
                Plugin.Log.LogWarning($"[GunMagPatch] No body found for magazine spawn.");
                return;
            }

            // 基于原版 riflemagazine prefab 创建弹匣实例
            var prefab = Resources.Load<GameObject>("riflemagazine");
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[GunMagPatch] riflemagazine prefab not found, cannot spawn '{magId}'.");
                return;
            }

            var go = UnityEngine.Object.Instantiate(prefab);
            var newItem = go.GetComponent<Item>();
            if (newItem == null)
            {
                UnityEngine.Object.Destroy(go);
                return;
            }

            // 配置自定义弹匣
            var request = new MedicalGrantRequest(magId, magId, 1, "MagUnload", "riflemagazine");
            if (magId == AXMCMagItemSystem.ItemKey)
                AXMCMagItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (magId == DVL10MagItemSystem.ItemKey)
                DVL10MagItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (magId == AKMMagItemSystem.ItemKey)
                AKMMagItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (magId == DeagleMagItemSystem.ItemKey)
                DeagleMagItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (magId == Glock17MagItemSystem.ItemKey)
                Glock17MagItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (magId == M4A1MagItemSystem.ItemKey)
                M4A1MagItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (magId == P90MagItemSystem.ItemKey)
                P90MagItemSystem.ConfigureSpawnedItem(newItem, request);

            // 修正弹匣中的子弹数量为退弹时的数量
            var ammo = newItem.GetComponent<AmmoScript>();
            if (ammo != null)
                ammo.rounds = rounds;

            // 放到枪附近并自动拾取
            go.transform.position = gun.transform.position;
            go.AddComponent<FreshItemDrop>();
            body.AutoPickUpItem(newItem);

            Plugin.Log.LogInfo($"[GunMagPatch] Spawned custom magazine '{magId}' with {rounds} rounds.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GunMagPatch] Failed to spawn custom magazine '{magId}': {ex}");
        }
    }

}

/// <summary>
/// 弹匣悬停描述补丁。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class AXMCMagHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref (string, string) __result)
    {
        var marker = item.GetComponent<AXMCMagItemMarker>();
        if (marker == null) return;

        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class DVL10MagHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref (string, string) __result)
    {
        var marker = item.GetComponent<DVL10MagItemMarker>();
        if (marker == null) return;

        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class AKMMagHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref (string, string) __result)
    {
        var marker = item.GetComponent<AKMMagItemMarker>();
        if (marker == null) return;

        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}

/// <summary>
/// 弹匣音效加载辅助类。
/// </summary>
public static class GunMagSoundHelper
{
    private static readonly Dictionary<string, AudioClip> _cachedSounds = new();

    public static AudioClip? TryLoadSound(string fileName, string gunDir)
    {
        var key = $"{gunDir}/{fileName}";
        if (_cachedSounds.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var soundPath = Path.Combine(assemblyDir, "Framework", "Assets", "guns", gunDir, $"{fileName}.wav");
            if (File.Exists(soundPath))
            {
                var clip = LoadWavSync(soundPath);
                if (clip != null)
                {
                    _cachedSounds[key] = clip;
                    return clip;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GunMagSound] Failed to load sound '{fileName}': {ex.Message}");
        }

        return null;
    }

    private static AudioClip? LoadWavSync(string path)
    {
        try
        {
            using var uwr = UnityWebRequestMultimedia.GetAudioClip("file:///" + path, AudioType.WAV);
            uwr.SendWebRequest();
            while (!uwr.isDone) { }
            if (uwr.result == UnityWebRequest.Result.Success)
                return DownloadHandlerAudioClip.GetContent(uwr);
        }
        catch { }
        return null;
    }
}
