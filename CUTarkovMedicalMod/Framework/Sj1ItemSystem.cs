using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// SJ1 兴奋剂注射器系统。
/// 效果：临时力量等级 +5、韧性等级 +3（耐力恢复 +30%），持续 5 分钟；一次性阿片药物作用 +5。
/// 副作用：立即 +10 患病；每秒 -0.1 饱食/水分。
/// </summary>
public static class Sj1ItemSystem
{
    public const string ItemKey = "sj1";
    public const string BaseGameItemId = "syringe";

    public const string DisplayName = "SJ1兴奋剂注射器【SJ1】";
    public const string Description =
        "战斗兴奋剂。在战斗前注射能够获得力量和耐力。可以降低疼痛敏感度。兴奋剂被允许供特种作战单位使用。上面有SJ1的标记。有副作用。写着'TerraGroup 实验室开发'。\n\n" +
        "<color=#54ff9f>效果：力量等级+5、韧性等级+3、耐力恢复+30%，持续5分钟；轻微阿片类药物影响。</color>\n" +
        "<color=#ff6666>副作用：立即 +10患病；效果期间每秒消耗0.1饱食度与0.1水分。</color>";

    private static Sprite? _cachedIcon;

    public static bool IsSj1Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的 SJ1 物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsSj1Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<Sj1ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Sj1ItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "sj1-icon";
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
    /// 在 Item.GlobalItems 注册 SJ1 的 ItemInfo。
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
            clone.value = 14;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(Sj1ItemSystem).GetMethod(
                nameof(Sj1UseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered SJ1 ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register SJ1: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// SJ1 使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// </summary>
    private static void Sj1UseAction(Body body, Item item)
    {
        Plugin.Log.LogInfo("SJ1 useAction invoked by game native system.");

        Sj1EffectController.Attach(body).ActivateOrRefresh();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied SJ1: light combat stimulant + opioid effect activated.");
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
            value = 14,
            tags = "drug,medicine,medical,stim,combine,craft"
        };
        info.SetTags();

        var useMethod = typeof(Sj1ItemSystem).GetMethod(
            nameof(Sj1UseAction), BindingFlags.Static | BindingFlags.NonPublic);
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

    internal static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var assetDir = Path.Combine(assemblyDir, "Framework", "Assets");
            var iconPath = Path.Combine(assetDir, "sj1.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "sj1.webp");
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
            _cachedIcon.name = "sj1-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load SJ1 icon: {ex.Message}");
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
/// SJ1 物品标记组件。
/// </summary>
public sealed class Sj1ItemMarker : MonoBehaviour
{
    public string itemKey = Sj1ItemSystem.ItemKey;
    public string displayName = Sj1ItemSystem.DisplayName;
    public string description = Sj1ItemSystem.Description;
}

/// <summary>
/// SJ1 效果控制器：
/// 增益期（300s / 5min）：STR +5、RES +3（耐力恢复 +30%）；每秒 -0.1 饱食/水分。
/// 使用瞬间：患病 +10；阿片镇痛 dose +5。
/// </summary>
public sealed class Sj1EffectController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Delay,       // 1s 生效延迟
        Buff,        // 300s 属性增益
    }

    internal const float ActivationDelay = 1f;
    internal const float BuffDuration = 300f;                // 5 分钟
    internal const int StrengthLevelBoost = 5;               // 力量等级临时 +5
    internal const int ResilienceLevelBoost = 3;             // 韧性等级临时 +3（≈ 耐力恢复 +30%）
    internal const float SicknessOnUse = 10f;                // 立即患病 +10
    internal const float OpiateDose = 5f;                    // 一次性阿片药物作用
    internal const float FoodWaterDrainPerSec = 0.1f;        // 每秒消耗饱食/水分

    private Body? _body;
    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _drainAccumulator;           // 吃喝消耗累积器
    private bool _statsApplied;                // 属性增益是否已应用

    public static Sj1EffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<Sj1EffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<Sj1EffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;
        if (isRefresh)
            StimBuffIndicator.ShowOneTimeEffect(Sj1ItemSystem.ItemKey, "二次注射 计时器已刷新");

        // 立即副作用：患病 +10（每次注射都触发）
        _body!.sicknessAmount += SicknessOnUse;
        Plugin.Log.LogInfo($"[SJ1] Immediate sicknessAmount +{SicknessOnUse} (now {_body.sicknessAmount}).");
        StimBuffIndicator.ShowOneTimeEffect(Sj1ItemSystem.ItemKey, "患病+10", isNegative: true);

        // 阿片镇痛（可叠加）
        InjectOpiate(_body, OpiateDose);
        StimBuffIndicator.ShowOneTimeEffect(Sj1ItemSystem.ItemKey, "阿片镇痛+5");

        _phase = Phase.Delay;
        _phaseTimer = ActivationDelay;
        _drainAccumulator = 0f;
        if (!isRefresh)
            _statsApplied = false;
        enabled = true;

        StimBuffIndicator.ShowBuff(
            Sj1ItemSystem.ItemKey,
            "SJ1",
            TryGetSj1Icon(),
            BuffDuration + ActivationDelay,
            BuffDuration + ActivationDelay,
            new Color(0.3f, 0.7f, 0.9f), // 蓝青色（轻型战斗兴奋剂）
            positiveDescs: new[] { "力量+5", "韧性+3", "阿片镇痛+5" },
            negativeDescs: new[] { "每秒-0.1饱食/水分" });
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _phase == Phase.Idle)
        {
            StimBuffIndicator.HideBuff(Sj1ItemSystem.ItemKey);
            RestoreStats();
            enabled = false;
            return;
        }

