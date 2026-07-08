using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// CMS 手术包系统。
/// 使用 useLimbAction 在肢体上自动检测并触发原版 minigame：
/// - 有弹片 → ShrapnelMinigame（镊子模式）
/// - 脱臼   → DislocationMinigame（扳手模式）
/// - 骨折   → 直接加速骨折恢复（无需 minigame）
/// 每次 minigame 结束后消耗耐久。
/// </summary>
public static class CmsKitItemSystem
{
    public const string ItemKey = "cms";
    public const string BaseGameItemId = "bruisekit";

    public static string DisplayName => I18n.Tr("cms.name");
    public static string Description => I18n.Tr("cms.desc");

    internal const float DislocationCost = 0.1f;   // 复位关节消耗耐久
    internal const float ShrapnelCost = 0.08f;     // 拔出弹片消耗耐久
    internal const float FractureCost = 0.2f;      // 加速骨折恢复消耗耐久
    internal const float FractureHealRatio = 0.5f; // 骨折恢复时间保留比例（减少50%）
    internal const float FracturePain = 50f;        // 骨折加速恢复造成的疼痛

    private static Sprite? _cachedIcon;

    // 追踪 minigame 前的肢体状态，用于判断是否实际完成了操作
    internal static Item? _pendingItem;
    internal static Limb? _pendingLimb;
    internal static int _pendingShrapnelBefore;
    internal static bool _pendingDislocatedBefore;

