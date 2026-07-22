using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// PNB 兴奋剂注射器系统。
/// 效果：指甲恢复；每秒所有肢体 +0.2 肌肉健康，持续 2 分钟；耐力恢复 +30%（RES +3），持续 5 分钟。
/// 副作用：延迟 5 分钟后永久力量等级 -1；兴奋剂震颤持续 60 秒。
/// </summary>
public static class PnbItemSystem
{
    public const string ItemKey = "pnb";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("pnb.name");
    public static string Description => I18n.Tr("pnb.desc");

    private static Sprite? _cachedIcon;

    public static bool IsPnbRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的 PNB 物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsPnbRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<PnbItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<PnbItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "pnb-icon";
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
    /// 在 Item.GlobalItems 注册 PNB 的 ItemInfo。
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

            var useMethod = typeof(PnbItemSystem).GetMethod(
                nameof(PnbUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered PNB ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register PNB: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// PNB 使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// </summary>
    private static void PnbUseAction(Body body, Item item)
    {

        InjectorSound.Play();
        Plugin.Log.LogInfo("PNB useAction invoked by game native system.");

        PnbEffectController.Attach(body).ActivateOrRefresh();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied PNB: muscle regeneration + stamina recovery effect activated.");
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
            tags = "drug,medicine,medical,stim,combine,craft",
            rec = new Recognition(13)
        };
        info.SetTags();

        var useMethod = typeof(PnbItemSystem).GetMethod(
            nameof(PnbUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            rec = new Recognition(13),
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
            var iconPath = Path.Combine(assetDir, "pnb.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "pnb.webp");
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
            _cachedIcon.name = "pnb-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load PNB icon: {ex.Message}");
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
/// PNB 物品标记组件。
/// </summary>
public sealed class PnbItemMarker : MonoBehaviour
{
    public string itemKey = PnbItemSystem.ItemKey;
    public string displayName = PnbItemSystem.DisplayName;
    public string description = PnbItemSystem.Description;
}

/// <summary>
/// PNB 效果控制器。
/// 增益期（300s / 5min）：立即修复指甲（clawHealth = 100）；前 120s 每秒所有肢体 +0.2 肌肉健康。
/// 副作用（300s 后）：永久 STR -1；兴奋剂震颤 60s。
/// </summary>
public sealed class PnbEffectController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Delay,       // 1s 生效延迟
        Buff,        // 300s 增益期（指甲修复 + 肌肉愈合 120s + 耐力恢复 300s）
        SideEffect,  // 60s 副作用（仅震颤维持）
    }

    internal const float ActivationDelay = 1f;
    internal const float HealDuration = 120f;               // 肌肉愈合持续 2 分钟
    internal const float BuffDuration = 300f;               // 肌肉愈合持续 5 分钟
    internal const float TremorDuration = 60f;              // 震颤持续 60 秒
    internal const float HealPerSecond = 0.2f;              // 每秒每个肢体肌肉恢复
    internal const int StrengthPenalty = 1;                  // 力量等级永久 -1
    internal const float StimulantTremorIntensity = 4f;     // 兴奋剂震颤强度

    /// <summary>游戏原生 Body.clawHealth 字段反射缓存（角蛋白生长素/指甲健康）。</summary>
    private static FieldInfo? _clawHealthField;

    private Body? _body;
    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _tickAccumulator;
    private float _elapsed;                    // 已生效时间（不含延迟）
    private bool _strengthPenaltyApplied;      // STR 惩罚是否已触发

    public static PnbEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<PnbEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<PnbEffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;

        if (isRefresh)
        {
            StimBuffIndicator.ShowOneTimeEffect(PnbItemSystem.ItemKey, I18n.Tr("pnb.ot.0"));
            Plugin.Log.LogInfo("[PNB] Refresh: positive effects skipped, timer reset.");
        }

        _phase = Phase.Delay;
        _phaseTimer = ActivationDelay;
        _elapsed = 0f;
        _tickAccumulator = 0f;
        _strengthPenaltyApplied = false; // 负面STR惩罚每次都触发
        enabled = true;

        StimBuffIndicator.ShowBuff(
            PnbItemSystem.ItemKey,
            I18n.Tr("pnb.buff"),
            TryGetPnbIcon(),
            BuffDuration + TremorDuration + ActivationDelay,
            BuffDuration + TremorDuration + ActivationDelay,
            new Color(0.4f, 0.8f, 0.5f), // 嫩绿色（再生+恢复）
            positiveDescs: I18n.TrAll("pnb.pos.0", "pnb.pos.1"),
            negativeDescs: I18n.TrAll("pnb.neg.0"));
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _phase == Phase.Idle)
        {
            StimBuffIndicator.HideBuff(PnbItemSystem.ItemKey);
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
            case Phase.SideEffect:
                UpdateSideEffect();
                break;
        }
    }

    private void UpdateDelay()
    {
        _phaseTimer -= Time.deltaTime;

        StimBuffIndicator.ShowBuff(
            PnbItemSystem.ItemKey,
            "PNB",
            TryGetPnbIcon(),
            _phaseTimer + BuffDuration + TremorDuration,
            BuffDuration + TremorDuration + ActivationDelay,
            new Color(0.4f, 0.8f, 0.5f));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Buff;
            _phaseTimer = BuffDuration;
            _elapsed = 0f;
            _tickAccumulator = 0f;

            // 修复指甲损伤（角蛋白生长素效果 → clawHealth = 100）
            RestoreClawHealth();

            Plugin.Log.LogInfo($"[PNB] Buff phase: claw restored, muscle heal {HealPerSecond}/s for {HealDuration}s");
        }
    }

