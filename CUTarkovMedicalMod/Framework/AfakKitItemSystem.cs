using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// AFAK单兵战术急救包 — 贴肢使用（BandageMinigame）。
/// 效果：止血、表皮恢复、骨折恢复、脱臼恢复。
/// 模板：基于原版 bruisekit。
/// </summary>
public static class AfakKitItemSystem
{
    public const string ItemKey = "afak";
    public const string BaseGameItemId = "bruisekit";

    public static string DisplayName => I18n.Tr("afak.name");
    public static string Description => I18n.Tr("afak.desc");

    private const float PerformanceDivisor = 30f; // 30圈耗尽

    // 每圈（normalAngle=1.0）效果 = 常量 / PerformanceDivisor
    private const float BleedingStopFactor = 126f;      // 每圈止血 4.2
    private const float SkinHealFactor = 60f;           // 每圈表皮恢复 2
    private const float BoneHealReduction = 39f;        // 每圈骨折恢复 1.3
    private const float DislocationHealReduction = 39f; // 每圈脱臼恢复 1.3
    private const float DisinfectFactor = 150f;         // 每圈消毒 5s（累加）
    private const float DisinfectCap = 150f;            // 消毒上限 150s
    private const float OpiateFactor = 30f;             // 每圈阿片+1

    private static Sprite? _cachedIcon;

    public static bool IsAfakKitRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsAfakKitRequest(request)) return;

        EnsureRegisteredInItemTable();
        InjectMoodleIcon();

        item.id = ItemKey;
        item.SetCondition(1f);

        // 替换贴图（等比放大 1.5 倍，保持正方形比例）
        var icon = TryGetAfakKitIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var baseSpr = sr.sprite;
                var scaledTex = ScaleTextureUniform(icon.texture, 1f);
                AddOutline(scaledTex, new Color32(30, 30, 35, 180), 1);
                var scaledSprite = Sprite.Create(scaledTex,
                    new Rect(0, 0, scaledTex.width, scaledTex.height),
                    new Vector2(0.5f, 0.5f), baseSpr.pixelsPerUnit > 0f ? baseSpr.pixelsPerUnit : 32f);
                scaledSprite.name = "afak-icon";
                sr.sprite = scaledSprite;
            }
            else if (sr != null)
            {
                sr.sprite = icon;
            }
        }

        // 调整碰撞箱以匹配新贴图大小
        ResizeColliderToSprite(item);

        var marker = item.gameObject.GetComponent<AfakKitItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<AfakKitItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[AFAK] Configured spawned item '{ItemKey}' (id={item.id}, condition={item.condition}).");
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
                Plugin.Log.LogInfo($"[AFAK] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[AFAK] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[AFAK] Failed to register '{ItemKey}': {ex}");
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
            useLimbAction = AfakLimbAction,
            destroyAtZeroCondition = true,
            weight = 1.5f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 18,
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
            weight = 1.5f,
            scaleWeightWithCondition = false,
            useLimbAction = AfakLimbAction,
            value = 18,
            tags = "medicine,medical,combine,craft",
            rec = new Recognition(8),
        };
        info.SetTags();
        return info;
    }

    private static void AfakLimbAction(Limb limb, Item item)
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
            Plugin.Log.LogError($"[AFAK] Limb action failed: {ex}");
        }
    }

    // ===== ItemMarker =====

    public sealed class AfakKitItemMarker : MonoBehaviour
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
            var marker = item.GetComponent<AfakKitItemMarker>();
            if (marker == null) return;
            if (!item.Stats.rec.recognizable) return;
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
                    if (pixels[idx].a > 0) continue; // 只处理透明像素

                    bool neighbor = false;
                    for (int dy = -1; dy <= 1 && !neighbor; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                            if (copy[ny * w + nx].a > 0) { neighbor = true; break; }
                        }
                    }
                    if (neighbor)
                        pixels[idx] = outlineColor;
                }
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
    }

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "afak_icon.png");

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
                _cachedIcon.name = "afak-icon";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AFAK] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    public static Sprite? TryGetAfakKitIcon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetAfakKitIcon();
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
