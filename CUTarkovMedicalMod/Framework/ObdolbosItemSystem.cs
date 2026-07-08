using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// Obdolbos 自制药物注射器系统。
/// 效果：延迟 5s 后随机触发一种作用（共 8 种，权重不同）。
/// 每次副作用都不一样，真正的"赌命"针剂。
/// </summary>
public static class ObdolbosItemSystem
{
    public const string ItemKey = "obdolbos";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("obdolbos.name");
    public static string Description => I18n.Tr("obdolbos.desc");

    private static Sprite? _cachedIcon;

    public static bool IsObdolbosRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsObdolbosRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<ObdolbosItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<ObdolbosItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "obdolbos-icon";
                    sr.sprite = adjusted;
                }
                else
                {
                    sr.sprite = icon;
                }
            }
        }
    }

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
            clone.tags = MergeTags(clone.tags, "drug,medicine,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(ObdolbosItemSystem).GetMethod(
                nameof(ObdolbosUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Obdolbos ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Obdolbos: {ex.Message}");
            return false;
        }
    }

    private static void ObdolbosUseAction(Body body, Item item)
    {
        InjectorSound.Play();
        Plugin.Log.LogInfo("Obdolbos useAction invoked.");

        // 激活延迟 + 随机效果控制器
        ObdolbosEffectController.Attach(body).Activate();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Obdolbos injected — random effect pending (5s delay).");
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
            tags = "drug,medicine,stim,combine,craft",
            rec = new Recognition(13)
        };
        info.SetTags();

        var useMethod = typeof(ObdolbosItemSystem).GetMethod(
            nameof(ObdolbosUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "obdolbos.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "obd1.webp");
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
            _cachedIcon.name = "obdolbos-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Obdolbos icon: {ex.Message}");
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
/// Obdolbos 物品标记组件。
/// </summary>
public sealed class ObdolbosItemMarker : MonoBehaviour
{
    public string itemKey = ObdolbosItemSystem.ItemKey;
    public string displayName = ObdolbosItemSystem.DisplayName;
    public string description = ObdolbosItemSystem.Description;
}

/// <summary>
/// Obdolbos 效果控制器。
/// 使用后延迟 5s，然后随机选择一种效果激活。
/// 效果权重（共 115）：天神10 / 抹除15 / 恢复15 / 脑损15 / 猝死15 / 代谢紊乱15 / 战斗兴奋15 / 辐射15
/// </summary>
public sealed class ObdolbosEffectController : MonoBehaviour
{
    private enum Outcome
    {
        None,
        GodMode,          // 10/115: 血容量恢复 + 肢体修复 + 脑健康 + 永久技能
        MindWipe,         // 15/115: 精神抹除
        PhysRecovery,     // 15/115: 吃喝回复 + 体温降低 10min
        BrainDamage,      // 15/115: 脑组织 -25
        InstantDeath,     // 15/115: 大脑归零
        MetabolicChaos,   // 15/115: 吃喝消耗 + 减重 + 升温 30s
        CombatStim,       // 15/115: 耐力恢复 80% 25min + 永久 STR+3
        Radioactive,      // 15/115: 辐射+10 + 患病+30 + 永久 INT+5
    }

    // 效果持续时间常量
    private const float ActivationDelay = 5f;
    private const float GodModeDuration = 30f;
    private const float PhysRecoveryDuration = 600f;    // 10 分钟
    private const float MetabolicDuration = 30f;
    private const float CombatStimDuration = 1500f;     // 25 分钟

    private Body? _body;
    private float _timer;
    private Outcome _outcome = Outcome.None;
    private bool _outcomeApplied;
    private bool _staminaRecoveryActive;
    private float _tickAccumulator;
    private float _initialTemp;        // 注射时的体温，温度偏移以此为准
    private string _instanceKey = "";        // 本实例的唯一 key
    private static int _nextInstanceId;

    public static ObdolbosEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<ObdolbosEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<ObdolbosEffectController>();
        controller._body = body;
        return controller;
    }

    public void Activate()
    {
        // 为每次注射分配唯一 key，使多重注射各自独立显示
        if (string.IsNullOrEmpty(_instanceKey))
        {
            _instanceKey = $"{ObdolbosItemSystem.ItemKey}_{_nextInstanceId++}";
            InjectIconForInstance(_instanceKey);
        }

        _timer = ActivationDelay;
        _outcome = Outcome.None;
        _outcomeApplied = false;
        _staminaRecoveryActive = false;
        _tickAccumulator = 0f;
        // 清除上一次注射可能残留的耐力恢复加成（新注射可能触发不同效果）
        if (_body != null)
            StaminaBonusManager.ClearBonus(_body, ObdolbosItemSystem.ItemKey);
        enabled = true;

        StimBuffIndicator.ShowBuff(
            _instanceKey,
            I18n.Tr("obdolbos.buff"),
            TryGetObdolbosIcon(),
            _timer,
            _timer,
            new Color(1f, 0.55f, 0f),
            positiveDescs: null,
            negativeDescs: null);
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null)
        {
            Cleanup();
            return;
        }

        _timer -= Time.deltaTime;

        // 延迟阶段
        if (!_outcomeApplied)
        {
            StimBuffIndicator.ShowBuff(
                _instanceKey,
                I18n.Tr("obdolbos.buff"),
                TryGetObdolbosIcon(),
                Mathf.Max(0f, _timer),
                ActivationDelay,
                new Color(1f, 0.55f, 0f),
                positiveDescs: null,
                negativeDescs: null);

            if (_timer <= 0f)
            {
                _outcome = RollOutcome();
                _initialTemp = _body!.temperature;
                ApplyOutcome(_outcome);
                _outcomeApplied = true;
                _timer = GetOutcomeDuration(_outcome);
                _tickAccumulator = 0f;
            }
            return;
        }

        // 效果进行阶段
        UpdateActiveEffect();

        if (_timer <= 0f && _outcome != Outcome.None)
        {
            Cleanup();
        }
    }

    #region Random Outcome

    private static Outcome RollOutcome()
    {
        // 权重总和 115
        const int TotalWeight = 115;
        var roll = Random.Range(0, TotalWeight);
        var cumulative = 0;

        cumulative += 10; if (roll < cumulative) { Plugin.Log.LogInfo("[Obdolbos] Rolled: 天神下凡 (10%)"); return Outcome.GodMode; }
        cumulative += 15; if (roll < cumulative) { Plugin.Log.LogInfo("[Obdolbos] Rolled: 精神抹除 (15%)"); return Outcome.MindWipe; }
        cumulative += 15; if (roll < cumulative) { Plugin.Log.LogInfo("[Obdolbos] Rolled: 生理恢复 (15%)"); return Outcome.PhysRecovery; }
        cumulative += 15; if (roll < cumulative) { Plugin.Log.LogInfo("[Obdolbos] Rolled: 脑损伤 (15%)"); return Outcome.BrainDamage; }
        cumulative += 15; if (roll < cumulative) { Plugin.Log.LogInfo("[Obdolbos] Rolled: 猝死 (15%)"); return Outcome.InstantDeath; }
        cumulative += 15; if (roll < cumulative) { Plugin.Log.LogInfo("[Obdolbos] Rolled: 代谢紊乱 (15%)"); return Outcome.MetabolicChaos; }
        cumulative += 15; if (roll < cumulative) { Plugin.Log.LogInfo("[Obdolbos] Rolled: 战斗兴奋 (15%)"); return Outcome.CombatStim; }
        // 剩余 15: Radioactive
        Plugin.Log.LogInfo("[Obdolbos] Rolled: 放射性污染 (15%)");
        return Outcome.Radioactive;
    }

    private static float GetOutcomeDuration(Outcome outcome) => outcome switch
    {
        Outcome.GodMode => GodModeDuration,
        Outcome.MindWipe => 10f,        // 瞬间效果，保留10秒让一次性通知显示
        Outcome.PhysRecovery => PhysRecoveryDuration,
        Outcome.BrainDamage => 10f,     // 瞬间效果，保留10秒让一次性通知显示
        Outcome.InstantDeath => 10f,    // 瞬间效果，保留10秒让一次性通知显示
        Outcome.MetabolicChaos => MetabolicDuration,
        Outcome.CombatStim => CombatStimDuration,
        Outcome.Radioactive => 10f,     // 瞬间效果，保留10秒让一次性通知显示
        _ => 0f
    };

    #endregion

    #region Apply Outcomes

    private void ApplyOutcome(Outcome outcome)
    {
        if (_body == null) return;

        switch (outcome)
        {
            case Outcome.GodMode:
                ApplyGodMode();
                break;
            case Outcome.MindWipe:
                ApplyMindWipe();
                break;
            case Outcome.PhysRecovery:
                ApplyPhysRecovery();
                break;
            case Outcome.BrainDamage:
                ApplyBrainDamage();
                break;
            case Outcome.InstantDeath:
                ApplyInstantDeath();
                break;
            case Outcome.MetabolicChaos:
                ApplyMetabolicChaos();
                break;
            case Outcome.CombatStim:
                ApplyCombatStim();
                break;
            case Outcome.Radioactive:
                ApplyRadioactive();
                break;
        }
    }

    /// <summary>
    /// 天神下凡：所有非断肢肌肉/表皮 +50, 脑组织 +40, STR/INT/RES 永久 +10。
    /// 血容量回复 (0.1L/s × 30s) 在 Update 循环中处理。
    /// </summary>
    private void ApplyGodMode()
    {
        if (_body == null) return;

        // 肢体修复
        var limbs = _body.limbs;
        if (limbs != null)
        {
            foreach (var limb in limbs)
            {
                if (limb == null || limb.dismembered) continue;
                limb.muscleHealth = Mathf.Min(100f, limb.muscleHealth + 50f);
                limb.skinHealth = Mathf.Min(100f, limb.skinHealth + 50f);
            }
        }

        // 脑组织修复
        _body.brainHealth = Mathf.Min(100f, _body.brainHealth + 40f);

        // 永久技能提升
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatSTR, 10);
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatINT, 10);
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatRES, 10);

        // 肢体修复、脑组织、技能提升均为一次性永久效果
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.0"));
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.1"));

        Plugin.Log.LogInfo("[Obdolbos] 天神下凡: limbs+50 muscle/skin, brain+40, STR/INT/RES+10 permanent.");
    }

    /// <summary>
    /// 精神抹除：通过原生 mindwipe 液体的 onDrink 回调触发，保留完整的视觉效果（Vignette、音效等）。
    /// 直接设置 mindWipe.active=true 会跳过 MindwipeScript.Start() 中的视觉特效实例化。
    /// </summary>
    private void ApplyMindWipe()
    {
        if (_body == null) return;
        try
        {
            // 通过原生 mindwipe 液体的 onDrink 回调触发精神抹除（服用1ml）
            if (Liquids.Registry.TryGetValue("mindwipe", out var mindwipeLiquid) && mindwipeLiquid.onDrink != null)
            {
                mindwipeLiquid.onDrink.Invoke(1f, _body);
                Plugin.Log.LogInfo("[Obdolbos] Mindwipe triggered via native onDrink callback (1ml).");
            }
            else
            {
                // Fallback: 直接设置 mindWipe.active（无视觉特效）
                Plugin.Log.LogWarning("[Obdolbos] mindwipe liquid not found in Registry, falling back to direct active=true.");
                var mindWipe = _body.mindWipe;
                if (mindWipe == null)
                {
                    mindWipe = _body.gameObject.AddComponent<MindwipeScript>();
                    mindWipe.body = _body;
                    _body.mindWipe = mindWipe;
                }
                mindWipe.active = true;
            }

            StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.2"), isNegative: true);

            // 隐藏减益：头部和胸部增加70疼痛
            var limbs = _body.limbs;
            if (limbs != null)
            {
                foreach (var limb in limbs)
                {
                    if (limb == null || limb.dismembered) continue;
                    if (limb.isHead || (limb.isVital && !limb.isHead))
                        limb.pain += 70f;
                }
            }

            Plugin.Log.LogInfo("[Obdolbos] Mindwipe triggered. Hidden debuff: +70 pain to head and chest.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Obdolbos] Mindwipe failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 生理恢复：立即回复 5 吃喝；体温 -2°C 持续 10min（在 Update 中维持）。
    /// </summary>
    private void ApplyPhysRecovery()
    {
        if (_body == null) return;
        _body.Eat(5f, 0.03f); // 3:1 比例，0.03f = +0.01kg 实际
        _body.Drink(5f);
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.3"));
        Plugin.Log.LogInfo($"[Obdolbos] 生理恢复: food/water +5, temp -2°C (from {_initialTemp:F1}) for 600s.");
    }

    /// <summary>
    /// 脑损伤：脑组织健康 -25。
    /// </summary>
    private void ApplyBrainDamage()
    {
        if (_body == null) return;
        _body.brainHealth = Mathf.Max(0f, _body.brainHealth - 25f);
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.4"), isNegative: true);
        Plugin.Log.LogInfo($"[Obdolbos] 脑损伤: brainHealth -25 (now {_body.brainHealth:F1}).");
    }

    /// <summary>
    /// 猝死：大脑完整度归零。
    /// </summary>
    private void ApplyInstantDeath()
    {
        if (_body == null) return;
        _body.brainHealth = 0f;
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.5"), isNegative: true);
        Plugin.Log.LogInfo("[Obdolbos] 猝死: brainHealth = 0.");
    }

    /// <summary>
    /// 代谢紊乱：每秒 -1 吃喝、-0.1kg 体重、体温 +3°C，持续 30s（在 Update 中处理）。
    /// </summary>
    private void ApplyMetabolicChaos()
    {
        if (_body == null) return;
        Plugin.Log.LogInfo($"[Obdolbos] 代谢紊乱: food/water -1/s, weight -0.1kg/s, temp +3°C (from {_initialTemp:F1}) for 30s.");
    }

    /// <summary>
    /// 战斗兴奋：耐力恢复 +80%（RES +8），持续 25min；永久 STR +3。
    /// </summary>
    private void ApplyCombatStim()
    {
        if (_body == null) return;
        _staminaRecoveryActive = true;  // 由 UpdateActiveEffect 每帧直接操作 stamina
        // 注册耐力恢复加成到多来源叠加管理器（+80%，持续 25 分钟）
        StaminaBonusManager.AddBonus(_body, 0.8f, CombatStimDuration, ObdolbosItemSystem.ItemKey);
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatSTR, 3);
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.6"));
        Plugin.Log.LogInfo("[Obdolbos] 战斗兴奋: stamina recovery +80% for 1500s, STR +3 permanent.");
    }

    /// <summary>
    /// 放射性污染：辐射 +10Gy、患病 +30、永久 INT +5。
    /// </summary>
    private void ApplyRadioactive()
    {
        if (_body == null) return;
        _body.radiationSickness += 33f;   // ≈ 显示 +10Gy（内部单位~3.3:1）
        _body.sicknessAmount += 30f;
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatINT, 5);
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.7"), isNegative: true);
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, I18n.Tr("obdolbos.ot.8"));
        Plugin.Log.LogInfo($"[Obdolbos] 放射性污染: radiation +10Gy (now {_body.radiationSickness:F1}), sickness +30, INT +5 permanent.");
    }

    #endregion

    #region Per-Frame Effects

    private void UpdateActiveEffect()
    {
        if (_body == null) return;

        var desc = GetOutcomeDescriptions(_outcome);
        StimBuffIndicator.ShowBuff(
            _instanceKey,
            GetOutcomeLabel(_outcome),
            TryGetObdolbosIcon(),
            Mathf.Max(0f, _timer),
            GetOutcomeDuration(_outcome),
            GetOutcomeColor(_outcome),
            positiveDescs: desc.Positive,
            negativeDescs: desc.Negative);

        switch (_outcome)
        {
            case Outcome.GodMode:
                TickGodMode();
                break;
            case Outcome.PhysRecovery:
                TickPhysRecovery();
                break;
            case Outcome.MetabolicChaos:
                TickMetabolicChaos();
                break;
            case Outcome.CombatStim:
                // 每帧直接追加耐力恢复 +80%（仅当本物品是当前最强来源时）
                if (_staminaRecoveryActive
                    && StaminaBonusManager.IsTopSource(_body!, ObdolbosItemSystem.ItemKey)
                    && _body!.staminaStrength != null)
                {
                    var extraRecovery = _body.staminaStrength.Evaluate(_body.energy * 0.01f) * Time.deltaTime * 0.8f;
                    _body.stamina += extraRecovery;
                }
                break;
        }
    }

    /// <summary>
    /// 天神下凡 Tick：血容量每秒 +0.1L，仅当血容量低于阈值时恢复（匹配 ETG-c 守卫逻辑）。
    /// </summary>
    private void TickGodMode()
    {
        if (_body!.bloodVolume >= 5f) return;   // 已满则不操作，防止单位差异导致误 clamp

        _tickAccumulator += Time.deltaTime;
        while (_tickAccumulator >= 1f)
        {
            _tickAccumulator -= 1f;
            _body.bloodVolume = Mathf.Min(5f, _body.bloodVolume + 0.1f);
        }
    }

    /// <summary>
    /// 生理恢复 Tick：维持体温 -2°C。
    /// </summary>
    private void TickPhysRecovery()
    {
        // 持续维持低温（相对注射时 -2°C），只降温不升温
        var targetTemp = _initialTemp - 2f;
        if (_body!.temperature > targetTemp)
            _body.temperature -= Mathf.Min(_body.temperature - targetTemp, Time.deltaTime * 0.5f);
    }

    /// <summary>
    /// 代谢紊乱 Tick：每秒 -1 吃喝、-0.1kg 体重，持续升温 +3°C。
    /// </summary>
    private void TickMetabolicChaos()
    {
        _tickAccumulator += Time.deltaTime;
        while (_tickAccumulator >= 1f)
        {
            _tickAccumulator -= 1f;
            _body!.hunger = Mathf.Max(0f, _body.hunger - 1f);
            _body.thirst = Mathf.Max(0f, _body.thirst - 1f);
            _body.weightOffset -= 0.3f; // 3:1 比例，0.3f = -0.1kg 实际
        }

        // 持续升温 +3°C（相对注射时体温），只升温不降温
        var targetTemp = _initialTemp + 3f;
        if (_body!.temperature < targetTemp)
            _body.temperature += Mathf.Min(targetTemp - _body.temperature, Time.deltaTime * 0.5f);
    }

    #endregion

    #region Utility

    private void Cleanup()
    {
        _staminaRecoveryActive = false;
        if (_body != null)
            StaminaBonusManager.ClearBonus(_body, ObdolbosItemSystem.ItemKey);
        _outcome = Outcome.None;
        _outcomeApplied = false;
        StimBuffIndicator.HideBuff(_instanceKey);
        enabled = false;
    }

    /// <summary>
    /// 为新实例 key 注入图标到 MoodleManager.icons，确保状态栏能正确显示图标。
    /// </summary>
    private static void InjectIconForInstance(string instanceKey)
    {
        var icon = TryGetObdolbosIcon();
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

            var stimKey = $"stim_{instanceKey}";
            if (!icons.ContainsKey(stimKey))
            {
                icons[stimKey] = icon;
                Plugin.Log.LogInfo($"[Obdolbos] Injected icon for instance key '{stimKey}'");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Obdolbos] Failed to inject icon for '{instanceKey}': {ex.Message}");
        }
    }

    private void OnDisable()
    {
        _staminaRecoveryActive = false;
        if (_body != null)
            StaminaBonusManager.ClearBonus(_body, ObdolbosItemSystem.ItemKey);
    }

    private static string GetOutcomeLabel(Outcome outcome) => outcome switch
    {
        Outcome.GodMode => I18n.Tr("obdolbos.outcome.0"),
        Outcome.MindWipe => I18n.Tr("obdolbos.outcome.1"),
        Outcome.PhysRecovery => I18n.Tr("obdolbos.outcome.2"),
        Outcome.BrainDamage => I18n.Tr("obdolbos.outcome.3"),
        Outcome.InstantDeath => I18n.Tr("obdolbos.outcome.4"),
        Outcome.MetabolicChaos => I18n.Tr("obdolbos.outcome.5"),
        Outcome.CombatStim => I18n.Tr("obdolbos.outcome.6"),
        Outcome.Radioactive => I18n.Tr("obdolbos.outcome.7"),
        _ => I18n.Tr("obdolbos.buff")
    };

    private static (IReadOnlyList<string>? Positive, IReadOnlyList<string>? Negative) GetOutcomeDescriptions(Outcome outcome) => outcome switch
    {
        Outcome.GodMode => (I18n.TrAll("obdolbos.outcome_pos.0"), null as string[]),
        Outcome.MindWipe => (null, null as string[]),
        Outcome.PhysRecovery => (I18n.TrAll("obdolbos.outcome_pos.2"), null as string[]),
        Outcome.BrainDamage => (null, null as string[]),
        Outcome.InstantDeath => (null, null as string[]),
        Outcome.MetabolicChaos => (null, I18n.TrAll("obdolbos.outcome_neg.5.0", "obdolbos.outcome_neg.5.1", "obdolbos.outcome_neg.5.2", "obdolbos.outcome_neg.5.3")),
        Outcome.CombatStim => (I18n.TrAll("obdolbos.outcome_pos.6"), null as string[]),
        Outcome.Radioactive => (null, null as string[]),
        _ => (null as string[], null as string[])
    };

    private static Color GetOutcomeColor(Outcome outcome) => outcome switch
    {
        // 正面效果 → 绿色系
        Outcome.GodMode => new Color(0.2f, 0.9f, 0.4f),
        Outcome.PhysRecovery => new Color(0.3f, 0.8f, 0.5f),
        Outcome.CombatStim => new Color(0.2f, 0.7f, 0.9f),
        // 负面效果 → 红色系
        Outcome.MindWipe => new Color(0.9f, 0.3f, 0.5f),
        Outcome.BrainDamage => new Color(1f, 0.4f, 0.3f),
        Outcome.InstantDeath => new Color(0.8f, 0f, 0f),
        Outcome.MetabolicChaos => new Color(1f, 0.5f, 0.2f),
        Outcome.Radioactive => new Color(0.7f, 0.6f, 0.1f),
        _ => new Color(1f, 0.55f, 0f)
    };

    private static Sprite? TryGetObdolbosIcon()
    {
        var method = typeof(ObdolbosItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }

    #endregion
}

/// <summary>
/// 修改 Obdolbos 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class ObdolbosHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<ObdolbosItemMarker>();
        if (marker == null) return;
            if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