    public static bool IsCmsRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsCmsRequest(request)) return;

        EnsureRegisteredInItemTable();
        InjectMoodleIcon();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<CmsItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<CmsItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var baseSpr = sr.sprite;
                // 缩放至 0.8 倍，保持图标自身长宽比
                int targetW = Mathf.RoundToInt(icon.texture.width * 0.8f);
                int targetH = Mathf.RoundToInt(icon.texture.height * 0.8f);
                var scaledTex = StretchTextureToSize(icon.texture, targetW, targetH);
                AddOutline(scaledTex, new Color32(30, 30, 35, 180), 1);
                var scaledSprite = Sprite.Create(scaledTex,
                    new Rect(0, 0, scaledTex.width, scaledTex.height),
                    new Vector2(0.5f, 0.5f), baseSpr.pixelsPerUnit > 0f ? baseSpr.pixelsPerUnit : 32f);
                scaledSprite.name = "cms-icon";
                sr.sprite = scaledSprite;
            }
            else if (sr != null)
            {
                sr.sprite = icon;
            }
        }

        // 调整碰撞箱以匹配新贴图大小
        ResizeColliderToSprite(item);

        Plugin.Log.LogInfo($"[CMS] Configured spawned item '{ItemKey}'.");
    }

    public static bool EnsureRegisteredInItemTable()
    {
        try
        {
            var globalItemsField = typeof(Item).GetField("GlobalItems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (globalItemsField == null) return false;

            var map = globalItemsField.GetValue(null) as System.Collections.IDictionary;
            if (map == null) return false;

            if (map.Contains(ItemKey)) return true;

            ItemInfo? clone = null;
            if (map.Contains(BaseGameItemId))
            {
                var source = map[BaseGameItemId] as ItemInfo;
                clone = CloneItemInfo(source);
            }
            if (clone == null)
                clone = CreateFallbackItemInfo();

            clone.fullName = DisplayName;
            clone.description = Description;
            clone.category = "medical";
            clone.weight = 0.5f;
            clone.value = 12;
            clone.usable = false;
            clone.usableOnLimb = true;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "medicine,medical,surgery,combine,craft");
            clone.SetTags();

            clone.useAction = null;
            clone.useLimbAction = CmsLimbAction;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered CMS ItemInfo with custom useLimbAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register CMS: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 肢体使用 — 自动检测肢体状态，触发对应的原版 minigame。
    /// </summary>
    private static void CmsLimbAction(Limb limb, Item item)
    {
        try
        {
            if (limb.dismembered)
            {
                PlayerCamera.main?.DoAlert(I18n.Tr("cms.alert.dismembered"), true);
                return;
            }

            // 优先级：弹片 > 脱臼 > 骨折加速恢复
            Minigame? minigame = null;
            if (limb.shrapnel > 0)
            {
                Plugin.Log.LogInfo($"[CMS] Starting ShrapnelMinigame (tweezers=true) for limb {limb.name}.");
                minigame = new ShrapnelMinigame(limb, true);
            }
            else if (limb.dislocated)
            {
                Plugin.Log.LogInfo($"[CMS] Starting DislocationMinigame (wrench=true) for limb {limb.name}.");
                minigame = new DislocationMinigame(limb, true);
            }
            else if (limb.broken)
            {
                // 弹片和脱臼都已治疗完成（或没有），但有骨折 → 直接加速骨折恢复（无需 minigame）
                Plugin.Log.LogInfo($"[CMS] Accelerating fracture recovery for limb {limb.name} (boneHealTimer: {limb.boneHealTimer:F1} -> {limb.boneHealTimer * FractureHealRatio:F1}).");

                limb.boneHealTimer *= FractureHealRatio; // 减少 50% 恢复时间
                limb.pain += FracturePain;               // 造成 50 疼痛

                item.condition -= FractureCost;
                if (item.condition <= 0f)
                    item.SetCondition(0f);

                PlayerCamera.main?.DoAlert(I18n.Tr("cms.alert.fracture_done"), false);
                Plugin.Log.LogInfo($"[CMS] Fracture treatment done, condition -{FractureCost} (now {item.condition:F2}), pain +{FracturePain}.");
                return;
            }

            if (minigame == null)
            {
                PlayerCamera.main?.DoAlert(I18n.Tr("cms.alert.no_surgery"), true);
                return;
            }

            // 记录 minigame 前的肢体状态，用于 EndMinigame 时判断是否实际完成了操作
            _pendingItem = item;
            _pendingLimb = limb;
            _pendingShrapnelBefore = limb.shrapnel;
            _pendingDislocatedBefore = limb.dislocated;

            MinigameBase.main.StartMinigame(minigame, item);

            // 若 StartMinigame 因已有 minigame 运行而未启动，清除标记
            if (MinigameBase.main.currentMinigame == null)
            {
                _pendingItem = null;
                _pendingLimb = null;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CMS] Failed to start minigame: {ex.Message}");
        }
    }

    #region Helper Methods

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "medical",
            usable = false,
            usableOnLimb = true,
            usableWithLMB = false,
            combineable = true,
            destroyAtZeroCondition = true,
            scaleWeightWithCondition = false,
            weight = 0.5f,
            value = 12,
            tags = "medicine,medical,surgery,combine,craft",
            useLimbAction = CmsLimbAction
        };
        info.SetTags();
        return info;
    }

    private static ItemInfo CloneItemInfo(ItemInfo? source)
    {
        if (source == null) return CreateFallbackItemInfo();

        var clone = new ItemInfo
        {
            fullName = source.fullName,
            description = source.description,
            category = source.category,
            slotRotation = source.slotRotation,
            usable = source.usable,
            usableOnLimb = source.usableOnLimb,
            rotSpeed = source.rotSpeed,
            useAction = source.useAction,
            useLimbAction = source.useLimbAction,
            destroyAtZeroCondition = source.destroyAtZeroCondition,
            weight = source.weight,
            scaleWeightWithCondition = source.scaleWeightWithCondition,
            onlyHoldInHands = source.onlyHoldInHands,
            autoAttack = source.autoAttack,
            usableWithLMB = source.usableWithLMB,
            wearable = source.wearable,
            wearableCanBeHeld = source.wearableCanBeHeld,
            desiredWearLimb = source.desiredWearLimb,
            wearSlotId = source.wearSlotId,
            wearableArmor = source.wearableArmor,
            wearableIsolation = source.wearableIsolation,
            wearableHitDurabilityLossMultiplier = source.wearableHitDurabilityLossMultiplier,
            jumpHeightMultChange = source.jumpHeightMultChange,
            combineable = source.combineable,
            ignoreDepression = source.ignoreDepression,
            value = source.value,
            wearableVisualOffset = source.wearableVisualOffset,
            tags = source.tags,
            decayInfo = source.decayInfo,
            decayMinutes = source.decayMinutes,
            rec = new Recognition(6),
            qualities = source.qualities
        };
        clone.SetTags();
        return clone;
    }

    private static string MergeTags(string existing, string additions)
    {
        if (string.IsNullOrWhiteSpace(existing)) return additions;
        var merged = existing;
        foreach (var tag in additions.Split(','))
        {
            var t = tag.Trim();
            if (t.Length > 0 && merged.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0)
                merged += "," + t;
        }
        return merged;
    }

    #endregion

    #region Icon

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var assetDir = Path.Combine(assemblyDir, "Framework", "Assets");
            var iconPath = Path.Combine(assetDir, "cms.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "cms.webp");
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
            _cachedIcon.name = "cms-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load CMS icon: {ex.Message}");
            return null;
        }
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

    private static Texture2D StretchTextureToSize(Texture2D source, int targetW, int targetH)
    {
        var result = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
        result.filterMode = FilterMode.Point;
        result.wrapMode = TextureWrapMode.Clamp;

        var sourcePixels = source.GetPixels32();
        var newPixels = new Color32[targetW * targetH];

        for (int y = 0; y < targetH; y++)
        {
            for (int x = 0; x < targetW; x++)
            {
                int srcX = (x * source.width) / targetW;
                int srcY = (y * source.height) / targetH;
                newPixels[y * targetW + x] = sourcePixels[srcY * source.width + srcX];
            }
        }

        result.SetPixels32(newPixels);
        result.Apply();
        return result;
    }

    private static void AddOutline(Texture2D tex, Color32 outlineColor, int thickness)
    {
        int w = tex.width;
        int h = tex.height;
        var pixels = tex.GetPixels32();

        for (int t = 0; t < thickness; t++)
        {
            var copy = (Color32[])pixels.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (pixels[idx].a > 0) continue;

                    bool hasNeighbor = false;
                    if (x > 0 && copy[idx - 1].a > 0) hasNeighbor = true;
                    if (x < w - 1 && copy[idx + 1].a > 0) hasNeighbor = true;
                    if (y > 0 && copy[idx - w].a > 0) hasNeighbor = true;
                    if (y < h - 1 && copy[idx + w].a > 0) hasNeighbor = true;

                    if (hasNeighbor)
                        pixels[idx] = outlineColor;
                }
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
    }

    public static Sprite? TryGetCmsIcon() => TryLoadIcon();

    private static void InjectMoodleIcon()
    {
        var icon = TryGetCmsIcon();
        if (icon == null) return;

        try
        {
            var manager = MoodleManager.main;
            if (manager == null) return;

            var iconsField = typeof(MoodleManager).GetField("icons",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (iconsField == null) return;

            var icons = iconsField.GetValue(manager) as System.Collections.Generic.Dictionary<string, Sprite>;
            if (icons == null) return;

            if (!icons.ContainsKey(ItemKey))
                icons[ItemKey] = icon;
        }
        catch { }
    }

    #endregion
}

