using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// FN P90 5.7x28 冲锋枪。
/// 基于原版 rifle 预制体克隆，修改 GunScript 字段实现自定义数值。
/// 全自动射击模式，50发弹匣供弹。
/// </summary>
public static class P90ItemSystem
{
    public const string ItemKey = "p90";
    public const string BaseGameItemId = "rifle";

    public static string DisplayName => I18n.Tr("p90.name");
    public static string Description => I18n.Tr("p90.desc");

    // === GunScript 数值 ===
    private const int MagCapacity = 50;
    private const float KnockBack = 3.1f;
    private const float AnimalDamage = 45f;
    private const float StructureDamage = 35f;
    private const float Loudness = 1.9f;
    private const int ShotsPerFire = 1;
    private const float VerticalSpread = 0.12f;
    private const float ConditionLossPerShot = 0.08f;
    private const float DesiredGasTime = 0.085f;
    private const int FiringModeOverride = 2; // Auto

    private static Sprite? _cachedIcon;
    private static Sprite? _cachedNoMagIcon;
    private static AudioClip? _cachedFireSound;

    public static bool IsP90Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsP90Request(request)) return;

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
            gun.firingMode = (GunScript.FiringMode)FiringModeOverride;

            var fireSound = TryLoadFireSound();
            if (fireSound != null)
                gun.fireSound = fireSound;

            var rackSound = TryLoadSound("p90_open", "p90");
            if (rackSound != null)
                gun.customRack = rackSound;
            var unrackSound = TryLoadSound("p90_close", "p90");
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

            Plugin.Log.LogInfo($"[P90] Configured GunScript: mag={MagCapacity}, dmg={AnimalDamage}, spread={VerticalSpread}, mode=Auto");
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<P90ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<P90ItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[P90] Configured spawned item '{ItemKey}' (condition={item.condition}).");
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
                Plugin.Log.LogInfo($"[P90] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[P90] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[P90] Failed to register '{ItemKey}': {ex}");
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
            weight = 3.6f,
            scaleWeightWithCondition = false,
            combineable = source.combineable,
            value = 57,
            tags = "cangetwet,gun",
            rec = new Recognition(12),
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
            weight = 3.6f,
            scaleWeightWithCondition = false,
            value = 57,
            tags = "cangetwet,gun",
            rec = new Recognition(12),
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
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "p90.png");
            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                _cachedIcon = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 20f);
                _cachedIcon.name = "p90-icon";
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[P90] Failed to load icon: {ex.Message}"); }
        return _cachedIcon;
    }

    private static Sprite? TryLoadNoMagIcon()
    {
        if (_cachedNoMagIcon != null) return _cachedNoMagIcon;
        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "p90_magout.png");
            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                _cachedNoMagIcon = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 20f);
                _cachedNoMagIcon.name = "p90-nomag-icon";
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[P90] Failed to load no-mag icon: {ex.Message}"); }
        return _cachedNoMagIcon;
    }

    private static AudioClip? TryLoadFireSound()
    {
        if (_cachedFireSound != null) return _cachedFireSound;
        _cachedFireSound = TryLoadSound("p90_fire", "p90");
        if (_cachedFireSound != null)
            Plugin.Log.LogInfo("[P90] Loaded fire sound 'p90_fire.wav'");
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
        catch (Exception ex) { Plugin.Log.LogWarning($"[P90] Failed to load sound '{fileName}': {ex.Message}"); }
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

public sealed class P90ItemMarker : MonoBehaviour
{
    public string displayName = P90ItemSystem.DisplayName;
    public string description = P90ItemSystem.Description;
}

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class P90HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref (string, string) __result)
    {
        var marker = item.GetComponent<P90ItemMarker>();
        if (marker == null) return;
        if (!item.Stats.rec.recognizable) return;
        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
