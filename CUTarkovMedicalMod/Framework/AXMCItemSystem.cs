using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// Accuracy International AXMC .338 LM 栓动式狙击步枪。
/// 基于原版 rifle 预制体克隆，修改 GunScript 字段实现自定义数值。
/// </summary>
public static class AXMCItemSystem
{
    public const string ItemKey = "axmc";
    public const string BaseGameItemId = "rifle";

    public static string DisplayName => I18n.Tr("axmc.name");
    public static string Description => I18n.Tr("axmc.desc");

    // === GunScript 数值 ===
    private const int MagCapacity = 10;
    private const float KnockBack = 18f;
    private const float AnimalDamage = 310f;
    private const float StructureDamage = 299f;
    private const float Loudness = 6f;
    private const int ShotsPerFire = 1;
    private const float VerticalSpread = 0f;
    private const float ConditionLossPerShot = 0.5f;
    private const float DesiredGasTime = 0.2f;
    // FiringMode enum actual values: Pump=0, SemiAuto=1, Auto=2
    private const int FiringModeOverride = 0; // Pump (栓动)

    private static Sprite? _cachedIcon;
    private static Sprite? _cachedNoMagIcon;
    private static AudioClip? _cachedFireSound;

    public static bool IsAXMCRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置生成的物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsAXMCRequest(request)) return;

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
            if (FiringModeOverride >= 0)
                gun.firingMode = (GunScript.FiringMode)FiringModeOverride;

            var fireSound = TryLoadFireSound();
            if (fireSound != null)
                gun.fireSound = fireSound;

            // 设置拉栓/闭栓音效
            var rackSound = TryLoadSound("axmc_open", "ax");
            if (rackSound != null)
                gun.customRack = rackSound;
            var unrackSound = TryLoadSound("axmc_close", "ax");
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

            // 调整枪管和火光位置：向外偏移，使弹道起点和火光更靠外
            if (gun.barrel != null)
                gun.barrel.localPosition += new Vector3(2.5f, 0f, 0f);
            if (gun.muzzleParticle != null)
                gun.muzzleParticle.transform.localPosition += new Vector3(2.5f, 0f, 0f);

            Plugin.Log.LogInfo($"[AXMC] Configured GunScript: mag={MagCapacity}, dmg={AnimalDamage}, spread={VerticalSpread}");
        }

        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<AXMCItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<AXMCItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[AXMC] Configured spawned item '{ItemKey}' (condition={item.condition}).");
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
                Plugin.Log.LogInfo($"[AXMC] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[AXMC] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[AXMC] Failed to register '{ItemKey}': {ex}");
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
            weight = 5f,
            scaleWeightWithCondition = false,
            combineable = source.combineable,
            value = 79,
            tags = "cangetwet,gun",
            rec = new Recognition(11),
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
            weight = 5f,
            scaleWeightWithCondition = false,
            value = 79,
            tags = "cangetwet,gun",
            rec = new Recognition(11),
        };
        info.SetTags();
        return info;
    }

    // ===== Icon =====

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "axmc.png");

            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;

                _cachedIcon = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.30f, 0.5f), 13.2f);
                _cachedIcon.name = "axmc-icon";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AXMC] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    // ===== No-Mag Icon =====

    private static Sprite? TryLoadNoMagIcon()
    {
        if (_cachedNoMagIcon != null) return _cachedNoMagIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "axmc_magout.png");

            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;

                _cachedNoMagIcon = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.30f, 0.5f), 13.2f);
                _cachedNoMagIcon.name = "axmc-nomag-icon";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AXMC] Failed to load no-mag icon: {ex.Message}");
        }

        return _cachedNoMagIcon;
    }

    // ===== Sounds =====

    private static AudioClip? TryLoadFireSound()
    {
        if (_cachedFireSound != null) return _cachedFireSound;
        _cachedFireSound = TryLoadSound("axmc_fire", "ax");
        if (_cachedFireSound != null)
            Plugin.Log.LogInfo("[AXMC] Loaded fire sound 'axmc_fire.wav'");
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
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AXMC] Failed to load sound '{fileName}': {ex.Message}");
        }
        return null;
    }

    // ===== Collider =====

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

    // ===== WAV Loader =====

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

/// <summary>
/// 物品标记组件。
/// </summary>
public sealed class AXMCItemMarker : MonoBehaviour
{
    public string displayName = AXMCItemSystem.DisplayName;
    public string description = AXMCItemSystem.Description;
}

/// <summary>
/// 悬停描述补丁 - 智力不足时显示"Unknown Object"。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class AXMCHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref (string, string) __result)
    {
        var marker = item.GetComponent<AXMCItemMarker>();
        if (marker == null) return;

        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
