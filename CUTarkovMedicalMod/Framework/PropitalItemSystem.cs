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
/// Propital 再生兴奋剂注射器系统。
/// 效果：每秒所有肢体恢复 0.1 肌肉+表皮健康，持续 15 分钟；立即 +20 阿片剂量。
/// 副作用：立即患病 +10；3 分钟后韧性/力量永久 -2；10 分钟后管视+颤栗持续 5 分钟。
/// </summary>
public static class PropitalItemSystem
{
    public const string ItemKey = "propital";
    public const string BaseGameItemId = "syringe";

    public const string DisplayName = "Propital再生兴奋剂注射器【Propital】";
    public const string Description =
        "军用药物。通过增加嘌呤和嘧啶碱基、RNA、功能性酶促细胞元素的生物合成来刺激再生过程。但是它有长期的副作用，只允许专业医师和护理人员使用。写着'TerraGroup 实验室开发'。\n\n" +
        "<color=#54ff9f>效果：每秒恢复所有肢体 0.1 表皮与肌肉健康，持续15分钟；中幅阿片类药物影响。</color>\n" +
        "<color=#ff6666>副作用：患病 +10；延迟3分钟后韧性/力量等级永久 -2；延迟10分钟后出现严重管视效应与颤栗，持续5分钟。</color>";

    private static Sprite? _cachedIcon;

