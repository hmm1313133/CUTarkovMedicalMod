using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// xTG-12 解毒剂系统。
/// 效果：+70% 抵抗力，毒素 -100%，持续 5 分钟。
/// 副作用：3 分钟后 20% 概率呕吐；5 分钟后颤栗 1 分钟。
/// </summary>
public static class Xtg12ItemSystem
{
    public const string ItemKey = "xtg12";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("xtg12.name");
    public static string Description => I18n.Tr("xtg12.desc");

    private static Sprite? _cachedIcon;

    public static bool IsXtg12Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsXtg12Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<Xtg12ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Xtg12ItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "xtg12-icon";
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
            clone.value = 18;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(Xtg12ItemSystem).GetMethod(
                nameof(Xtg12UseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered xTG-12 ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register xTG-12: {ex.Message}");
            return false;
        }
    }

    private static void Xtg12UseAction(Body body, Item item)
    {

        InjectorSound.Play();
        Plugin.Log.LogInfo("xTG-12 useAction invoked.");

        Xtg12EffectController.Attach(body).Activate();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("xTG-12 injected — detox + resistance for 5min; vomit at 3min; tremor at 5min.");
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
            value = 18,
            tags = "drug,medicine,medical,stim,combine,craft",
            rec = new Recognition(13)
        };
        info.SetTags();

        var useMethod = typeof(Xtg12ItemSystem).GetMethod(
            nameof(Xtg12UseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "xtg12.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "xtg12.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;

            texture.filterMode = FilterMode.Point;   // 像素化，不模糊
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "xtg12-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load xTG-12 icon: {ex.Message}");
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
/// xTG-12 物品标记组件。
/// </summary>
public sealed class Xtg12ItemMarker : MonoBehaviour
{
    public string itemKey = Xtg12ItemSystem.ItemKey;
    public string displayName = Xtg12ItemSystem.DisplayName;
    public string description = Xtg12ItemSystem.Description;
}

/// <summary>
/// xTG-12 效果控制器。
/// 时间线：
///   t=0:       +70% 免疫力，毒素清零
///   t=0~300s: 持续维持毒素清零（每 tick 刷新）
///   t=180s:    20% 概率触发呕吐（一次性）
///   t=300~360s: 颤栗（miscShakeIntensity 维持高水平 60 秒）
/// </summary>
public sealed class Xtg12EffectController : MonoBehaviour
{
    // 正面效果持续 5 分钟
    private const float DetoxDurationSeconds = 300f;
    // 3 分钟时触发呕吐判定
    private const float VomitTriggerTime = 180f;
    private const float VomitChance = 0.2f;
    // 颤栗持续 1 分钟
    private const float TremorDurationSeconds = 60f;
    // 颤栗强度（游戏内 miscShakeIntensity 每秒衰减 1.0，设 60 则约 60s 归零）
    private const float TremorIntensity = 60f;
    // 感染清除：2分钟内线性降低80%感染度，败血症 -70
    private const float InfectionClearDuration = 120f;
    private const float InfectionClearRatio = 0.8f;
    private const float SepsisReduceAmount = 70f;

    private Body? _body;
    private float _remaining;
    private float _accumulator;
    private bool _vomitChecked;
    private bool _immunityModified;
    private bool _isTremorPhase;
    private float _tremorRemaining;
    private float _infectionClearRemaining;
    private float[]? _initialInfections;
    private float _initialSepticShock;

    public static Xtg12EffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<Xtg12EffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<Xtg12EffectController>();
        controller._body = body;
        return controller;
    }

    public void Activate()
    {
        bool isRefresh = enabled;
        if (isRefresh)
            StimBuffIndicator.ShowOneTimeEffect(Xtg12ItemSystem.ItemKey, I18n.Tr("xtg12.ot.0"));

        _remaining = DetoxDurationSeconds;
        _accumulator = 0f;
        _vomitChecked = false;
        _immunityModified = false;
        _isTremorPhase = false;
        _tremorRemaining = 0f;
        enabled = true;

        // 记录初始感染度和败血症，用于线性清除
        _infectionClearRemaining = InfectionClearDuration;
        if (_body != null && _body.limbs != null)
        {
            _initialInfections = new float[_body.limbs.Length];
            for (int i = 0; i < _body.limbs.Length; i++)
                _initialInfections[i] = _body.limbs[i].infectionAmount;
            _initialSepticShock = _body.septicShock;
        }

        // 解毒效果（可叠加）
        ApplyDetoxEffect();

        StimBuffIndicator.ShowBuff(
            Xtg12ItemSystem.ItemKey,
            I18n.Tr("xtg12.buff"),
            TryGetXtg12Icon(),
            _remaining,
            DetoxDurationSeconds,
            new Color(0.3f, 0.7f, 1f), // 蓝色（解毒）
            positiveDescs: I18n.TrAll("xtg12.pos.0", "xtg12.pos.1", "xtg12.pos.2"));
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null)
        {
            Cleanup();
            return;
        }

        if (!_isTremorPhase)
        {
            // === 解毒阶段 ===
            if (_remaining <= 0f)
            {
                StimBuffIndicator.HideBuff(Xtg12ItemSystem.ItemKey);
                StartTremorPhase();
                return;
            }

            _accumulator += Time.deltaTime;
            while (_accumulator >= 1f)
            {
                _accumulator -= 1f;
                _remaining -= 1f;
                TickDetox();
            }

            // 3 分钟时触发呕吐判定（一次性）
            var elapsed = DetoxDurationSeconds - _remaining;
            if (!_vomitChecked && elapsed >= VomitTriggerTime)
            {
                _vomitChecked = true;
                TryTriggerVomit();
            }

            StimBuffIndicator.ShowBuff(
                Xtg12ItemSystem.ItemKey,
                I18n.Tr("xtg12.buff"),
                TryGetXtg12Icon(),
                _remaining,
                DetoxDurationSeconds,
                new Color(0.3f, 0.7f, 1f),
                positiveDescs: _infectionClearRemaining > 0f
                    ? I18n.TrAll("xtg12.pos.0", "xtg12.pos.1", "xtg12.pos.2")
                    : I18n.TrAll("xtg12.pos.0", "xtg12.pos.1"));

            if (_remaining <= 0f)
            {
                StimBuffIndicator.HideBuff(Xtg12ItemSystem.ItemKey);
                StartTremorPhase();
            }
        }
        else
        {
            // === 颤栗阶段 ===
            if (_tremorRemaining <= 0f)
            {
                Cleanup();
                return;
            }

            _accumulator += Time.deltaTime;
            while (_accumulator >= 1f)
            {
                _accumulator -= 1f;
                _tremorRemaining -= 1f;
            }

            // 持续维持颤栗强度（游戏每帧衰减，我们每帧覆盖）
            _body.miscShakeIntensity = TremorIntensity;

            StimBuffIndicator.ShowBuff(
                Xtg12ItemSystem.ItemKey,
                I18n.Tr("xtg12.buff_alt"),
                TryGetXtg12Icon(),
                _tremorRemaining,
                TremorDurationSeconds,
                new Color(1f, 0.4f, 0.4f), // 红色（负面）
                negativeDescs: I18n.TrAll("xtg12.neg.0"));

            if (_tremorRemaining <= 0f)
            {
                Cleanup();
            }
        }
    }

