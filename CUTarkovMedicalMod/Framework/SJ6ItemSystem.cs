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
/// SJ6 TGLabs 战斗兴奋剂注射器系统。
/// 效果：耐力上限 +20%、耐力恢复 +120%（直接操作 Body.stamina），持续 15 分钟。
/// 副作用：立即 +25 患病；延迟 10 分钟管视效应 + 颤栗，持续 5 分钟。
/// </summary>
public static class SJ6ItemSystem
{
    public const string ItemKey = "sj6";
    public const string BaseGameItemId = "syringe";

    public const string DisplayName = "SJ6 TGLabs战斗兴奋剂注射器【SJ6】";
    public const string Description =
        "战斗兴奋剂。在战斗前注射能够提高身体能力。兴奋剂被允许供特种作战单位使用。上面有SJ6的标记，写着'TerraGroup 实验室开发'。\n\n" +
        "<color=#54ff9f>效果：耐力上限+20%、耐力恢复+120%（持续15分钟/900秒）。</color>\n" +
        "<color=#ff6666>副作用：立即 +25患病；10分钟后出现严重管视效应与颤栗，持续5分钟。</color>";

    private static Sprite? _cachedIcon;

    public static bool IsSJ6Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的 SJ6 物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsSJ6Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<SJ6ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<SJ6ItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "sj6-icon";
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
    /// 在 Item.GlobalItems 注册 SJ6 的 ItemInfo。
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
            clone.value = 17;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(SJ6ItemSystem).GetMethod(
                nameof(SJ6UseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered SJ6 ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register SJ6: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// SJ6 使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// 激活效果控制器，管理属性增益→延迟副作用的完整生命周期。
    /// </summary>
    private static void SJ6UseAction(Body body, Item item)
    {
        InjectorSound.Play();
        Plugin.Log.LogInfo("SJ6 useAction invoked by game native system.");

        SJ6EffectController.Attach(body).ActivateOrRefresh();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied SJ6: combat stimulant effect activated.");
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
            value = 17,
            tags = "drug,medicine,medical,stim,combine,craft"
        };
        info.SetTags();

        var useMethod = typeof(SJ6ItemSystem).GetMethod(
            nameof(SJ6UseAction), BindingFlags.Static | BindingFlags.NonPublic);
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

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var assetDir = Path.Combine(assemblyDir, "Framework", "Assets");
            var iconPath = Path.Combine(assetDir, "sj6.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "sj6.webp");
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
            _cachedIcon.name = "sj6-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load SJ6 icon: {ex.Message}");
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
/// SJ6 物品标记组件。
/// </summary>
public sealed class SJ6ItemMarker : MonoBehaviour
{
    public string itemKey = SJ6ItemSystem.ItemKey;
    public string displayName = SJ6ItemSystem.DisplayName;
    public string description = SJ6ItemSystem.Description;
}

/// <summary>
/// SJ6 效果控制器：
/// 增益期（900s / 15min）：耐力上限 +20%、耐力恢复 +120%（直接操作 Body.stamina）。
/// 使用瞬间：患病 +25。
/// 延迟 10min（600s）：管视效应 + 颤栗，持续 300s（5分钟）。
/// </summary>
public sealed class SJ6EffectController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Delay,       // 1s 生效延迟
        Buff,        // 900s 耐力增益
    }

    internal const float ActivationDelay = 1f;
    internal const float BuffDuration = 900f;               // 15 分钟
    internal const float StaminaCapBonus = 1.20f;           // 耐力上限 +20%
    internal const float StaminaRecoveryBoost = 1.20f;      // 耐力恢复 +120%（额外恢复比例）
    internal const float SicknessOnUse = 25f;                // 立即患病 +25
    internal const float DebuffDelay = 600f;                 // 10 分钟后触发管视+震颤
    internal const float DebuffDuration = 300f;              // 管视+震颤持续 5 分钟
    internal const float StimulantTremorIntensity = 4f;      // 兴奋剂震颤强度（miscShakeIntensity）
    internal const float TunnelVisionIntensity = 0.0f;     // 管视最小值（完全漆黑）
    internal const float TunnelVisionMax = 0.25f;          // 管视最大值（仍严重受限），与最小值正弦波动

    private Body? _body;
    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _elapsed;                    // 已生效时间（不含延迟）
    private float _staminaCapBaseline;         // 追踪峰值耐力，用于计算 +20% 上限
    private bool _tunnelTremorActive;          // 管视+震颤是否激活中
    private float _tunnelTremorRemaining;      // 管视+震颤剩余时间

    public static SJ6EffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<SJ6EffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<SJ6EffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;

        if (isRefresh)
        {
            StimBuffIndicator.ShowOneTimeEffect(SJ6ItemSystem.ItemKey, "二次注射 正面效果不叠加");
            Plugin.Log.LogInfo("[SJ6] Refresh: timer reset, negatives re-trigger.");
        }

        // 立即副作用：患病 +25（每次注射都触发）
        _body!.sicknessAmount += SicknessOnUse;
        Plugin.Log.LogInfo($"[SJ6] Immediate sicknessAmount +{SicknessOnUse} (now {_body.sicknessAmount}).");
        StimBuffIndicator.ShowOneTimeEffect(SJ6ItemSystem.ItemKey, "患病+25", isNegative: true);

        _phase = Phase.Delay;
        _phaseTimer = ActivationDelay;
        _elapsed = 0f;
        _tunnelTremorActive = false;
        _tunnelTremorRemaining = 0f;
        enabled = true;

        StimBuffIndicator.ShowBuff(
            SJ6ItemSystem.ItemKey,
            "SJ6",
            TryGetSJ6Icon(),
            BuffDuration + ActivationDelay,
            BuffDuration + ActivationDelay,
            new Color(0.9f, 0.55f, 0.1f), // 橙色（战斗兴奋剂）
            positiveDescs: new[] { "耐力上限+20%", "耐力恢复+120%" },
            negativeDescs: Array.Empty<string>());
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _phase == Phase.Idle)
        {
            StimBuffIndicator.HideBuff(SJ6ItemSystem.ItemKey);
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
            SJ6ItemSystem.ItemKey,
            "SJ6",
            TryGetSJ6Icon(),
            _phaseTimer + BuffDuration,
            BuffDuration + ActivationDelay,
            new Color(0.9f, 0.55f, 0.1f),
            positiveDescs: new[] { "耐力上限+20%", "耐力恢复+120%" },
            negativeDescs: Array.Empty<string>());

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Buff;
            _phaseTimer = BuffDuration;
            _elapsed = 0f;

