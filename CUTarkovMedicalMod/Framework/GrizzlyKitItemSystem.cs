using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// Grizzly急救包 — 贴肢使用（BandageMinigame）。
/// 效果：加速骨折恢复、加速脱臼恢复、消毒、表皮恢复、肌肉恢复。
/// 模板：基于原版 bruisekit。
/// </summary>
public static class GrizzlyKitItemSystem
{
    public const string ItemKey = "grizzlykit";
    public const string BaseGameItemId = "bruisekit";

    public const string DisplayName = "Grizzly急救包";
    public const string Description =
        "旅行用Grizzly急救包被认为是最好的急救包之一。它包含了所有极端情况下所需要的一切医疗用品。" +
        "即使它看上去尘封许久，但里面的药品还很完好。\n\n" +
        "<color=#54ff9f>效果：大幅加速骨折恢复与止血；中幅表皮和肌肉健康度恢复；略微消毒。耐久极高但很重。</color>";

    private const float PerformanceDivisor = 100f;

    // 每圈（normalAngle=1.0）效果 = 常量 × 0.01
    private const float BoneHealReduction = 400f;   // 每圈骨折恢复 4
    private const float DislocationHealReduction = 300f; // 每圈脱臼恢复 3
    private const float SkinHealFactor = 330f;      // 每圈表皮恢复 3.3
    private const float MuscleHealFactor = 300f;    // 每圈肌肉恢复 3

    private static Sprite? _cachedIcon;

    public static bool IsGrizzlyKitRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsGrizzlyKitRequest(request)) return;

        EnsureRegisteredInItemTable();
        InjectMoodleIcon();

        item.id = ItemKey;
        item.SetCondition(1f);

        // 替换贴图（等比放大 2 倍）
        var icon = TryGetGrizzlyKitIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var baseSpr = sr.sprite;
                var scaledTex = ScaleTextureUniform(icon.texture, 0.3f);
                var scaledSprite = Sprite.Create(scaledTex,
                    new Rect(0, 0, scaledTex.width, scaledTex.height),
                    new Vector2(0.5f, 0.5f), baseSpr.pixelsPerUnit > 0f ? baseSpr.pixelsPerUnit : 32f);
                scaledSprite.name = "grizzlykit-icon";
                sr.sprite = scaledSprite;
            }
            else if (sr != null)
            {
                sr.sprite = icon;
            }
        }

        // 调整碰撞箱以匹配新贴图大小
        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<GrizzlyKitItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<GrizzlyKitItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[GrizzlyKit] Configured spawned item '{ItemKey}' (id={item.id}, condition={item.condition}).");
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
                Plugin.Log.LogInfo($"[GrizzlyKit] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[GrizzlyKit] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GrizzlyKit] Failed to register '{ItemKey}': {ex}");
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
            usable = source.usable,
            usableOnLimb = true,
            rotSpeed = source.rotSpeed,
            useAction = source.useAction,
            useLimbAction = GrizzlyLimbAction,
            destroyAtZeroCondition = true,
            weight = 3f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 32,
            tags = "medicine,medical,combine,craft",
            rec = new Recognition(3),
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
            category = "medical",
            slotRotation = 0f,
            usableOnLimb = true,
            destroyAtZeroCondition = true,
            combineable = true,
            weight = 3f,
            scaleWeightWithCondition = false,
            useLimbAction = GrizzlyLimbAction,
            value = 32,
            tags = "medicine,medical,combine,craft",
            rec = new Recognition(3),
        };
        info.SetTags();
        return info;
    }

    private static void GrizzlyLimbAction(Limb limb, Item item)
    {
        try
        {
            MinigameBase.main.StartMinigame(
                new BandageMinigame(delegate(float normalAngle)
                {
                    var perf = normalAngle / PerformanceDivisor;

                    // 1) 消耗耐久
                    item.condition -= perf;

                    // 2) 骨折加速恢复
                    limb.boneHealTimer = Mathf.Max(0f, limb.boneHealTimer - perf * BoneHealReduction);

                    // 2.5) 脱臼加速恢复
                    limb.dislocationTimer = Mathf.Max(0f, limb.dislocationTimer - perf * DislocationHealReduction);

                    // 3) 消毒（每圈 +10s，最高 120s）
                    limb.disinfectionTime = Mathf.Min(120f, limb.disinfectionTime + 10f);

                    // 4) 表皮恢复（直接加，立即生效）
                    limb.skinHealth = Mathf.Min(100f, limb.skinHealth + perf * SkinHealFactor);

                    // 5) 肌肉恢复
                    limb.muscleHealth += perf * MuscleHealFactor;

                    // 6) 止血（直接扣减出血量 + 持续 buff，比普通绷带止血更快）
                    limb.bleedAmount = Mathf.Max(0f, limb.bleedAmount - perf * 15f);
                    limb.bandageSlowAmount += perf * 490f;
                },
                new Color(1f, 0.84f, 0f), limb),
                item);

            // 金色绷带外观
            limb.CreateTemporarySprite(
                Resources.Load<Sprite>("Special/bandageWrap"),
                0f,
                new Color(1f, 0.84f, 0f),
                scaleLimb: true);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[GrizzlyKit] Limb action failed: {ex}");
        }
    }

    // ===== ItemMarker =====

    public sealed class GrizzlyKitItemMarker : MonoBehaviour
    {
        public string displayName = DisplayName;
        public string description = Description;
    }

    // ===== Hover patch =====

    [HarmonyPatch(typeof(PlayerCamera), "ItemHoverDescription")]
    public static class HoverPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Item item, ref (string, string) __result)
        {
            var marker = item.GetComponent<GrizzlyKitItemMarker>();
            if (marker == null) return;
            __result.Item1 = marker.displayName;
            HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
        }
    }

    // ===== Icon =====

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

    private static Texture2D ScaleTextureUniform(Texture2D source, float scale)
    {
        int targetW = Mathf.RoundToInt(source.width * scale);
        int targetH = Mathf.RoundToInt(source.height * scale);
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

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "grizzlykit_icon.png");

            if (File.Exists(iconPath))
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;

                _cachedIcon = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 32f);
                _cachedIcon.name = "grizzlykit-icon";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GrizzlyKit] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    public static Sprite? TryGetGrizzlyKitIcon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetGrizzlyKitIcon();
        if (icon == null) return;

        try
        {
            var manager = MoodleManager.main;
            if (manager == null) return;

            var iconsField = typeof(MoodleManager).GetField("icons",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (iconsField == null) return;

            var icons = iconsField.GetValue(manager) as Dictionary<string, Sprite>;
            if (icons == null) return;

            if (!icons.ContainsKey(ItemKey))
                icons[ItemKey] = icon;
        }
        catch { }
    }
}
