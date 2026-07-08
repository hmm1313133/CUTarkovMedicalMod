using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 多功能手术工具包系统。
/// 使用 useLimbAction 在肢体上自动检测并触发原版 minigame：
/// - 有弹片 → ShrapnelMinigame（镊子模式）
/// - 脱臼   → DislocationMinigame（扳手模式）
/// - 骨折   → AmputationMinigame（截肢）
/// 每次 minigame 结束后消耗耐久。
/// </summary>
public static class MultiToolItemSystem
{
    public const string ItemKey = "multitool";
    public const string BaseGameItemId = "bruisekit";

    public const string DisplayName = "Surv12野战手术包【Surv12】";
    public const string Description =
        "带有更多高质量器械的高级外科手术包，让使用者在野战条件下也能治疗严重伤害。\n" +
        "在肢体上使用时会自动判断需要的手术类型，耐久很高但略重。\n\n" +
        "<color=#54ff9f>效果：拔出弹片、复位脱臼、加速90%骨折恢复（造成中小幅疼痛），自动检测（治疗骨折需要先治疗弹片和脱臼）。</color>\n" ;

    internal const float DislocationCost = 0.05f; // 复位关节消耗耐久
    internal const float ShrapnelCost = 0.02f;    // 拔出弹片消耗耐久
    internal const float FractureCost = 0.08f;    // 加速骨折恢复消耗耐久
    internal const float FractureHealRatio = 0.1f; // 骨折恢复时间保留比例（减少90%）
    internal const float FracturePain = 30f;       // 骨折加速恢复造成的疼痛

    private static Sprite? _cachedIcon;

    // 追踪 minigame 前的肢体状态，用于判断是否实际完成了操作
    internal static Item? _pendingItem;
    internal static Limb? _pendingLimb;
    internal static int _pendingShrapnelBefore;
    internal static bool _pendingDislocatedBefore;

    public static bool IsMultiToolRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsMultiToolRequest(request)) return;

        EnsureRegisteredInItemTable();
        InjectMoodleIcon();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<MultiToolItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<MultiToolItemMarker>();

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
                scaledSprite.name = "multitool-icon";
                sr.sprite = scaledSprite;
            }
            else if (sr != null)
            {
                sr.sprite = icon;
            }
        }

        // 调整碰撞箱以匹配新贴图大小
        ResizeColliderToSprite(item);

        Plugin.Log.LogInfo($"[MultiTool] Configured spawned item '{ItemKey}'.");
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
            clone.weight = 1.5f;
            clone.value = 20;
            clone.usable = false;
            clone.usableOnLimb = true;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "medicine,medical,surgery,combine,craft");
            clone.SetTags();

            clone.useAction = null;
            clone.useLimbAction = MultiToolLimbAction;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered MultiTool ItemInfo with custom useLimbAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register MultiTool: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 肢体使用 — 自动检测肢体状态，触发对应的原版 minigame。
    /// </summary>
    private static void MultiToolLimbAction(Limb limb, Item item)
    {
        try
        {
            if (limb.dismembered)
            {
                PlayerCamera.main?.DoAlert("该肢体已截肢", true);
                return;
            }

            // 优先级：弹片 > 脱臼 > 骨折加速恢复
            Minigame? minigame = null;
            if (limb.shrapnel > 0)
            {
                Plugin.Log.LogInfo($"[MultiTool] Starting ShrapnelMinigame (tweezers=true) for limb {limb.name}.");
                minigame = new ShrapnelMinigame(limb, true);
            }
            else if (limb.dislocated)
            {
                Plugin.Log.LogInfo($"[MultiTool] Starting DislocationMinigame (wrench=true) for limb {limb.name}.");
                minigame = new DislocationMinigame(limb, true);
            }
            else if (limb.broken)
            {
                // 弹片和脱臼都已治疗完成（或没有），但有骨折 → 直接加速骨折恢复（无需 minigame）
                Plugin.Log.LogInfo($"[MultiTool] Accelerating fracture recovery for limb {limb.name} (boneHealTimer: {limb.boneHealTimer:F1} -> {limb.boneHealTimer * FractureHealRatio:F1}).");

                limb.boneHealTimer *= FractureHealRatio; // 减少 90% 恢复时间
                limb.pain += FracturePain;               // 造成 30 疼痛

                item.condition -= FractureCost;
                if (item.condition <= 0f)
                    item.SetCondition(0f);

                PlayerCamera.main?.DoAlert("骨折恢复加速完成", false);
                Plugin.Log.LogInfo($"[MultiTool] Fracture treatment done, condition -{FractureCost} (now {item.condition:F2}), pain +{FracturePain}.");
                return;
            }

            if (minigame == null)
            {
                PlayerCamera.main?.DoAlert("该部位无需手术处理", true);
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
            Plugin.Log.LogWarning($"[MultiTool] Failed to start minigame: {ex.Message}");
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
            weight = 1.5f,
            value = 20,
            tags = "medicine,medical,surgery,combine,craft",
            useLimbAction = MultiToolLimbAction
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
            rec = source.rec,
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
            var iconPath = Path.Combine(assetDir, "multitool.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "multitool.webp");
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
            _cachedIcon.name = "multitool-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load MultiTool icon: {ex.Message}");
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

    public static Sprite? TryGetMultiToolIcon() => TryLoadIcon();

    private static void InjectMoodleIcon()
    {
        var icon = TryGetMultiToolIcon();
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
/// 多功能手术工具包物品标记组件。
/// </summary>
public sealed class MultiToolItemMarker : MonoBehaviour
{
    public string itemKey = MultiToolItemSystem.ItemKey;
    public string displayName = MultiToolItemSystem.DisplayName;
    public string description = MultiToolItemSystem.Description;
}

/// <summary>
/// 修改多功能手术工具包物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class MultiToolHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<MultiToolItemMarker>();
        if (marker == null) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}

/// <summary>
/// 监听 MinigameBase.EndMinigame，仅在 minigame 实际完成操作时消耗耐久。
/// 如果玩家进入 minigame 后直接退出（未拔出弹片/未复位关节），不消耗耐久。
/// </summary>
[HarmonyPatch(typeof(MinigameBase), nameof(MinigameBase.EndMinigame))]
public static class MultiToolEndMinigamePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (MultiToolItemSystem._pendingItem == null || MultiToolItemSystem._pendingLimb == null)
            return;

        var item = MultiToolItemSystem._pendingItem;
        var limb = MultiToolItemSystem._pendingLimb;
        var shrapnelBefore = MultiToolItemSystem._pendingShrapnelBefore;
        var dislocatedBefore = MultiToolItemSystem._pendingDislocatedBefore;

        // 清除追踪
        MultiToolItemSystem._pendingItem = null;
        MultiToolItemSystem._pendingLimb = null;

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
                cost = MultiToolItemSystem.DislocationCost;
            else if (limb.shrapnel < shrapnelBefore)
                cost = MultiToolItemSystem.ShrapnelCost;

            item.condition -= cost;
            if (item.condition <= 0f)
                item.SetCondition(0f);
            Plugin.Log.LogInfo($"[MultiTool] Surgery completed, condition -{cost} (now {item.condition:F2}).");
        }
        else
        {
            Plugin.Log.LogInfo("[MultiTool] Minigame exited without progress, no condition cost.");
        }
    }
}

