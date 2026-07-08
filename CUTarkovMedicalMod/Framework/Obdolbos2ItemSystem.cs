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
/// Obdolbos 2 鸡尾酒兴奋剂注射器系统。
/// 效果：立即永久 STR/RES/INT +6、负重上限 +3u；
/// 耐力恢复 -30%、最大耐力 -20% 持续 40min。
/// 副作用：延迟 5min 后每秒 -0.2 饱食/水分、头部/胸部肌肉 -0.3/秒，持续 5min。
/// </summary>
public static class Obdolbos2ItemSystem
{
    public const string ItemKey = "obdolbos2";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("obdolbos2.name");
    public static string Description => I18n.Tr("obdolbos2.desc");
    private static Sprite? _cachedIcon;

    public static bool IsObdolbos2Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsObdolbos2Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<Obdolbos2ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Obdolbos2ItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "obd2-icon";
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
            clone.value = 15;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(Obdolbos2ItemSystem).GetMethod(
                nameof(Obdolbos2UseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Obdolbos 2 ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Obdolbos 2: {ex.Message}");
            return false;
        }
    }

    private static void Obdolbos2UseAction(Body body, Item item)
    {
        InjectorSound.Play();
        Plugin.Log.LogInfo("Obdolbos 2 useAction invoked by game native system.");
        Obdolbos2EffectController.Attach(body).ActivateOrRefresh();
        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);
        Plugin.Log.LogInfo("Applied Obdolbos 2: permanent stats + stamina debuff + delayed drain.");
    }

    #region Helper Methods

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName, description = Description,
            category = "ModStim", usable = true, usableOnLimb = false,
            usableWithLMB = false, combineable = true,
            destroyAtZeroCondition = true, scaleWeightWithCondition = false,
            weight = 0.1f, value = 15, tags = "drug,medicine,medical,stim,combine,craft"
        };
        info.SetTags();
        var useMethod = typeof(Obdolbos2ItemSystem).GetMethod(nameof(Obdolbos2UseAction), BindingFlags.Static | BindingFlags.NonPublic);
        if (useMethod != null) info.useAction = (ItemInfo.Use)Delegate.CreateDelegate(typeof(ItemInfo.Use), useMethod);
        return info;
    }

    private static ItemInfo CloneItemInfo(ItemInfo? source)
    {
        if (source == null) return CreateFallbackItemInfo();
        var clone = new ItemInfo
        {
            fullName = source.fullName, description = source.description, category = source.category,
            slotRotation = source.slotRotation, usable = source.usable, usableOnLimb = source.usableOnLimb,
            rotSpeed = source.rotSpeed, useAction = source.useAction, useLimbAction = source.useLimbAction,
            destroyAtZeroCondition = source.destroyAtZeroCondition, weight = source.weight,
            scaleWeightWithCondition = source.scaleWeightWithCondition, onlyHoldInHands = source.onlyHoldInHands,
            autoAttack = source.autoAttack, usableWithLMB = source.usableWithLMB, wearable = source.wearable,
            wearableCanBeHeld = source.wearableCanBeHeld, desiredWearLimb = source.desiredWearLimb,
            wearSlotId = source.wearSlotId, wearableArmor = source.wearableArmor, wearableIsolation = source.wearableIsolation,
            wearableHitDurabilityLossMultiplier = source.wearableHitDurabilityLossMultiplier,
            jumpHeightMultChange = source.jumpHeightMultChange, combineable = source.combineable,
            ignoreDepression = source.ignoreDepression, value = source.value,
            wearableVisualOffset = source.wearableVisualOffset, tags = source.tags,
            decayInfo = source.decayInfo, decayMinutes = source.decayMinutes, rec = new Recognition(13), qualities = source.qualities
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
            var iconPath = Path.Combine(assetDir, "obd2.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "obd2.webp");
                if (!File.Exists(iconPath)) return null;
            }
            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            _cachedIcon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "obd2-icon";
            return _cachedIcon;
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"Failed to load Obdolbos 2 icon: {ex.Message}"); return null; }
    }

    private static Sprite? CreateSpriteMatchingBaseSize(Texture2D texture, Sprite? baseSprite)
    {
        if (texture == null) return null;
        if (baseSprite == null) return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 32f);
        var baseRect = baseSprite.rect;
        var basePpu = baseSprite.pixelsPerUnit > 0f ? baseSprite.pixelsPerUnit : 32f;
        var widthScale = baseRect.width > 0f ? texture.width / baseRect.width : 1f;
        var heightScale = baseRect.height > 0f ? texture.height / baseRect.height : 1f;
        var dominantScale = Mathf.Max(1f, Mathf.Max(widthScale, heightScale));
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), basePpu * dominantScale);
    }

    #endregion
}

