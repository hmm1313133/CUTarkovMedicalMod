using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 米屈肼注射器系统。
/// 效果：立即心脏纤颤进度 -20%；耐力上限 +10%、耐力恢复 +50%（直接操作 Body.stamina），持续 25 分钟。
/// 副作用：每秒 -0.1 饱食度和水分，持续 15 分钟。
/// </summary>
public static class MildronateItemSystem
{
    public const string ItemKey = "mildronate";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("mildronate.name");
    public static string Description => I18n.Tr("mildronate.desc");

    private static Sprite? _cachedIcon;

    public static bool IsMildronateRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的米屈肼物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsMildronateRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<MildronateItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<MildronateItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "mildronate-icon";
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
    /// 在 Item.GlobalItems 注册米屈肼的 ItemInfo。
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

            var useMethod = typeof(MildronateItemSystem).GetMethod(
                nameof(MildronateUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Mildronate ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Mildronate: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 米屈肼使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// </summary>
    private static void MildronateUseAction(Body body, Item item)
    {

        InjectorSound.Play();
        Plugin.Log.LogInfo("Mildronate useAction invoked by game native system.");

        MildronateEffectController.Attach(body).ActivateOrRefresh();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied Mildronate: combat stimulant with fibrillation reduction activated.");
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

        var useMethod = typeof(MildronateItemSystem).GetMethod(
            nameof(MildronateUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "Mildronate.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "Mildronate.webp");
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
            _cachedIcon.name = "mildronate-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Mildronate icon: {ex.Message}");
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
/// 米屈肼物品标记组件。
/// </summary>
public sealed class MildronateItemMarker : MonoBehaviour
{
    public string itemKey = MildronateItemSystem.ItemKey;
    public string displayName = MildronateItemSystem.DisplayName;
    public string description = MildronateItemSystem.Description;
}

/// <summary>
/// 米屈肼效果控制器：
/// 立即：心脏纤颤进度 -20%
/// 增益期（1500s / 25min）：耐力上限 +10%、耐力恢复 +50%（直接操作 Body.stamina）
/// 副作用（900s / 15min）：每秒 -0.1 饱食/水分（在前 15 分钟内与增益并存）
/// </summary>
public sealed class MildronateEffectController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Delay,       // 1s 生效延迟
        Buff,        // 1500s 耐力增益 + 前 900s 吃喝消耗
    }

    internal const float ActivationDelay = 1f;
    internal const float BuffDuration = 1500f;              // 25 分钟
    internal const float DrainDuration = 900f;              // 15 分钟吃喝消耗
    internal const float StaminaCapBonus = 1.10f;           // 耐力上限 +10%
    internal const float StaminaRecoveryBoost = 0.50f;     // 耐力恢复 +50%（额外恢复比例）
    internal const float FibrillationReduction = 0.20f;     // 纤颤进度 -20%
    internal const float FoodWaterDrainPerSec = 0.1f;       // 每秒消耗饱食/水分

    private Body? _body;
    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _drainTimer;
    private float _drainAccumulator;
    private float _staminaCapBaseline;  // 追踪观测到的峰值耐力，用于计算 +10% 上限
    private bool _drainActive;

    public static MildronateEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<MildronateEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<MildronateEffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;
        if (isRefresh)
            StimBuffIndicator.ShowOneTimeEffect(MildronateItemSystem.ItemKey, I18n.Tr("mildronate.ot.0"));

        // 心脏纤颤进度 -20%（可叠加）
        ReduceFibrillation();
        StimBuffIndicator.ShowOneTimeEffect(MildronateItemSystem.ItemKey, I18n.Tr("mildronate.ot.1"));

        _phase = Phase.Delay;
        _phaseTimer = ActivationDelay;
        _drainTimer = 0f;
        _drainAccumulator = 0f;
        _drainActive = false;
        enabled = true;
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _phase == Phase.Idle)
        {
            StimBuffIndicator.HideBuff(MildronateItemSystem.ItemKey);
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
            MildronateItemSystem.ItemKey,
            I18n.Tr("mildronate.buff"),
            TryGetMildronateIcon(),
            _phaseTimer + BuffDuration,
            BuffDuration + ActivationDelay,
            new Color(0.8f, 0.4f, 0.1f), // 橙色（战斗兴奋剂）
            positiveDescs: Array.Empty<string>(),
            negativeDescs: Array.Empty<string>());

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Buff;
            _phaseTimer = BuffDuration;
            _drainTimer = DrainDuration;
            _drainAccumulator = 0f;
            _drainActive = true;

            // 以当前耐力值为基准，追踪峰值以计算 +20% 上限
            _staminaCapBaseline = _body!.stamina;

            // 注册耐力恢复和耐力上限加成到多来源叠加管理器
            StaminaBonusManager.AddBonus(_body, StaminaRecoveryBoost, BuffDuration, MildronateItemSystem.ItemKey);
            StaminaCapBonusManager.AddBonus(_body, StaminaCapBonus - 1f, BuffDuration, MildronateItemSystem.ItemKey);

            Plugin.Log.LogInfo($"[Mildronate] Buff phase: stamina cap +{(StaminaCapBonus-1f)*100}% (baseline={_staminaCapBaseline:F1}), "
                + $"stamina recovery +50% for {BuffDuration}s, "
                + $"food/water drain {FoodWaterDrainPerSec}/s for {DrainDuration}s");
        }
    }

