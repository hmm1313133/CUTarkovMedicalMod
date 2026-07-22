using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// IFAK单兵战术急救包 — 贴肢使用（BandageMinigame）。
/// 效果：止血、表皮恢复、骨折恢复、脱臼恢复。
/// 模板：基于原版 bruisekit。
/// </summary>
public static class IfakKitItemSystem
{
    public const string ItemKey = "ifak";
    public const string BaseGameItemId = "bruisekit";

    public static string DisplayName => I18n.Tr("ifak.name");
    public static string Description => I18n.Tr("ifak.desc");

    private const float PerformanceDivisor = 20f; // 20圈耗尽

    // 每圈（normalAngle=1.0）效果 = 常量 / PerformanceDivisor
    private const float BleedingStopFactor = 80f;        // 每圈止血 4
    private const float SkinHealFactor = 40f;           // 每圈表皮恢复 2
    private const float BoneHealReduction = 20f;        // 每圈骨折恢复 1
    private const float DislocationHealReduction = 20f; // 每圈脱臼恢复 1
    private const float DisinfectFactor = 100f;         // 每圈消毒 5s（累加）
    private const float DisinfectCap = 100f;            // 消毒上限 100s
    private const float OpiateFactor = 20f;             // 每圈阿片+1

    private static Sprite? _cachedIcon;

    public static bool IsIfakKitRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsIfakKitRequest(request)) return;

        EnsureRegisteredInItemTable();
        InjectMoodleIcon();

        item.id = ItemKey;
        item.SetCondition(1f);

        // 替换贴图（等比放大 1.5 倍，保持正方形比例）
        var icon = TryGetIfakKitIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var baseSpr = sr.sprite;
                var scaledTex = ScaleTextureUniform(icon.texture, 1f);
                var scaledSprite = Sprite.Create(scaledTex,
                    new Rect(0, 0, scaledTex.width, scaledTex.height),
                    new Vector2(0.5f, 0.5f), baseSpr.pixelsPerUnit > 0f ? baseSpr.pixelsPerUnit : 32f);
                scaledSprite.name = "ifak-icon";
                sr.sprite = scaledSprite;
            }
            else if (sr != null)
            {
                sr.sprite = icon;
            }
        }

        // 调整碰撞箱以匹配新贴图大小
        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<IfakKitItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<IfakKitItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[IFAK] Configured spawned item '{ItemKey}' (id={item.id}, condition={item.condition}).");
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
                Plugin.Log.LogInfo($"[IFAK] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[IFAK] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[IFAK] Failed to register '{ItemKey}': {ex}");
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
            useLimbAction = IfakLimbAction,
            destroyAtZeroCondition = true,
            weight = 1.2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 14,
            tags = "medicine,medical,combine,craft",
            rec = new Recognition(8),
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
            weight = 1.2f,
            scaleWeightWithCondition = false,
            useLimbAction = IfakLimbAction,
            value = 14,
            tags = "medicine,medical,combine,craft",
            rec = new Recognition(8),
        };
        info.SetTags();
        return info;
    }

    private static void IfakLimbAction(Limb limb, Item item)
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

                    // 3) 脱臼加速恢复
                    limb.dislocationTimer = Mathf.Max(0f, limb.dislocationTimer - perf * DislocationHealReduction);

                    // 4) 表皮恢复
                    limb.skinHealAmount += perf * SkinHealFactor;

                    // 5) 止血
                    limb.bandageSlowAmount += perf * BleedingStopFactor;

                    // 6) 消毒（累加，上限封顶）
                    limb.disinfectionTime = Mathf.Min(DisinfectCap, limb.disinfectionTime + perf * DisinfectFactor);

                    // 7) 阿片影响
                    var body = limb.body;
                    if (body != null)
                    {
                        var pk = body.GetComponent<Painkillers>();
                        if (pk == null) pk = body.gameObject.AddComponent<Painkillers>();
                        pk.opiateAmount += perf * OpiateFactor;
                    }
                },
                new Color(0.75f, 0.75f, 0.78f), limb),
                item);

            // 银灰色绷带外观
            limb.CreateTemporarySprite(
                Resources.Load<Sprite>("Special/bandageWrap"),
                0f,
                new Color(0.75f, 0.75f, 0.78f),
                scaleLimb: true);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[IFAK] Limb action failed: {ex}");
        }
    }

    // ===== ItemMarker =====

    public sealed class IfakKitItemMarker : MonoBehaviour
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
            var marker = item.GetComponent<IfakKitItemMarker>();
            if (marker == null) return;
            if (item.Stats?.rec == null || !item.Stats.rec.recognizable) return;
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

    internal static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "ifak_icon.png");

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
                _cachedIcon.name = "ifak-icon";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[IFAK] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    public static Sprite? TryGetIfakKitIcon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetIfakKitIcon();
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