/// <summary>
/// CMS 手术包物品标记组件。
/// </summary>
public sealed class CmsItemMarker : MonoBehaviour
{
    public string itemKey = CmsKitItemSystem.ItemKey;
    public string displayName = CmsKitItemSystem.DisplayName;
    public string description = CmsKitItemSystem.Description;
}

/// <summary>
/// 修改 CMS 手术包物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class CmsHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<CmsItemMarker>();
        if (marker == null) return;
        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}

/// <summary>
/// 监听 MinigameBase.EndMinigame，仅在 minigame 实际完成操作时消耗耐久。
/// 如果玩家进入 minigame 后直接退出（未拔出弹片/未复位关节），不消耗耐久。
/// </summary>
[HarmonyPatch(typeof(MinigameBase), nameof(MinigameBase.EndMinigame))]
public static class CmsEndMinigamePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (CmsKitItemSystem._pendingItem == null || CmsKitItemSystem._pendingLimb == null)
            return;

        var item = CmsKitItemSystem._pendingItem;
        var limb = CmsKitItemSystem._pendingLimb;
        var shrapnelBefore = CmsKitItemSystem._pendingShrapnelBefore;
        var dislocatedBefore = CmsKitItemSystem._pendingDislocatedBefore;

        // 清除追踪
        CmsKitItemSystem._pendingItem = null;
        CmsKitItemSystem._pendingLimb = null;

        // 判断 minigame 是否实际完成了操作
        bool didSomething = false;
        if (limb.shrapnel < shrapnelBefore)
            didSomething = true; // 弹片被拔出
        if (dislocatedBefore && !limb.dislocated)
            didSomething = true; // 脱臼已复位

        if (didSomething)
        {
            // 根据操作类型消耗不同耐久
            float cost = 0f;
            if (dislocatedBefore && !limb.dislocated)
                cost = CmsKitItemSystem.DislocationCost;
            else if (limb.shrapnel < shrapnelBefore)
                cost = CmsKitItemSystem.ShrapnelCost;

            item.condition -= cost;
            if (item.condition <= 0f)
                item.SetCondition(0f);
            Plugin.Log.LogInfo($"[CMS] Surgery completed, condition -{cost} (now {item.condition:F2}).");
        }
        else
        {
            Plugin.Log.LogInfo("[CMS] Minigame exited without progress, no condition cost.");
        }
    }
}