            // 以当前耐力值为基准
            _staminaCapBaseline = _body!.stamina;

            // 注册耐力恢复和耐力上限加成到多来源叠加管理器
            StaminaBonusManager.AddBonus(_body, StaminaRecoveryBoost, BuffDuration, SJ6ItemSystem.ItemKey);
            StaminaCapBonusManager.AddBonus(_body, StaminaCapBonus - 1f, BuffDuration, SJ6ItemSystem.ItemKey);

            Plugin.Log.LogInfo($"[SJ6] Buff phase: stamina cap +{(StaminaCapBonus-1f)*100}%, recovery +{StaminaRecoveryBoost*100}% for {BuffDuration}s");
        }
    }

    private void UpdateBuff()
    {
        var dt = Time.deltaTime;
        _phaseTimer -= dt;
        _elapsed += dt;

        // ===== 耐力操纵：直接操作 Body.stamina =====
        // 仅当本物品是当前最强来源时才执行，避免多来源重复追加

        // 1. 耐力上限 +20%
        if (StaminaCapBonusManager.IsTopSource(_body!, SJ6ItemSystem.ItemKey))
        {
            try
            {
                if (_body!.stamina > _staminaCapBaseline)
                    _staminaCapBaseline = _body.stamina;

                var effectiveCap = _staminaCapBaseline * StaminaCapBonus;
                if (_body.stamina > effectiveCap)
                    _body.stamina = effectiveCap;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SJ6] Stamina cap clamp failed: {ex.Message}");
            }
        }

        // 2. 耐力恢复 +120%
        if (StaminaBonusManager.IsTopSource(_body!, SJ6ItemSystem.ItemKey))
        {
            try
            {
                if (_body!.staminaStrength != null)
                {
                    var extraRecovery = _body.staminaStrength.Evaluate(_body.energy * 0.01f) * dt * StaminaRecoveryBoost;
                    _body.stamina += extraRecovery;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SJ6] Stamina recovery boost failed: {ex.Message}");
            }
        }

        // 延迟 10 分钟：管视 + 兴奋剂震颤（持续 5 分钟）
        if (!_tunnelTremorActive && _elapsed >= DebuffDelay)
        {
            _tunnelTremorActive = true;
            _tunnelTremorRemaining = DebuffDuration;
            ApplyTunnelVisionAndTremor();
            Plugin.Log.LogInfo($"[SJ6] Tunnel vision + stimulant tremor applied for {DebuffDuration}s.");
        }

        // 管视+震颤期间持续维持
        if (_tunnelTremorActive && _tunnelTremorRemaining > 0f)
        {
            _tunnelTremorRemaining -= dt;
            MaintainTunnelVisionAndTremor();
        }
        else if (_tunnelTremorActive && _tunnelTremorRemaining <= 0f)
        {
            _tunnelTremorActive = false;
            SkillEffectHelper.ClearTunnelVision(_body);
            Plugin.Log.LogInfo("[SJ6] Tunnel vision + tremor ended.");
        }

        // 更新 buff 显示
        var label = _tunnelTremorActive ? "SJ6(副作用)" : "SJ6";
        var color = _tunnelTremorActive
            ? new Color(1f, 0.3f, 0.3f)   // 红色（副作用警告）
            : new Color(0.9f, 0.55f, 0.1f); // 橙色（战斗兴奋剂）

        StimBuffIndicator.ShowBuff(
            SJ6ItemSystem.ItemKey,
            label,
            TryGetSJ6Icon(),
            _phaseTimer,
            BuffDuration,
            color,
            positiveDescs: _tunnelTremorActive ? null : new[] { "耐力上限+20%", "耐力恢复+120%" },
            negativeDescs: _tunnelTremorActive ? new[] { "管视效应", "震颤" } : Array.Empty<string>());

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Idle;
            _tunnelTremorActive = false;
            _tunnelTremorRemaining = 0f;
            SkillEffectHelper.ClearTunnelVision(_body);
            if (_body != null)
            {
                StaminaBonusManager.ClearBonus(_body, SJ6ItemSystem.ItemKey);
                StaminaCapBonusManager.ClearBonus(_body, SJ6ItemSystem.ItemKey);
            }
            StimBuffIndicator.HideBuff(SJ6ItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[SJ6] Effect ended.");
        }
    }

    /// <summary>
    /// 延迟 10 分钟后触发：管视效应 + 兴奋剂震颤。
    /// 震颤使用原生 miscShakeIntensity 字段（与 Liquids.HighGradeStimulantStep 相同机制），
    /// 在 HandleVisuals 中以 Clamp01(miscShakeIntensity)*0.05 叠加到身体视觉抖动。
    /// 管视通过全屏黑色径向遮罩（暗角效果）实现，遮罩在中心狭小区域透明、边缘快速变黑，
    /// 由 TunnelVisionOverlay 每帧更新 alpha，在 0.00~0.75 间正弦波动（极致管视效应）。
    /// </summary>
    private void ApplyTunnelVisionAndTremor()
    {
        if (_body == null) return;

        // 兴奋剂震颤：注入 miscShakeIntensity（原生兴奋剂震颤字段）
        SkillEffectHelper.AddStimulantTremor(_body, StimulantTremorIntensity);

        // 管视：全屏黑色径向遮罩，透明中心→黑色边缘，在 0.15~0.9 间正弦波动
        SkillEffectHelper.SetTunnelVision(_body, TunnelVisionIntensity, TunnelVisionMax);
    }

    /// <summary>
    /// 管视+震颤期间每帧维持效果（防止自然衰减过快消除）。
    /// </summary>
    private void MaintainTunnelVisionAndTremor()
    {
        if (_body == null) return;

        // 维持兴奋剂震颤强度
        SkillEffectHelper.MaintainStimulantTremor(_body, StimulantTremorIntensity);

        // 管视由 TunnelVisionOverlay.Update 每帧自动维持（SetTunnelVision 设置后持续生效）
    }

    private void OnDisable()
    {
        _tunnelTremorActive = false;
        _tunnelTremorRemaining = 0f;
        // 注意：不在此处 ClearTunnelVision，原因同 Propital。
    }

    private static Sprite? TryGetSJ6Icon()
    {
        var method = typeof(SJ6ItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改 SJ6 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class SJ6HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<SJ6ItemMarker>();
        if (marker == null) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
