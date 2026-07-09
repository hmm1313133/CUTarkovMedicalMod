using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

// ===== 7.62x51mm BPZ FMJ (DVL-10) =====

/// <summary>
/// 7.62x51 毫米 BPZ FMJ 步枪弹。
/// 适用于 DVL-10 狙击步枪。
/// AmmoScript: itemType=Round, ammoType=Rifle, maxRounds=1, rounds=1
/// </summary>
public static class Ammo76251BPZItemSystem
{
    public const string ItemKey = "76251bpz";
    public const string BaseGameItemId = "556round";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Rifle;

    public static string DisplayName => I18n.Tr("76251bpz.name");
    public static string Description => I18n.Tr("76251bpz.desc");

    private static Sprite? _cachedIcon;

    public static bool Is76251BPZRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is76251BPZRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[76251BPZ] Configured AmmoScript: itemType=Round, ammoType=Rifle");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo76251BPZMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo76251BPZMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[76251BPZ] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[76251BPZ] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[76251BPZ] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[76251BPZ] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "76251bpz.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "76251bpz.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "76251bpz-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[76251BPZ] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo76251BPZMarker : MonoBehaviour
{
    public string displayName = Ammo76251BPZItemSystem.DisplayName;
    public string description = Ammo76251BPZItemSystem.Description;
}

// ===== 7.62x39mm SP (SKS) =====

/// <summary>
/// 7.62x39 毫米 SP 步枪弹。
/// 适用于 SKS 卡宾枪。
/// AmmoScript: itemType=Round, ammoType=Rifle, maxRounds=1, rounds=1
/// </summary>
public static class Ammo76239SPItemSystem
{
    public const string ItemKey = "76239sp";
    public const string BaseGameItemId = "556round";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Rifle;

    public static string DisplayName => I18n.Tr("76239sp.name");
    public static string Description => I18n.Tr("76239sp.desc");

    private static Sprite? _cachedIcon;

    public static bool Is76239SPRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is76239SPRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[76239SP] Configured AmmoScript: itemType=Round, ammoType=Rifle");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo76239SPMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo76239SPMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[76239SP] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[76239SP] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[76239SP] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[76239SP] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "76239sp.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "76239sp.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "76239sp-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[76239SP] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo76239SPMarker : MonoBehaviour
{
    public string displayName = Ammo76239SPItemSystem.DisplayName;
    public string description = Ammo76239SPItemSystem.Description;
}

// ===== 12/70 Magnum 8.5mm Buckshot (MP133) =====

/// <summary>
/// 12/70 Magnum 8.5 毫米鹿弹。
/// 适用于 MP-133 霰弹枪。
/// AmmoScript: itemType=Round, ammoType=Shotgun, maxRounds=1, rounds=1
/// </summary>
public static class Ammo12g85ItemSystem
{
    public const string ItemKey = "12g85";
    public const string BaseGameItemId = "12gauge";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Shotgun;

    public static string DisplayName => I18n.Tr("12g85.name");
    public static string Description => I18n.Tr("12g85.desc");

    private static Sprite? _cachedIcon;

    public static bool Is12g85Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is12g85Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[12g85] Configured AmmoScript: itemType=Round, ammoType=Shotgun");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo12g85Marker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo12g85Marker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[12g85] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[12g85] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[12g85] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[12g85] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "12g85.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "12g85.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "12g85-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[12g85] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo12g85Marker : MonoBehaviour
{
    public string displayName = Ammo12g85ItemSystem.DisplayName;
    public string description = Ammo12g85ItemSystem.Description;
}

// ===== .338 Lapua Magnum UCW (AXMC) =====

/// <summary>
/// .338 Lapua Magnum UCW 步枪弹。
/// 适用于 AXMC 狙击步枪。
/// AmmoScript: itemType=Round, ammoType=Rifle, maxRounds=1, rounds=1
/// </summary>
public static class Ammo338UCWItemSystem
{
    public const string ItemKey = "338ucw";
    public const string BaseGameItemId = "556round";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Rifle;

    public static string DisplayName => I18n.Tr("338ucw.name");
    public static string Description => I18n.Tr("338ucw.desc");

    private static Sprite? _cachedIcon;