    public static bool IsPropitalRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的 Propital 物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsPropitalRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<PropitalItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<PropitalItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "propital-icon";
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
    /// 在 Item.GlobalItems 注册 Propital 的 ItemInfo。
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
            clone.value = 16;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(PropitalItemSystem).GetMethod(
                nameof(PropitalUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Propital ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Propital: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Propital 使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// 激活效果控制器，管理再生→延迟副作用的完整生命周期。
    /// </summary>
    private static void PropitalUseAction(Body body, Item item)
    {
        Plugin.Log.LogInfo("Propital useAction invoked by game native system.");

        PropitalEffectController.Attach(body).ActivateOrRefresh();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied Propital: regeneration + opiate effect activated.");
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
            value = 16,
            tags = "drug,medicine,medical,stim,combine,craft"
        };
        info.SetTags();

        var useMethod = typeof(PropitalItemSystem).GetMethod(
            nameof(PropitalUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "propital.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "propital.webp");
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
            _cachedIcon.name = "propital-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Propital icon: {ex.Message}");
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
/// Propital 物品标记组件。
/// </summary>
public sealed class PropitalItemMarker : MonoBehaviour
{
    public string itemKey = PropitalItemSystem.ItemKey;
    public string displayName = PropitalItemSystem.DisplayName;
    public string description = PropitalItemSystem.Description;
}

/// <summary>
/// Propital 效果控制器：
/// 增益期（900s / 15min）：每秒所有肢体 +0.1 肌肉 +0.1 表皮健康。
/// 使用瞬间：阿片剂量 +20、患病 +10。
/// 延迟 3min（180s）：韧性 -2、力量 -2（永久）。
/// 延迟 10min（600s）：管视效应 + 颤栗，持续 300s（5分钟）。
/// </summary>
public sealed class PropitalEffectController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Delay,       // 1s 生效延迟
        Buff,        // 900s 再生恢复 + 阿片
    }

    internal const float ActivationDelay = 1f;
    internal const float BuffDuration = 900f;           // 15 分钟
    internal const float HealPerSecond = 0.1f;          // 每秒每个肢体恢复健康
    internal const float OpiateDose = 20f;              // 一次性阿片剂量
    internal const float StatPenaltyDelay = 180f;        // 3 分钟后触发属性惩罚
    internal const float DebuffDelay = 600f;             // 10 分钟后触发管视+震颤
    internal const float DebuffDuration = 300f;          // 管视+震颤持续 5 分钟
    internal const int ResiliencePenalty = 2;            // 韧性等级永久 -2
    internal const int StrengthPenalty = 2;              // 力量等级永久 -2
    internal const float SicknessPenalty = 10f;          // 患病 +10
    internal const float StimulantTremorIntensity = 4f;  // 兴奋剂震颤强度（miscShakeIntensity，比之前更强）
    internal const float TunnelVisionIntensity = 0.0f;   // 管视最小值（完全漆黑）
    internal const float TunnelVisionMax = 0.25f;        // 管视最大值（仍严重受限），与最小值正弦波动

    private Body? _body;
    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _tickAccumulator;
    private float _elapsed;                    // 已生效时间（不含延迟）
    private bool _statPenaltyApplied;          // 属性惩罚是否已触发
    private bool _tunnelTremorActive;          // 管视+震颤是否激活中
    private float _tunnelTremorRemaining;      // 管视+震颤剩余时间

    public static PropitalEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<PropitalEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<PropitalEffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;
        if (isRefresh)
            StimBuffIndicator.ShowOneTimeEffect(PropitalItemSystem.ItemKey, "二次注射 计时器已刷新");

        // 注入阿片剂量（可叠加）
        InjectOpiate(_body, OpiateDose);
        StimBuffIndicator.ShowOneTimeEffect(PropitalItemSystem.ItemKey, "中幅阿片影响");

        // 立即副作用：患病 +10（每次注射都触发）
        _body!.sicknessAmount += SicknessPenalty;
        StimBuffIndicator.ShowOneTimeEffect(PropitalItemSystem.ItemKey, $"患病+{SicknessPenalty}", isNegative: true);
        Plugin.Log.LogInfo($"[Propital] Immediate sicknessAmount +{SicknessPenalty} (now {_body.sicknessAmount}).");

        _phase = Phase.Delay;
        _phaseTimer = ActivationDelay;
        _elapsed = 0f;
        _tickAccumulator = 0f;
        _statPenaltyApplied = false;
        _tunnelTremorActive = false;
        _tunnelTremorRemaining = 0f;
        enabled = true;

        StimBuffIndicator.ShowBuff(
            PropitalItemSystem.ItemKey,
            "Propital",
            TryGetPropitalIcon(),
            BuffDuration + ActivationDelay,
            BuffDuration + ActivationDelay,
            new Color(0.2f, 0.9f, 0.4f), // 翠绿色（再生）
            positiveDescs: new[] { "全部肢体再生" },
            negativeDescs: Array.Empty<string>());
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _phase == Phase.Idle)
        {
            StimBuffIndicator.HideBuff(PropitalItemSystem.ItemKey);
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
            PropitalItemSystem.ItemKey,
            "Propital",
            TryGetPropitalIcon(),
            _phaseTimer + BuffDuration,
            BuffDuration + ActivationDelay,
            new Color(0.2f, 0.9f, 0.4f));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Buff;
            _phaseTimer = BuffDuration;
            _elapsed = 0f;
            _tickAccumulator = 0f;
            Plugin.Log.LogInfo($"[Propital] Buff phase: +{HealPerSecond}/s all limbs, opiate +{OpiateDose}, for {BuffDuration}s");
        }
    }

    private void UpdateBuff()
    {
        var dt = Time.deltaTime;
        _phaseTimer -= dt;
        _elapsed += dt;

        // 每秒恢复所有肢体健康
        _tickAccumulator += dt;
        while (_tickAccumulator >= 1f)
        {
            _tickAccumulator -= 1f;
            HealAllLimbs();
        }

        // 延迟 3 分钟：属性惩罚（仅触发一次）
        if (!_statPenaltyApplied && _elapsed >= StatPenaltyDelay)
        {
            ApplyStatPenalty();
            _statPenaltyApplied = true;
        }

        // 延迟 10 分钟：管视 + 兴奋剂震颤（持续 5 分钟）
        if (!_tunnelTremorActive && _elapsed >= DebuffDelay)
        {
            _tunnelTremorActive = true;
            _tunnelTremorRemaining = DebuffDuration;
            ApplyTunnelVisionAndTremor();
            Plugin.Log.LogInfo($"[Propital] Tunnel vision + stimulant tremor applied for {DebuffDuration}s.");
        }

        // 管视+震颤期间持续施加
        if (_tunnelTremorActive && _tunnelTremorRemaining > 0f)
        {
            _tunnelTremorRemaining -= dt;
            MaintainTunnelVisionAndTremor();
        }
        else if (_tunnelTremorActive && _tunnelTremorRemaining <= 0f)
        {
            _tunnelTremorActive = false;
            SkillEffectHelper.ClearTunnelVision(_body);
            Plugin.Log.LogInfo("[Propital] Tunnel vision + tremor ended.");
        }

        // 更新 buff 显示
        var label = _tunnelTremorActive ? "Propital(副作用)" : "Propital";
        var color = _tunnelTremorActive
            ? new Color(1f, 0.3f, 0.3f)   // 红色（副作用警告）
            : new Color(0.2f, 0.9f, 0.4f); // 翠绿色（再生）

        StimBuffIndicator.ShowBuff(
            PropitalItemSystem.ItemKey,
            label,
            TryGetPropitalIcon(),
            _phaseTimer,
            BuffDuration,
            color,
            positiveDescs: _tunnelTremorActive ? null : new[] { "全部肢体再生" },
            negativeDescs: _tunnelTremorActive ? new[] { "管视效应" } : null);

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Idle;
            _tunnelTremorActive = false;
            _tunnelTremorRemaining = 0f;
            SkillEffectHelper.ClearTunnelVision(_body);
            StimBuffIndicator.HideBuff(PropitalItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[Propital] Effect ended. Regeneration stopped.");
        }
    }

    /// <summary>
    /// 每秒恢复所有非断肢部位的肌肉与外皮健康。
    /// </summary>
    private void HealAllLimbs()
    {
        if (_body == null || _body.limbs == null) return;

        foreach (var limb in _body.limbs)
        {
            if (limb == null || limb.dismembered) continue;
            limb.muscleHealth = Mathf.Min(100f, limb.muscleHealth + HealPerSecond);
            limb.skinHealth = Mathf.Min(100f, limb.skinHealth + HealPerSecond);
        }
    }

    /// <summary>
    /// 延迟 3 分钟后触发：韧性/力量等级永久 -2（直接修改 Skills.RES/STR 整数等级）。
    /// </summary>
    private void ApplyStatPenalty()
    {
        if (_body == null) return;

        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatRES, -ResiliencePenalty);
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatSTR, -StrengthPenalty);
        StimBuffIndicator.ShowOneTimeEffect(PropitalItemSystem.ItemKey, $"韧性-{ResiliencePenalty} 力量-{StrengthPenalty} 永久", isNegative: true);

        Plugin.Log.LogInfo($"[Propital] Stat penalty applied: RES -{ResiliencePenalty} (now {SkillEffectHelper.GetLevel(_body, SkillEffectHelper.StatRES)}), " +
            $"STR -{StrengthPenalty} (now {SkillEffectHelper.GetLevel(_body, SkillEffectHelper.StatSTR)}).");
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

    /// <summary>
    /// 向原生 Painkillers 组件注入阿片剂量。
    /// </summary>
    private static void InjectOpiate(Body? body, float dose)
    {
        if (body == null) return;

        var pk = body.GetComponent<Painkillers>();
        if (pk == null)
        {
            pk = body.gameObject.AddComponent<Painkillers>();
            pk.opiateAmount = dose;
            Plugin.Log.LogInfo($"[Propital] Created Painkillers component, opiateAmount={dose}");
        }
        else
        {
            pk.opiateAmount += dose;
            Plugin.Log.LogInfo($"[Propital] Existing Painkillers found, opiateAmount += {dose} (now {pk.opiateAmount})");
        }
    }

    private void OnDisable()
    {
        _tunnelTremorActive = false;
        _tunnelTremorRemaining = 0f;
        // 注意：不在此处 ClearTunnelVision，原因同 SJ6。
    }

    private static Sprite? TryGetPropitalIcon()
    {
        var method = typeof(PropitalItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改 Propital 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class PropitalHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<PropitalItemMarker>();
        if (marker == null) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