    private void ApplyDetoxEffect()
    {
        if (_body == null) return;

        // +70 免疫力持续 300 秒（通过 ImmunityBonusManager，支持多来源叠加）
        ImmunityBonusManager.AddBonus(_body, 70f, DetoxDurationSeconds, Xtg12ItemSystem.ItemKey);
        _immunityModified = true;

        // 毒素 -100%
        _body.venomCurrent = 0f;
        _body.venomTotal = 0f;

        Plugin.Log.LogInfo($"[xTG-12] Detox applied: ImmunityBonus +70 for {DetoxDurationSeconds}s, venom cleared.");
    }

    /// <summary>
    /// 每秒维持毒素清零（防止游戏引擎重新累积毒素）
    /// </summary>
    private void TickDetox()
    {
        if (_body == null) return;

        // 持续压制毒素
        if (_body.venomCurrent > 0f)
            _body.venomCurrent = 0f;
        if (_body.venomTotal > 0f)
            _body.venomTotal = 0f;

        // 感染清除：2分钟内线性降低80%感染度 + 败血症 -20%
        if (_infectionClearRemaining > 0f)
        {
            _infectionClearRemaining -= 1f;

            if (_body.limbs != null && _initialInfections != null)
            {
                float reductionPerTick = InfectionClearRatio / InfectionClearDuration;
                for (int i = 0; i < _body.limbs.Length && i < _initialInfections.Length; i++)
                {
                    float reduce = _initialInfections[i] * reductionPerTick;
                    _body.limbs[i].infectionAmount = Mathf.Max(0f, _body.limbs[i].infectionAmount - reduce);
                }
            }

            // 败血症线性降低20%
            float septicReduce = SepsisReduceAmount / InfectionClearDuration;
            _body.septicShock = Mathf.Max(0f, _body.septicShock - septicReduce);
        }
    }

    private void TryTriggerVomit()
    {
        if (_body == null) return;

        var roll = Random.value;
        Plugin.Log.LogInfo($"[xTG-12] Vomit check at 3min: roll={roll:F3}, threshold={VomitChance}");

        if (roll < VomitChance)
        {
            try
            {
                _body.vomiter?.Vomit();
                Plugin.Log.LogInfo("[xTG-12] Vomiting triggered (20% chance).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[xTG-12] Vomit failed: {ex.Message}");
            }
        }
        else
        {
            Plugin.Log.LogInfo("[xTG-12] Vomit skipped (80% chance to avoid).");
        }
    }

    private void StartTremorPhase()
    {
        _isTremorPhase = true;
        _tremorRemaining = TremorDurationSeconds;
        _accumulator = 0f;

        Plugin.Log.LogInfo("[xTG-12] Tremor phase started: miscShakeIntensity elevated for 60s.");
    }

    private void Cleanup()
    {
        // 清除抵抗力加成
        if (_immunityModified && _body != null)
        {
            ImmunityBonusManager.ClearBonus(_body, Xtg12ItemSystem.ItemKey);
            Plugin.Log.LogInfo("[xTG-12] Immunity bonus cleared.");
        }

        // 确保不再颤栗
        if (_body != null)
            _body.miscShakeIntensity = 0f;

        _immunityModified = false;
        _isTremorPhase = false;
        StimBuffIndicator.HideBuff(Xtg12ItemSystem.ItemKey);
        enabled = false;
    }

    private void OnDisable()
    {
        if (_immunityModified && _body != null)
        {
            ImmunityBonusManager.ClearBonus(_body, Xtg12ItemSystem.ItemKey);
            _immunityModified = false;
        }
        if (_body != null)
            _body.miscShakeIntensity = 0f;
    }

    private static Sprite? TryGetXtg12Icon()
    {
        var method = typeof(Xtg12ItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改 xTG-12 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class Xtg12HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<Xtg12ItemMarker>();
        if (marker == null) return;
            if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