    public static bool Is338UCWRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is338UCWRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[338UCW] Configured AmmoScript: itemType=Round, ammoType=Rifle");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo338UCWMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo338UCWMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[338UCW] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[338UCW] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[338UCW] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[338UCW] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(10),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(10),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "338ucw.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "338ucw.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "338ucw-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[338UCW] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo338UCWMarker : MonoBehaviour
{
    public string displayName = Ammo338UCWItemSystem.DisplayName;
    public string description = Ammo338UCWItemSystem.Description;
}

// ===== .50 AE Solid Copper =====

/// <summary>
/// .50 AE 铜质实心弹。
/// AmmoScript: itemType=Round, ammoType=Pistol, maxRounds=1, rounds=1
/// </summary>
public static class Ammo50CopperItemSystem
{
    public const string ItemKey = "50copper";
    public const string BaseGameItemId = "556round";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Pistol;

    public static string DisplayName => I18n.Tr("50copper.name");
    public static string Description => I18n.Tr("50copper.desc");

    private static Sprite? _cachedIcon;

    public static bool Is50CopperRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is50CopperRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[50Copper] Configured AmmoScript: itemType=Round, ammoType=Pistol");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo50CopperMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo50CopperMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[50Copper] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[50Copper] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[50Copper] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[50Copper] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "50copper.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "50copper.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "50copper-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[50Copper] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo50CopperMarker : MonoBehaviour
{
    public string displayName = Ammo50CopperItemSystem.DisplayName;
    public string description = Ammo50CopperItemSystem.Description;
}

// ===== .45 ACP FMJ =====

/// <summary>
/// .45 ACP 全金属被甲弹。
/// AmmoScript: itemType=Round, ammoType=Pistol, maxRounds=1, rounds=1
/// </summary>
public static class Ammo45FMJItemSystem
{
    public const string ItemKey = "45fmj";
    public const string BaseGameItemId = "556round";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Pistol;

    public static string DisplayName => I18n.Tr("45fmj.name");
    public static string Description => I18n.Tr("45fmj.desc");

    private static Sprite? _cachedIcon;

    public static bool Is45FMJRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is45FMJRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[45FMJ] Configured AmmoScript: itemType=Round, ammoType=Pistol");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo45FMJMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo45FMJMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[45FMJ] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[45FMJ] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[45FMJ] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[45FMJ] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "45fmj.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "45fmj.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "45fmj-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[45FMJ] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo45FMJMarker : MonoBehaviour
{
    public string displayName = Ammo45FMJItemSystem.DisplayName;
    public string description = Ammo45FMJItemSystem.Description;
}

// ===== 9x19mm PSO gzh (Glock 17) =====

/// <summary>
/// 9x19 毫米 PSO gzh 手枪弹。
/// 适用于 GLOCK 17 手枪。
/// AmmoScript: itemType=Round, ammoType=Pistol, maxRounds=1, rounds=1
/// </summary>
public static class Ammo919PSOItemSystem
{
    public const string ItemKey = "919pso";
    public const string BaseGameItemId = "556round";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Pistol;

    public static string DisplayName => I18n.Tr("919pso.name");
    public static string Description => I18n.Tr("919pso.desc");

    private static Sprite? _cachedIcon;

    public static bool Is919PSORequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is919PSORequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[919PSO] Configured AmmoScript: itemType=Round, ammoType=Pistol");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo919PSOMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo919PSOMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[919PSO] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[919PSO] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[919PSO] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[919PSO] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "919pso.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "919pso.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "919pso-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[919PSO] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo919PSOMarker : MonoBehaviour
{
    public string displayName = Ammo919PSOItemSystem.DisplayName;
    public string description = Ammo919PSOItemSystem.Description;
}

// ===== 5.56x45mm FMJ (M4A1) =====

/// <summary>
/// 5.56x45 毫米 FMJ 步枪弹。
/// 适用于 M4A1 卡宾枪。
/// AmmoScript: itemType=Round, ammoType=Rifle, maxRounds=1, rounds=1
/// </summary>
public static class Ammo55645FMJItemSystem
{
    public const string ItemKey = "55645fmj";
    public const string BaseGameItemId = "556round";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Rifle;

    public static string DisplayName => I18n.Tr("55645fmj.name");
    public static string Description => I18n.Tr("55645fmj.desc");

