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

    public const string DisplayName = "Obdolbos鸡尾酒兴奋剂注射器【Obdolbos】";
    public const string Description =
        "装有自制药品的注射器，由 TerraGroup 实验室的前雇员Sanitar制作，标签处有着他的签名，每次带来的副作用都不一样。" +
        "要是没什么可失去了的话，你大可以冒这个险。这个一看就和其他针剂不太一样，不太对劲...\n\n" +
        "<color=#ffcc00>效果：延迟5秒后随机触发以下效果之一：</color>\n" +
        "<color=#4fc3f7>  · 10%：血容量每秒回复0.1L持续30s；所有肢体+50肌肉/表皮；脑组织+40；力量/智力/韧性等级永久+10</color>\n" +
        "<color=#ff6666>  · 15%：精神抹除</color>\n" +
        "<color=#4fc3f7>  · 15%：立即回复5吃喝；体温-2°C持续10分钟</color>\n" +
        "<color=#ff6666>  · 15%：脑组织健康度-25</color>\n" +
        "<color=#cc0000>  · 15%：猝死</color>\n" +
        "<color=#ff6666>  · 15%：每秒-1水分与饱食度、减重0.1kg、体温+3°C持续30秒</color>\n" +
        "<color=#4fc3f7>  · 15%：耐力恢复+80%持续25分钟；永久力量等级+3</color>\n" +
        "<color=#ff6666>  · 15%：辐射+10gy、患病+30；永久智力等级+5</color>";

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
            tags = "drug,medicine,stim,combine,craft"
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
        enabled = true;

        StimBuffIndicator.ShowBuff(
            _instanceKey,
            "Obdolbos",
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
                "Obdolbos",
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
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "肢体+50 脑组织+40");
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "STR+10 INT+10 RES+10 永久");

        Plugin.Log.LogInfo("[Obdolbos] 天神下凡: limbs+50 muscle/skin, brain+40, STR/INT/RES+10 permanent.");
    }

    /// <summary>
    /// 精神抹除：触发 Body.mindWipe (MindwipeScript)。
    /// </summary>
    private void ApplyMindWipe()
    {
        if (_body == null) return;
        try
        {
            var mindWipe = _body.mindWipe;
            if (mindWipe == null)
            {
                // 游戏可能未自动挂载 MindwipeScript，动态创建
                mindWipe = _body.gameObject.AddComponent<MindwipeScript>();
                mindWipe.body = _body;
                _body.mindWipe = mindWipe;
                Plugin.Log.LogInfo("[Obdolbos] MindwipeScript was null, created dynamically.");
            }
            mindWipe.active = true;
            StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "精神抹除触发", isNegative: true);

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
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "饱食/水分+5");
        Plugin.Log.LogInfo($"[Obdolbos] 生理恢复: food/water +5, temp -2°C (from {_initialTemp:F1}) for 600s.");
    }

    /// <summary>
    /// 脑损伤：脑组织健康 -25。
    /// </summary>
    private void ApplyBrainDamage()
    {
        if (_body == null) return;
        _body.brainHealth = Mathf.Max(0f, _body.brainHealth - 25f);
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "脑组织-25", isNegative: true);
        Plugin.Log.LogInfo($"[Obdolbos] 脑损伤: brainHealth -25 (now {_body.brainHealth:F1}).");
    }

    /// <summary>
    /// 猝死：大脑完整度归零。
    /// </summary>
    private void ApplyInstantDeath()
    {
        if (_body == null) return;
        _body.brainHealth = 0f;
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "立即死亡", isNegative: true);
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
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatSTR, 3);
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "力量+3 永久");
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
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "辐射+10Gy 患病+30", isNegative: true);
        StimBuffIndicator.ShowOneTimeEffect(_instanceKey, "INT+5 永久");
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
                // 每帧直接追加耐力恢复 +80%
                if (_staminaRecoveryActive && _body!.staminaStrength != null)
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
    }

    private static string GetOutcomeLabel(Outcome outcome) => outcome switch
    {
        Outcome.GodMode => "天神下凡",
        Outcome.MindWipe => "精神抹除",
        Outcome.PhysRecovery => "生理恢复",
        Outcome.BrainDamage => "脑损伤",
        Outcome.InstantDeath => "猝死",
        Outcome.MetabolicChaos => "代谢紊乱",
        Outcome.CombatStim => "战斗兴奋",
        Outcome.Radioactive => "放射性污染",
        _ => "Obdolbos"
    };

    private static (IReadOnlyList<string>? Positive, IReadOnlyList<string>? Negative) GetOutcomeDescriptions(Outcome outcome) => outcome switch
    {
        Outcome.GodMode => (new[] { "血容量每秒+0.1L" }, null as string[]),
        Outcome.MindWipe => (null, null as string[]),
        Outcome.PhysRecovery => (new[] { "体温-2℃" }, null as string[]),
        Outcome.BrainDamage => (null, null as string[]),
        Outcome.InstantDeath => (null, null as string[]),
        Outcome.MetabolicChaos => (null, new[] { "每秒-1饱食", "每秒-1水分", "体重-0.1/秒", "体温+3℃" }),
        Outcome.CombatStim => (new[] { "耐力恢复+80%" }, null as string[]),
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

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