        switch (_phase)
        {
            case Phase.Delay:
                UpdateDelay();
                break;
            case Phase.Buff:
                UpdateBuff();
                break;
        }
    }

    private void UpdateDelay()
    {
        _phaseTimer -= Time.deltaTime;

        StimBuffIndicator.ShowBuff(
            Sj1ItemSystem.ItemKey,
            "SJ1",
            TryGetSj1Icon(),
            _phaseTimer + BuffDuration,
            BuffDuration + ActivationDelay,
            new Color(0.3f, 0.7f, 0.9f));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Buff;
            _phaseTimer = BuffDuration;
            _drainAccumulator = 0f;

            // 应用属性增益
            ApplyStatBoosts();
            _statsApplied = true;

            Plugin.Log.LogInfo($"[SJ1] Buff phase: STR +{StrengthLevelBoost}, RES +{ResilienceLevelBoost} for {BuffDuration}s, "
                + $"food/water drain {FoodWaterDrainPerSec}/s");
        }
    }

    private void UpdateBuff()
    {
        var dt = Time.deltaTime;
        _phaseTimer -= dt;

        // 每秒消耗饱食/水分
        _drainAccumulator += dt;
        while (_drainAccumulator >= 1f)
        {
            _drainAccumulator -= 1f;
            DrainFoodWater();
        }

        // 更新 buff 显示
        StimBuffIndicator.ShowBuff(
            Sj1ItemSystem.ItemKey,
            "SJ1",
            TryGetSj1Icon(),
            _phaseTimer,
            BuffDuration,
            new Color(0.3f, 0.7f, 0.9f));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Idle;
            RestoreStats();
            StimBuffIndicator.HideBuff(Sj1ItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[SJ1] Effect ended. Stats restored.");
        }
    }

    /// <summary>
    /// 应用临时属性增益：STR +5、RES +3。
    /// </summary>
    private void ApplyStatBoosts()
    {
        if (_body == null) return;

        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatSTR, StrengthLevelBoost);
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatRES, ResilienceLevelBoost);

        Plugin.Log.LogInfo($"[SJ1] Stat boosts: STR +{StrengthLevelBoost} (now {SkillEffectHelper.GetLevel(_body, SkillEffectHelper.StatSTR)}), "
            + $"RES +{ResilienceLevelBoost} (now {SkillEffectHelper.GetLevel(_body, SkillEffectHelper.StatRES)}).");
    }

    /// <summary>
    /// 恢复属性增益。
    /// </summary>
    private void RestoreStats()
    {
        if (!_statsApplied || _body == null) return;

        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatSTR, -StrengthLevelBoost);
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatRES, -ResilienceLevelBoost);

        _statsApplied = false;
        Plugin.Log.LogInfo($"[SJ1] Stats restored: STR -{StrengthLevelBoost}, RES -{ResilienceLevelBoost}.");
    }

    /// <summary>
    /// 每秒扣除饱食度和水分。
    /// </summary>
    private void DrainFoodWater()
    {
        if (_body == null) return;

        try
        {
            _body.hunger = Mathf.Max(0f, _body.hunger - FoodWaterDrainPerSec);
            _body.thirst = Mathf.Max(0f, _body.thirst - FoodWaterDrainPerSec);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SJ1] DrainFoodWater failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 向原生 Painkillers 组件注入阿片剂量（一次性镇痛作用）。
    /// 复用于 MorphineItemSystem 的 Painkillers 机制：opiateAmount 决定止疼强度，
    /// 原生系统每帧自动代谢并降低 limb.pain。
    /// </summary>
    private static void InjectOpiate(Body? body, float dose)
    {
        if (body == null) return;

        var pk = body.GetComponent<Painkillers>();
        if (pk == null)
        {
            pk = body.gameObject.AddComponent<Painkillers>();
            pk.opiateAmount = dose;
            Plugin.Log.LogInfo($"[SJ1] Created Painkillers component, opiateAmount={dose}");
        }
        else
        {
            pk.opiateAmount += dose;
            Plugin.Log.LogInfo($"[SJ1] Existing Painkillers, opiateAmount += {dose} (now {pk.opiateAmount}).");
        }
    }

    private void OnDisable()
    {
        RestoreStats();
    }

    private static Sprite? TryGetSj1Icon()
    {
        var method = typeof(Sj1ItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改 SJ1 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class Sj1HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<Sj1ItemMarker>();
        if (marker == null) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
