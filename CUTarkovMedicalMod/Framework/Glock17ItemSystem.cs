using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// GLOCK 17 9x19 手枪。
/// 基于原版 pistol 预制体克隆，修改 GunScript 字段实现自定义数值。
/// 半自动射击模式，17发弹匣供弹。
/// </summary>
public static class Glock17ItemSystem
{
    public const string ItemKey = "glock17";
    public const string BaseGameItemId = "pistol";

    public static string DisplayName => I18n.Tr("glock17.name");
    public static string Description => I18n.Tr("glock17.desc");

    // === GunScript 数值 ===
    private const int MagCapacity = 17;
    private const float KnockBack = 5f;
    private const float AnimalDamage = 50f;
    private const float StructureDamage = 20f;
    private const float Loudness = 2f;
    private const int ShotsPerFire = 1;
    private const float VerticalSpread = 0.2f;
    private const float ConditionLossPerShot = 0.5f;
    private const float DesiredGasTime = 0.2f;
    // FiringMode: pistol base is already SemiAuto(1), inherit from base

    private static Sprite? _cachedIcon;
    private static Sprite? _cachedNoMagIcon;
    private static AudioClip? _cachedFireSound;

    public static bool IsGlock17Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsGlock17Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var gun = item.GetComponent<GunScript>();
        if (gun != null)
        {
            gun.magCapacity = MagCapacity;
            gun.roundsInMag = 0;
            gun.knockBack = KnockBack;
            gun.animalDamage = AnimalDamage;
            gun.structureDamage = StructureDamage;
            gun.loudness = Loudness;
            gun.shotsPerFire = ShotsPerFire;
            gun.verticalSpread = VerticalSpread;
            gun.conditionLossPerShot = ConditionLossPerShot;
            gun.desiredGasTime = DesiredGasTime;

            var fireSound = TryLoadFireSound();
            if (fireSound != null)
                gun.fireSound = fireSound;

            var rackSound = TryLoadSound("glock_open", "glock");
            if (rackSound != null)
                gun.customRack = rackSound;
            var unrackSound = TryLoadSound("glock_close", "glock");
            if (unrackSound != null)
                gun.customUnrack = unrackSound;

            var icon = TryLoadIcon();
            var noMagIcon = TryLoadNoMagIcon();
            if (icon != null)
            {
                gun.normalSprite = icon;
                gun.rackedSprite = icon;
                gun.normalSpriteNoMag = noMagIcon ?? icon;
                gun.rackedSpriteNoMag = noMagIcon ?? icon;

                var sr = item.GetComponent<SpriteRenderer>();
                if (sr != null)
                    sr.sprite = icon;
            }

            Plugin.Log.LogInfo($"[Glock17] Configured GunScript: mag={MagCapacity}, dmg={AnimalDamage}, spread={VerticalSpread}, mode=SemiAuto");
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<Glock17ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Glock17ItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[Glock17] Configured spawned item '{ItemKey}' (condition={item.condition}).");
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
                Plugin.Log.LogInfo($"[Glock17] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[Glock17] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Glock17] Failed to register '{ItemKey}': {ex}");
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
            usableWithLMB = true,
            autoAttack = true,
            rotSpeed = source.rotSpeed,
            useAction = source.useAction,
            useLimbAction = null,
            destroyAtZeroCondition = true,
            weight = 1.5f,
            scaleWeightWithCondition = false,
            combineable = source.combineable,
            value = 18,
            tags = "cangetwet,gun",
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
            category = "utility",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            usableWithLMB = true,
            autoAttack = true,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 1.5f,
            scaleWeightWithCondition = false,
            value = 18,
            tags = "cangetwet,gun",
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
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "glock.png");
            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                _cachedIcon = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.30f, 0.5f), 22f);
                _cachedIcon.name = "glock17-icon";
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Glock17] Failed to load icon: {ex.Message}"); }
        return _cachedIcon;
    }

    private static Sprite? TryLoadNoMagIcon()
    {
        if (_cachedNoMagIcon != null) return _cachedNoMagIcon;
        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "glock_magout.png");
            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                _cachedNoMagIcon = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.30f, 0.5f), 22f);
                _cachedNoMagIcon.name = "glock17-nomag-icon";
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Glock17] Failed to load no-mag icon: {ex.Message}"); }
        return _cachedNoMagIcon;
    }

    private static AudioClip? TryLoadFireSound()
    {
        if (_cachedFireSound != null) return _cachedFireSound;
        _cachedFireSound = TryLoadSound("glock_fire", "glock");
        if (_cachedFireSound != null)
            Plugin.Log.LogInfo("[Glock17] Loaded fire sound 'glock_fire.wav'");
        return _cachedFireSound;
    }

    private static AudioClip? TryLoadSound(string fileName, string gunDir)
    {
        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var soundPath = Path.Combine(assemblyDir, "Framework", "Assets", "guns", gunDir, $"{fileName}.wav");
            if (File.Exists(soundPath))
                return LoadWavSync(soundPath);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[Glock17] Failed to load sound '{fileName}': {ex.Message}"); }
        return null;
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

public sealed class Glock17ItemMarker : MonoBehaviour
{
    public string displayName = Glock17ItemSystem.DisplayName;
    public string description = Glock17ItemSystem.Description;
}

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class Glock17HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref (string, string) __result)
    {
        var marker = item.GetComponent<Glock17ItemMarker>();
        if (marker == null) return;
        if (!item.Stats.rec.recognizable) return;
        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