public sealed class Obdolbos2ItemMarker : MonoBehaviour
{
    public string itemKey = Obdolbos2ItemSystem.ItemKey;
    public string displayName = Obdolbos2ItemSystem.DisplayName;
    public string description = Obdolbos2ItemSystem.Description;
}

/// <summary>
/// Obdolbos 2 效果控制器：
/// 立即永久：STR/RES/INT +6，负重上限 +3u。
/// 持续（2400s / 40min）：耐力恢复 -30%，最大耐力 -20%。
/// 副作用（延迟 300s，持续 300s / 5min）：每秒 -0.2 饱食/水分，头部/胸部 -0.3 肌肉/秒。
/// </summary>
public sealed class Obdolbos2EffectController : MonoBehaviour
{
    internal const float ActivationDelay = 1f;
    internal const float BuffDuration = 2400f;              // 40 分钟
    internal const float SideEffectDelay = 300f;            // 5 分钟延迟
    internal const float SideEffectDuration = 300f;         // 5 分钟
    internal const float CarryWeightBonus = 3f;             // 负重上限 +3u
    internal const int StatBoost = 6;                       // STR/RES/INT +6
    internal const float StaminaRecoveryPenalty = 0.30f;    // 耐力恢复 -30%
    internal const float StaminaMaxRatio = 0.80f;           // 最大耐力 80%
    internal const float FoodWaterDrainPerSec = 0.2f;       // 每秒消耗饱食/水分
    internal const float MuscleDrainPerSec = 0.3f;          // 头部/胸部肌肉每秒消耗

    private Body? _body;
    private float _phaseTimer;
    private float _sideEffectTimer;
    private float _tickAccumulator;
    private float _staminaCapBaseline;
    private bool _buffStarted;
    private bool _sideEffectActive;
    private int _injectionCount;

    internal static Obdolbos2EffectController? ActiveInstance;
    internal bool IsCarryWeightActive => _injectionCount == 1 && _buffStarted && _phaseTimer > 0f;

    public static Obdolbos2EffectController Attach(Body body)
    {
        var c = body.gameObject.GetComponent<Obdolbos2EffectController>();
        if (c == null) c = body.gameObject.AddComponent<Obdolbos2EffectController>();
        c._body = body;
        return c;
    }

