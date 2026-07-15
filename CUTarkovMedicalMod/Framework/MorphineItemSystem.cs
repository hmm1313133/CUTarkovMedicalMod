using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 吗啡注射器系统。
/// 效果：300秒内强力止疼（压制所有颤栗/震屏），立即扣除饱食度与水分。
/// 定位：纯功能性药物，不治疗、不防出血，只消除疼痛副作用。
/// </summary>
public static class MorphineItemSystem
{
    // 使用独立键 "cu_morphine" 避免与游戏原生 "morphine"（LiquidItemInfo 药瓶，usable=false）冲突。
    // 原生 morphine 已存在于 GlobalItems，若键相同则 EnsureRegisteredInItemTable 会提前返回，
    // 导致自定义 useAction（Painkillers 注射）永不注册、item.id 指向原生药瓶（左键不可用）。
    public const string ItemKey = "cu_morphine";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("cu_morphine.name");
    public static string Description => I18n.Tr("cu_morphine.desc");

    private static Sprite? _cachedIcon;

    public static bool IsMorphineRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的吗啡物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsMorphineRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<MorphineItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<MorphineItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "morphine-icon";
                    sr.sprite = adjusted;
                }
                else
                {
                    sr.sprite = icon;
                }
            }
        }
    }

    /// <summary>
    /// 在 Item.GlobalItems 注册 morphine 的 ItemInfo。
    /// </summary>
    public static bool EnsureRegisteredInItemTable()
    {
        try
        {
            var globalItemsField = typeof(Item).GetField("GlobalItems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (globalItemsField == null) return false;

            var map = globalItemsField.GetValue(null) as IDictionary;
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
            clone.category = "ModStim";
            clone.weight = 0.1f;
            clone.value = 12;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,painkiller,narcotic,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(MorphineItemSystem).GetMethod(
                nameof(MorphineUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Morphine ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Morphine: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 吗啡使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// 通过原生 Painkillers 组件实现止疼：设置 opiateAmount 后，原生系统每帧
    /// 降低 limb.pain（actualOpiateReception * 0.3 * deltaTime），从而消除
    /// pain1-4 moodle 和 _Pain 屏幕着色器效果。
    /// </summary>
    private static void MorphineUseAction(Body body, Item item)
    {

        InjectorSound.Play();
        Plugin.Log.LogInfo("Morphine useAction invoked by game native system.");

        // 激活效果控制器（管理 buff 图标显示）
        MorphineEffectController.Attach(body).ActivateOrRefresh();

        // 立即扣除饱食度 10、水分 15（一次性副作用，塔科夫原版数值）
        body.Eat(-MorphineEffectController.HungerCostInstant, 0f);
        body.Drink(-MorphineEffectController.ThirstCostInstant);

        // 消耗物品
        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied Morphine: opiateAmount injected, native Painkillers system will handle pain suppression.");
    }

    #region Helper Methods

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "ModStim",
            usable = true,
            usableOnLimb = false,
            usableWithLMB = false,
            combineable = true,
            destroyAtZeroCondition = true,
            scaleWeightWithCondition = false,
            weight = 0.1f,
            value = 12,
            tags = "drug,medicine,medical,painkiller,narcotic,stim,combine,craft"
        };
        info.SetTags();

        var useMethod = typeof(MorphineItemSystem).GetMethod(
            nameof(MorphineUseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null)
        {
            info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                typeof(ItemInfo.Use), useMethod);
        }

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
            rec = new Recognition(9),
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

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var assetDir = Path.Combine(assemblyDir, "Framework", "Assets");
            var iconPath = Path.Combine(assetDir, "morphine.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "morphine.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;

            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "morphine-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Morphine icon: {ex.Message}");
            return null;
        }
    }

    private static Sprite? CreateSpriteMatchingBaseSize(Texture2D texture, Sprite? baseSprite)
    {
        if (texture == null) return null;
        if (baseSprite == null)
        {
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
        }

        var baseRect = baseSprite.rect;
        var basePpu = baseSprite.pixelsPerUnit > 0f ? baseSprite.pixelsPerUnit : 32f;
        var widthScale = baseRect.width > 0f ? texture.width / baseRect.width : 1f;
        var heightScale = baseRect.height > 0f ? texture.height / baseRect.height : 1f;
        var dominantScale = Mathf.Max(1f, Mathf.Max(widthScale, heightScale));
        return Sprite.Create(texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), basePpu * dominantScale);
    }

    #endregion
}