    private void UpdateBuff()
    {
        var dt = Time.deltaTime;
        _phaseTimer -= dt;

        // ===== 耐力操纵：每帧直接操作 Body.stamina =====
        // 仅当本物品是当前最强来源时才执行，避免多来源重复追加

        // 1. 耐力上限 +10%：先追踪原生峰值（在额外恢复之前），再 clamp。
        //    注意顺序必须在恢复 boost 之前，否则 baseline 会被自己抬高的值污染导致滚雪球。
        if (StaminaCapBonusManager.IsTopSource(_body!, MildronateItemSystem.ItemKey))
        {
            try
            {
                // 持续追踪原生峰值（此时 stamina 尚未被本帧额外恢复抬高）
                if (_body!.stamina > _staminaCapBaseline)
                    _staminaCapBaseline = _body.stamina;

                var effectiveCap = _staminaCapBaseline * StaminaCapBonus;
                if (_body.stamina > effectiveCap)
                    _body.stamina = effectiveCap;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Mildronate] Stamina cap clamp failed: {ex.Message}");
            }
        }

        // 2. 耐力恢复 +50%：使用游戏原生的 staminaStrength 曲线计算额外恢复量。
        //    游戏 HandleCirculation 每帧通过 staminaStrength.Evaluate(energy*0.01) 计算自然恢复，
        //    这里加上其 50% 额外恢复。
        if (StaminaBonusManager.IsTopSource(_body!, MildronateItemSystem.ItemKey))
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
                Plugin.Log.LogWarning($"[Mildronate] Stamina recovery boost failed: {ex.Message}");
            }
        }

        // ===== 前 15 分钟：饱和度和水分消耗 =====
        if (_drainActive)
        {
            _drainTimer -= dt;
            _drainAccumulator += dt;
            while (_drainAccumulator >= 1f)
            {
                _drainAccumulator -= 1f;
                DrainFoodWater();
            }

            if (_drainTimer <= 0f)
            {
                _drainActive = false;
                Plugin.Log.LogInfo("[Mildronate] Food/water drain ended (15 min elapsed).");
            }
        }

        // 更新 buff 显示（消耗期间橙红警告，之后绿色）
        var tintColor = _drainActive
            ? new Color(0.9f, 0.4f, 0.1f)   // 橙红（副作用进行中）
            : new Color(0.5f, 0.85f, 0.3f); // 绿色（纯增益阶段）

        StimBuffIndicator.ShowBuff(
            MildronateItemSystem.ItemKey,
            I18n.Tr("mildronate.buff"),
            TryGetMildronateIcon(),
            _phaseTimer,
            BuffDuration,
            tintColor,
            positiveDescs: I18n.TrAll("mildronate.pos.0", "mildronate.pos.1"),
            negativeDescs: _drainActive ? I18n.TrAll("mildronate.neg.0") : Array.Empty<string>());

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Idle;
            if (_body != null)
            {
                StaminaBonusManager.ClearBonus(_body, MildronateItemSystem.ItemKey);
                StaminaCapBonusManager.ClearBonus(_body, MildronateItemSystem.ItemKey);
            }
            StimBuffIndicator.HideBuff(MildronateItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[Mildronate] Effect ended. Stamina manipulation stopped.");
        }
    }

    /// <summary>
    /// 立即减少心脏纤颤进度 20%。
    /// </summary>
    private void ReduceFibrillation()
    {
        if (_body == null) return;

        try
        {
            var oldProgress = _body.fibrillationProgress;
            _body.fibrillationProgress *= (1f - FibrillationReduction);
            Plugin.Log.LogInfo($"[Mildronate] Fibrillation progress: {oldProgress:F2} → {_body.fibrillationProgress:F2} (-20%)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Mildronate] ReduceFibrillation failed: {ex.Message}");
        }
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
            Plugin.Log.LogWarning($"[Mildronate] DrainFoodWater failed: {ex.Message}");
        }
    }

    private static Sprite? TryGetMildronateIcon()
    {
        var method = typeof(MildronateItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改米屈肼物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class MildronateHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<MildronateItemMarker>();
        if (marker == null) return;
        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