    private void UpdateBuff()
    {
        var dt = Time.deltaTime;
        _phaseTimer -= dt;
        _elapsed += dt;

        // 每秒恢复所有肢体肌肉健康（仅前 120s）
        if (_elapsed <= HealDuration)
        {
            _tickAccumulator += dt;
            while (_tickAccumulator >= 1f)
            {
                _tickAccumulator -= 1f;
                HealAllLimbMuscles();
                RestoreClawHealth();  // 维护指甲健康（角蛋白持续作用）
            }
        }

        // 更新 buff 显示
        var isHealing = _elapsed <= HealDuration;
        var label = isHealing ? I18n.Tr("pnb.buff_alt") : I18n.Tr("pnb.buff_alt2");
        var color = new Color(0.4f, 0.8f, 0.5f);

        StimBuffIndicator.ShowBuff(
            PnbItemSystem.ItemKey,
            label,
            TryGetPnbIcon(),
            _phaseTimer,
            BuffDuration,
            color);

        if (_phaseTimer <= 0f)
        {
            // 触发副作用：STR -1（永久）+ 震颤 60s
            ApplyStrengthPenaltyPermanent();
            ApplyStimulantTremor();

            _phase = Phase.SideEffect;
            _phaseTimer = TremorDuration;

            Plugin.Log.LogInfo($"[PNB] Buff ended. STR -{StrengthPenalty} permanently. Tremor active for {TremorDuration}s.");
        }
    }

    private void UpdateSideEffect()
    {
        var dt = Time.deltaTime;
        _phaseTimer -= dt;

        // 维持震颤强度（防止自然衰减）
        MaintainStimulantTremor();

        // 更新 buff 显示
        StimBuffIndicator.ShowBuff(
            PnbItemSystem.ItemKey,
            I18n.Tr("pnb.buff_side"),
            TryGetPnbIcon(),
            _phaseTimer,
            TremorDuration,
            new Color(1f, 0.3f, 0.3f), // 红色（副作用警告）
            positiveDescs: Array.Empty<string>(),
            negativeDescs: I18n.TrAll("pnb.neg.1"));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Idle;
            StimBuffIndicator.HideBuff(PnbItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[PNB] All effects ended.");
        }
    }

    /// <summary>
    /// 每秒恢复所有非断肢部位的肌肉健康。
    /// </summary>
    private void HealAllLimbMuscles()
    {
        if (_body == null || _body.limbs == null) return;

        foreach (var limb in _body.limbs)
        {
            if (limb == null || limb.dismembered) continue;
            limb.muscleHealth = Mathf.Min(100f, limb.muscleHealth + HealPerSecond);
        }
    }

    /// <summary>
    /// 完全修复指甲/爪健康（模拟角蛋白生长素效果）。
    /// 游戏原生机制：Body.clawHealth（0-100），在 HandleBody 中每帧通过 clawGrowthRate 自动恢复。
    /// PNB 直接将其设为 100（满值），绕过 clawRegrowTime 延迟。
    /// </summary>
    private void RestoreClawHealth()
    {
        if (_body == null) return;

        try
        {
            if (_clawHealthField == null)
            {
                _clawHealthField = typeof(Body).GetField("clawHealth",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (_clawHealthField == null)
                {
                    Plugin.Log.LogWarning("[PNB] Body.clawHealth field not found via reflection.");
                    return;
                }
            }

            var currentClaw = (float)(_clawHealthField.GetValue(_body) ?? 0f);
            if (currentClaw < 100f)
            {
                _clawHealthField.SetValue(_body, 100f);
                Plugin.Log.LogInfo($"[PNB] clawHealth restored: {currentClaw:F1} → 100 (角蛋白生长素/指甲修复).");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PNB] Failed to restore clawHealth: {ex.Message}");
        }
    }

    /// <summary>
    /// 延迟 5 分钟后触发：STR -1（永久，不恢复）。
    /// </summary>
    private void ApplyStrengthPenaltyPermanent()
    {
        if (!_strengthPenaltyApplied && _body != null)
        {
            SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatSTR, -StrengthPenalty);
            _strengthPenaltyApplied = true;
            StimBuffIndicator.ShowOneTimeEffect(PnbItemSystem.ItemKey, I18n.TrFmt("pnb.ot.1", StrengthPenalty), isNegative: true);
            Plugin.Log.LogInfo($"[PNB] Permanent STR -{StrengthPenalty} applied "
                + $"(now {SkillEffectHelper.GetLevel(_body, SkillEffectHelper.StatSTR)}).");
        }
    }

    /// <summary>
    /// 注入兴奋剂震颤（miscShakeIntensity）。
    /// </summary>
    private void ApplyStimulantTremor()
    {
        if (_body == null) return;
        SkillEffectHelper.AddStimulantTremor(_body, StimulantTremorIntensity);
        Plugin.Log.LogInfo($"[PNB] Stimulant tremor applied (intensity +{StimulantTremorIntensity}).");
    }

    /// <summary>
    /// 每帧维持震颤强度（防止自然衰减）。
    /// </summary>
    private void MaintainStimulantTremor()
    {
        if (_body == null) return;
        SkillEffectHelper.MaintainStimulantTremor(_body, StimulantTremorIntensity);
    }

    private void OnDisable()
    {
    }

    private static Sprite? _iconSprite;
        private static Sprite? TryGetPnbIcon()
    {
        if (_iconSprite != null) return _iconSprite; var method = typeof(PnbItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return _iconSprite = method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改 PNB 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class PnbHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<PnbItemMarker>();
        if (marker == null) return;

        if (item.Stats?.rec == null || !item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
