using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// Salewa急救包 — 贴肢使用（BandageMinigame）。
/// 效果：止血、表皮恢复、骨折恢复、脱臼恢复。
/// 模板：基于原版 bruisekit。
/// </summary>
public static class SalewaKitItemSystem
{
    public const string ItemKey = "salewa";
    public const string BaseGameItemId = "bruisekit";

    public static string DisplayName => I18n.Tr("salewa.name");
    public static string Description => I18n.Tr("salewa.desc");

    private const float PerformanceDivisor = 40f; // 40圈耗尽

    // 每圈（normalAngle=1.0）效果 = 常量 / PerformanceDivisor
    private const float BleedingStopFactor = 150f;       // 每圈止血 3.75
    private const float SkinHealFactor = 80f;            // 每圈表皮恢复 2
    private const float BoneHealReduction = 52f;         // 每圈骨折恢复 1.3
    private const float DislocationHealReduction = 52f;  // 每圈脱臼恢复 1.3
    private const float DisinfectFactor = 120f;          // 每圈消毒 3s（累加）
    private const float DisinfectCap = 120f;             // 消毒上限 120s

    // 保温机制常量
    private const float ColdThreshold = 30f;       // 体温低于此值触发保温
    private const float TempRecoverPerWrap = 0.5f; // 每圈体温回升量
    private const float TempRecoverCap = 36f;      // 体温回升上限
    private static readonly Color KhakiColor = new Color(0.76f, 0.69f, 0.57f); // 卡其色
    private static readonly Color SilverGrayColor = new Color(0.75f, 0.75f, 0.78f); // 银灰色

    private static Sprite? _cachedIcon;

    public static bool IsSalewaKitRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsSalewaKitRequest(request)) return;

        EnsureRegisteredInItemTable();
        InjectMoodleIcon();

        item.id = ItemKey;
        item.SetCondition(1f);

        // 替换贴图
        var icon = TryGetSalewaKitIcon();
        if (icon != null)
        {
            var sr = item.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var baseSpr = sr.sprite;
                var baseRect = baseSpr.rect;
                var iconTex = icon.texture;
                // 以图标原始宽高比缩放，基于基础预制体的高度确定缩放大小
                float scale = baseRect.height * 0.625f / iconTex.height;
                int targetW = Mathf.RoundToInt(iconTex.width * scale);
                int targetH = Mathf.RoundToInt(iconTex.height * scale);
                var scaledTex = StretchTextureToSize(iconTex, targetW, targetH);
                var scaledSprite = Sprite.Create(scaledTex,
                    new Rect(0, 0, scaledTex.width, scaledTex.height),
                    new Vector2(0.5f, 0.5f), baseSpr.pixelsPerUnit > 0f ? baseSpr.pixelsPerUnit : 32f);
                scaledSprite.name = "salewa-icon";
                sr.sprite = scaledSprite;
            }
            else if (sr != null)
            {
                sr.sprite = icon;
            }
        }

        // 调整碰撞箱以匹配新贴图大小
        ResizeColliderToSprite(item);

        // 缩小物品体积至原来一半
        item.transform.localScale *= 0.5f;

        var marker = item.gameObject.GetComponent<SalewaKitItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<SalewaKitItemMarker>();
        marker.displayName = DisplayName;
        marker.description = Description;

        Plugin.Log.LogInfo($"[Salewa] Configured spawned item '{ItemKey}' (id={item.id}, condition={item.condition}, localScale={item.transform.localScale}).");
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
                Plugin.Log.LogInfo($"[Salewa] Registered '{ItemKey}' (cloned from '{BaseGameItemId}').");
                return true;
            }

            Item.GlobalItems[ItemKey] = CreateFallbackItemInfo();
            Plugin.Log.LogInfo($"[Salewa] Registered '{ItemKey}' (fallback).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Salewa] Failed to register '{ItemKey}': {ex}");
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
            useLimbAction = SalewaLimbAction,
            destroyAtZeroCondition = true,
            weight = 2f,
            scaleWeightWithCondition = false,
            combineable = true,
            value = 20,
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
            weight = 2f,
            scaleWeightWithCondition = false,
            useLimbAction = SalewaLimbAction,
            value = 20,
            tags = "medicine,medical,combine,craft",
            rec = new Recognition(8),
        };
        info.SetTags();
        return info;
    }

    private static void SalewaLimbAction(Limb limb, Item item)
    {
        try
        {
            // 检查保温机制条件：胸部 + 体温低于30°C
            var body = limb.body;
            bool isChest = limb.isVital && !limb.isHead;
            bool isCold = body != null && body.temperature < ColdThreshold;
            bool thermalMode = isChest && isCold;

            var bandageColor = thermalMode ? KhakiColor : SilverGrayColor;

            if (thermalMode)
                Plugin.Log.LogInfo($"[Salewa] Thermal mode active: chest detected, body temp={body!.temperature:F1}°C < {ColdThreshold}°C. Using khaki bandage.");

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

                    // 6) 保温：每圈体温回升0.1°C，上限36°C
                    if (thermalMode && body != null)
                    {
                        var recover = normalAngle * TempRecoverPerWrap;
                        var before = body.temperature;
                        body.temperature = Mathf.Min(TempRecoverCap, before + recover);
                        if (body.temperature < before) // 防止超上限回退
                            body.temperature = before;
                    }

                    // 7) 消毒（累加，上限封顶）
                    limb.disinfectionTime = Mathf.Min(DisinfectCap, limb.disinfectionTime + perf * DisinfectFactor);
                },
                bandageColor, limb),
                item);

            // 绷带外观（保温模式下为卡其色）
            limb.CreateTemporarySprite(
                Resources.Load<Sprite>("Special/bandageWrap"),
                0f,
                bandageColor,
                scaleLimb: true);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[Salewa] Limb action failed: {ex}");
        }
    }

    // ===== ItemMarker =====

    public sealed class SalewaKitItemMarker : MonoBehaviour
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
            var marker = item.GetComponent<SalewaKitItemMarker>();
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

    internal static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var iconPath = Path.Combine(assemblyDir, "Framework", "Assets", "salewa_icon.png");

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
                _cachedIcon.name = "salewa-icon";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Salewa] Failed to load icon: {ex.Message}");
        }

        return _cachedIcon;
    }

    public static Sprite? TryGetSalewaKitIcon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetSalewaKitIcon();
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