/// <summary>
/// 吗啡物品标记组件。
/// </summary>
public sealed class MorphineItemMarker : MonoBehaviour
{
    public string itemKey = MorphineItemSystem.ItemKey;
    public string displayName = MorphineItemSystem.DisplayName;
    public string description = MorphineItemSystem.Description;
}

/// <summary>
/// 吗啡效果控制器：
/// 通过原生 Painkillers 组件实现止疼 — 向 opiateAmount 注入剂量，
/// 原生系统每帧执行 limb.pain -= actualOpiateReception * 0.3 * deltaTime，
/// 从而消除 pain1-4 moodle 和 _Pain 屏幕着色器红化效果。
/// 
/// 数值设计（基于原生 Painkillers 代谢分析）：
/// - opiateAmount 注入 35，基础衰减率 0.059/s → 约 593s 自然代谢到 0
/// - 耐受度 opiateTolerance 以 0.08/s 追赶 → 约 437s 追平
/// - 考虑耐受追赶后实际止疼持续约 300s（与塔科夫原版一致）
/// - 原生系统自动处理 opiateHappiness（快感）、energy 消耗（副作用）
/// 
/// 饱食度/水分的一次性扣除在 MorphineUseAction 中完成。
/// </summary>
public sealed class MorphineEffectController : MonoBehaviour
{
    internal const float DurationSeconds = 300f;
    internal const float HungerCostInstant = 10f;   // 一次性饱食度扣除
    internal const float ThirstCostInstant = 15f;   // 一次性水分扣除
    private const float OpiateDose = 100f;           // 注入到 Painkillers.opiateAmount 的剂量

    private Body? _body;
    private float _remaining;

    public static MorphineEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<MorphineEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<MorphineEffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;
        if (isRefresh)
            StimBuffIndicator.ShowOneTimeEffect(MorphineItemSystem.ItemKey, I18n.Tr("cu_morphine.ot.0"));

        // 注入阿片剂量到原生 Painkillers 系统（可叠加）
        InjectOpiate(_body, OpiateDose);

        _remaining = 10f;
        enabled = true;
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null)
        {
            StimBuffIndicator.HideBuff(MorphineItemSystem.ItemKey);
            enabled = false;
            return;
        }

        _remaining -= Time.deltaTime;
        if (_remaining <= 0f)
        {
            StimBuffIndicator.HideBuff(MorphineItemSystem.ItemKey);
            enabled = false;
            return;
        }

        StimBuffIndicator.ShowBuff(
            MorphineItemSystem.ItemKey,
            I18n.Tr("cu_morphine.buff"),
            TryGetMorphineIcon(),
            _remaining,
            10f,
            new Color(0.31f, 0.76f, 0.97f),
            negativeDescs: I18n.TrAll("cu_morphine.neg.0"));
    }

    /// <summary>
    /// 向原生 Painkillers 组件注入阿片剂量。
    /// 如果已有 Painkillers 组件（之前用过止痛药），累加剂量；
    /// 否则添加 Painkillers 组件并设置初始剂量。
    /// 原生系统的 Update 会自动处理止疼、代谢、耐受度、opiateHappiness。
    /// </summary>
    private static void InjectOpiate(Body? body, float dose)
    {
        if (body == null) return;

        var pk = body.GetComponent<Painkillers>();
        if (pk == null)
        {
            pk = body.gameObject.AddComponent<Painkillers>();
            pk.opiateAmount = dose;
            Plugin.Log.LogInfo($"[Morphine] Created Painkillers component, opiateAmount={dose}");
        }
        else
        {
            pk.opiateAmount += dose;
            Plugin.Log.LogInfo($"[Morphine] Existing Painkillers found, opiateAmount += {dose} (now {pk.opiateAmount})");
        }
    }

    private static Sprite? TryGetMorphineIcon()
    {
        var method = typeof(MorphineItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改吗啡物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class MorphineHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<MorphineItemMarker>();
        if (marker == null) return;
        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