    public void ActivateOrRefresh()
    {
        _injectionCount++;

        if (_injectionCount == 1)
        {
            // 第一次注射：立即永久增益
            if (_body != null)
            {
                SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatSTR, StatBoost);
                SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatRES, StatBoost);
                SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatINT, StatBoost);
            }
            StimBuffIndicator.ShowOneTimeEffect(Obdolbos2ItemSystem.ItemKey, I18n.TrFmt("obdolbos2.ot.0", StatBoost));
            StimBuffIndicator.ShowOneTimeEffect(Obdolbos2ItemSystem.ItemKey, I18n.TrFmt("obdolbos2.ot.1", CarryWeightBonus));
            Plugin.Log.LogInfo("[Obdolbos 2] First injection: positive effects applied.");
        }
        else
        {
            // 第二次及以后注射：跳过正面效果，仅触发负面效果
            StimBuffIndicator.ShowOneTimeEffect(Obdolbos2ItemSystem.ItemKey, I18n.Tr("obdolbos2.ot.2"));
            Plugin.Log.LogInfo($"[Obdolbos 2] Injection #{_injectionCount}: positive effects skipped, negative only.");
        }

        _phaseTimer = ActivationDelay + BuffDuration;
        _sideEffectTimer = SideEffectDelay;
        _tickAccumulator = 0f;
        _buffStarted = false;
        _sideEffectActive = false;
        enabled = true;
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null)
        {
            Cleanup();
            return;
        }

        _phaseTimer -= Time.deltaTime;

        if (_phaseTimer <= 0f)
        {
            Cleanup();
            return;
        }

        if (!_buffStarted && _phaseTimer <= BuffDuration)
        {
            _buffStarted = true;
            _staminaCapBaseline = _body.stamina;
            ActiveInstance = this;
            Plugin.Log.LogInfo($"[Obdolbos 2] Stamina debuff active: recovery -{StaminaRecoveryPenalty*100}%, max -{(1f-StaminaMaxRatio)*100}% for {BuffDuration}s.");
        }

        if (_buffStarted)
        {
            // ===== 耐力恢复 -30%：扣除部分自然恢复 =====
            try
            {
                if (_body.staminaStrength != null)
                {
                    var naturalRecovery = _body.staminaStrength.Evaluate(_body.energy * 0.01f) * Time.deltaTime;
                    _body.stamina -= naturalRecovery * StaminaRecoveryPenalty;
                }
            }
            catch { }

            // ===== 最大耐力 -20% =====
            try
            {
                if (_body.stamina > _staminaCapBaseline)
                    _staminaCapBaseline = _body.stamina;
                var cap = _staminaCapBaseline * StaminaMaxRatio;
                if (_body.stamina > cap)
                    _body.stamina = cap;
            }
            catch { }
        }

        // ===== 副作用阶段 =====
        if (!_sideEffectActive)
        {
            _sideEffectTimer -= Time.deltaTime;
            if (_sideEffectTimer <= 0f)
            {
                _sideEffectActive = true;
                _sideEffectTimer = SideEffectDuration;
                _tickAccumulator = 0f;
                Plugin.Log.LogInfo($"[Obdolbos 2] Side effect: food/water -{FoodWaterDrainPerSec}/s, head/chest muscle -{MuscleDrainPerSec}/s for {SideEffectDuration}s.");
            }
        }
        else
        {
            _sideEffectTimer -= Time.deltaTime;
            _tickAccumulator += Time.deltaTime;
            while (_tickAccumulator >= 1f)
            {
                _tickAccumulator -= 1f;
                DrainFoodWater();
                DrainMuscle();
            }
            if (_sideEffectTimer <= 0f)
            {
                _sideEffectActive = false;
                Plugin.Log.LogInfo("[Obdolbos 2] Side effect ended.");
            }
        }

        // 状态栏显示
        ShowStatus();
    }

    private void ShowStatus()
    {
        var hasDebuff = _buffStarted && _phaseTimer > 0f;
        var hasSide = _sideEffectActive;

        IReadOnlyList<string>? pos = null;
        IReadOnlyList<string> neg;

        if (hasSide)
            neg = I18n.TrAll("obdolbos2.neg.0", "obdolbos2.neg.1", "obdolbos2.neg.2", "obdolbos2.neg.3");
        else if (hasDebuff)
            neg = I18n.TrAll("obdolbos2.neg.0", "obdolbos2.neg.1");
        else
            neg = Array.Empty<string>();

        var remaining = _phaseTimer;
        var total = BuffDuration + ActivationDelay;
        var label = _injectionCount >= 2
            ? (hasSide ? I18n.Tr("obdolbos2.buff_alt2") : I18n.Tr("obdolbos2.buff_alt"))
            : (hasSide ? I18n.Tr("obdolbos2.buff_side") : I18n.Tr("obdolbos2.buff"));

        StimBuffIndicator.ShowBuff(
            Obdolbos2ItemSystem.ItemKey,
            label,
            TryGetIcon(),
            remaining,
            total,
            new Color(1f, 0.55f, 0f), // 橙色
            positiveDescs: pos,
            negativeDescs: neg);
    }

    private void DrainFoodWater()
    {
        if (_body == null) return;
        try
        {
            _body.hunger = Mathf.Max(0f, _body.hunger - FoodWaterDrainPerSec);
            _body.thirst = Mathf.Max(0f, _body.thirst - FoodWaterDrainPerSec);
        }
        catch { }
    }

    private void DrainMuscle()
    {
        if (_body == null) return;
        try
        {
            var limbs = _body.limbs;
            if (limbs != null)
            {
                foreach (var limb in limbs)
                {
                    if (limb == null || limb.dismembered) continue;
                    if (limb.isHead || (limb.isVital && !limb.isHead))
                        limb.muscleHealth = Mathf.Max(0f, limb.muscleHealth - MuscleDrainPerSec);
                }
            }
        }
        catch { }
    }

    private void Cleanup()
    {
        ActiveInstance = null;
        StimBuffIndicator.HideBuff(Obdolbos2ItemSystem.ItemKey);
        enabled = false;
        Plugin.Log.LogInfo("[Obdolbos 2] Effect ended.");
    }

    private static Sprite? TryGetIcon()
    {
        var method = typeof(Obdolbos2ItemSystem).GetMethod("TryLoadIcon", BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class Obdolbos2HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;
        var marker = item.GetComponent<Obdolbos2ItemMarker>();
        if (marker == null) return;
        if (!item.Stats.rec.recognizable) return;
        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
