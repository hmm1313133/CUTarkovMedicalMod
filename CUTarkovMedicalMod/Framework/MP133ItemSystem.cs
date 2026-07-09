using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// MP-133 12铅径泵动式霰弹枪。
/// 基于原版 shotgun 预制体克隆，修改 GunScript 字段实现自定义数值。
/// </summary>
public static class MP133ItemSystem
{
    public const string ItemKey = "mp133";
    public const string BaseGameItemId = "shotgun";

    public static string DisplayName => I18n.Tr("mp133.name");
    public static string Description => I18n.Tr("mp133.desc");

    // === GunScript 数值 ===
    private const int MagCapacity = 4;
    private const float KnockBack = 12f;
    private const float AnimalDamage = 40f;
    private const float StructureDamage = 30f;
    private const float Loudness = 4f;
    private const int ShotsPerFire = 8;
    private const float VerticalSpread = 0.18f;
    private const float ConditionLossPerShot = 0.6f;
    private const float DesiredGasTime = 0f;

    private static Sprite? _cachedIcon;
    private static AudioClip? _cachedFireSound;

    public static bool IsMP133Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置生成的 MP-133 物品实例。
    /// 克隆 shotgun 预制体后修改 GunScript 字段。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsMP133Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        // 修改 GunScript 字段
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

            // 设置自定义开火音效
            var fireSound = TryLoadFireSound();
            if (fireSound != null)
                gun.fireSound = fireSound;

            // 设置拉栓/闭栓音效
            var rackSound = TryLoadSound("mr133_open", "mp133");
            if (rackSound != null)
                gun.customRack = rackSound;
            var unrackSound = TryLoadSound("mr133_close", "mp133");
            if (unrackSound != null)
                gun.customUnrack = unrackSound;

            // 设置自定义贴图
            var icon = TryLoadIcon();
            if (icon != null)
            {
                gun.normalSprite = icon;
                gun.rackedSprite = icon;
                gun.normalSpriteNoMag = icon;
                gun.rackedSpriteNoMag = icon;

                var sr = item.GetComponent<SpriteRenderer>();
                if (sr != null)
                    sr.sprite = icon;
            }

            // 调整枪管和火光位置：向外偏移，使弹道起点和火光更靠外
            if (gun.barrel != null)
                gun.barrel.localPosition += new Vector3(2.5f, 0f, 0f);
            if (gun.muzzleParticle != null)
                gun.muzzleParticle.transform.localPosition += new Vector3(2.5f, 0f, 0f);

            Plugin.Log.LogInfo($"[MP133] Configured GunScript: mag={MagCapacity}, dmg={AnimalDamage}, spread={VerticalSpread}, pellets={ShotsPerFire}");
        }

        // 调整碰撞箱
        ResizeColliderToSprite(item);

        // 添加标记组件
        var marker = item.gameObject.GetComponent<MP133ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<MP133ItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[MP133] Configured spawned item '{ItemKey}' (condition={item.condition}).");
    }

    /// <summary>
    /// 在 Item.GlobalItems 注册 MP-133 的 ItemInfo。
    /// 克隆 shotgun 的 ItemInfo，修改名称、重量、价值等。
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
                Plugin.Log.LogInfo($"[MP133] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[MP133] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[MP133] Failed to register '{ItemKey}': {ex}");
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
            weight = 4.5f,
            scaleWeightWithCondition = false,
            combineable = source.combineable,
            value = 45,
            tags = "cangetwet,gun",
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
            category = "utility",
            slotRotation = -90f,
            usable = true,
            usableOnLimb = false,
            usableWithLMB = true,
            autoAttack = true,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 4.5f,
            scaleWeightWithCondition = false,
            value = 45,
            tags = "cangetwet,gun",
            rec = new Recognition(9),
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
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "mp133.png");

            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;

                _cachedIcon = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.30f, 0.5f), 22.5f);
                _cachedIcon.name = "mp133-icon";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[MP133] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    // ===== Sounds =====

    private static AudioClip? TryLoadFireSound()
    {
        if (_cachedFireSound != null) return _cachedFireSound;
        _cachedFireSound = TryLoadSound("mp133_fire", "mp133");
        if (_cachedFireSound != null)
            Plugin.Log.LogInfo("[MP133] Loaded fire sound 'mp133_fire.wav'");
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
            Plugin.Log.LogWarning($"[MP133] Failed to load sound '{fileName}': {ex.Message}");
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
/// MP-133 物品标记组件。
/// </summary>
public sealed class MP133ItemMarker : MonoBehaviour
{
    public string displayName = MP133ItemSystem.DisplayName;
    public string description = MP133ItemSystem.Description;
}

/// <summary>
/// MP-133 悬停描述补丁 - 智力不足时显示"Unknown Object"。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class MP133HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref (string, string) __result)
    {
        var marker = item.GetComponent<MP133ItemMarker>();
        if (marker == null) return;

        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