    private static Sprite? _cachedIcon;

    public static bool Is55645FMJRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is55645FMJRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[55645FMJ] Configured AmmoScript: itemType=Round, ammoType=Rifle");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo55645FMJMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo55645FMJMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[55645FMJ] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[55645FMJ] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[55645FMJ] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[55645FMJ] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "55645fmj.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "55645fmj.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "55645fmj-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[55645FMJ] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo55645FMJMarker : MonoBehaviour
{
    public string displayName = Ammo55645FMJItemSystem.DisplayName;
    public string description = Ammo55645FMJItemSystem.Description;
}

// ===== 5.7x28mm SB193 (P90) =====

/// <summary>
/// 5.7x28 毫米 FN SB193 亚音速弹。
/// 适用于 FN P90 冲锋枪。
/// AmmoScript: itemType=Round, ammoType=Rifle, maxRounds=1, rounds=1
/// </summary>
public static class Ammo5728SB193ItemSystem
{
    public const string ItemKey = "5728sb193";
    public const string BaseGameItemId = "556round";
    public const GunScript.AmmoType AmmoTypeEnum = GunScript.AmmoType.Rifle;

    public static string DisplayName => I18n.Tr("5728sb193.name");
    public static string Description => I18n.Tr("5728sb193.desc");

    private static Sprite? _cachedIcon;

    public static bool Is5728SB193Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!Is5728SB193Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null)
        {
            ammo.itemType = AmmoScript.AmmoItemType.Round;
            ammo.ammoType = AmmoTypeEnum;
            ammo.maxRounds = 1;
            ammo.rounds = 1;
            Plugin.Log.LogInfo($"[5728SB193] Configured AmmoScript: itemType=Round, ammoType=Rifle");
        }

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.sprite = icon;
        }

        var marker = item.gameObject.GetComponent<Ammo5728SB193Marker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Ammo5728SB193Marker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[5728SB193] Configured spawned item '{ItemKey}'.");
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
                Plugin.Log.LogInfo($"[5728SB193] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[5728SB193] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[5728SB193] Failed to register '{ItemKey}': {ex}");
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
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
            weight = 0.01f,
            scaleWeightWithCondition = false,
            value = 1,
            tags = "bullet",
            rec = new Recognition(9),
        };
        info.SetTags();
        return info;
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "5728sb193.png");

            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "5728sb193.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "5728sb193-icon";
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[5728SB193] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }
}

public sealed class Ammo5728SB193Marker : MonoBehaviour
{
    public string displayName = Ammo5728SB193ItemSystem.DisplayName;
    public string description = Ammo5728SB193ItemSystem.Description;
}

// ===== 弹药悬停描述补丁 =====

/// <summary>
/// 弹药悬停描述统一补丁 — 所有自定义弹药物品共享此 HarmonyPatch。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class AmmoHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref (string, string) __result)
    {
        // 检查所有弹药标记
        var marker76251bpz = item.GetComponent<Ammo76251BPZMarker>();
        if (marker76251bpz != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker76251bpz.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }

        var marker76239sp = item.GetComponent<Ammo76239SPMarker>();
        if (marker76239sp != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker76239sp.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }

        var marker12g85 = item.GetComponent<Ammo12g85Marker>();
        if (marker12g85 != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker12g85.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }

        var marker338ucw = item.GetComponent<Ammo338UCWMarker>();
        if (marker338ucw != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker338ucw.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }

        var marker50copper = item.GetComponent<Ammo50CopperMarker>();
        if (marker50copper != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker50copper.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }

        var marker45fmj = item.GetComponent<Ammo45FMJMarker>();
        if (marker45fmj != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker45fmj.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }

        var marker919pso = item.GetComponent<Ammo919PSOMarker>();
        if (marker919pso != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker919pso.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }

        var marker55645fmj = item.GetComponent<Ammo55645FMJMarker>();
        if (marker55645fmj != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker55645fmj.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }

        var marker5728sb193 = item.GetComponent<Ammo5728SB193Marker>();
        if (marker5728sb193 != null)
        {
            if (!item.Stats.rec.recognizable) return;
            __result.Item1 = marker5728sb193.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
            return;
        }
    }
}
