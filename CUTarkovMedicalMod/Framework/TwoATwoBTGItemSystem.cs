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
/// 2A2-(b-TG) 兴奋剂注射器系统。
/// 效果：负重上限 +7u 持续 20min；心情立即 +5。
/// 副作用：每秒 -0.1 水分，持续 15min。
/// </summary>
public static class TwoATwoBTGItemSystem
{
    public const string ItemKey = "2a2btg";
    public const string BaseGameItemId = "syringe";

    public const string DisplayName = "2A2-(b-TG)兴奋剂注射器【2A2-(b-TG)】";
    public const string Description =
        "此产品项目旨在创造一种能在远离物资供应点进行侦查或转移时，依然提供战斗支撑的药物。同时，这款产品需要能够进行手工生产。它可以使中枢神经系统长期保持稳定，帮助战斗人员接触不同环境，并提高负重能力。使用此产品时，脱水速度将会提高。写着'TerraGroup 实验室开发'。\n\n" +
        "<color=#54ff9f>效果：负重上限+7u（可与其他针剂叠加） 持续20分钟；心情立即+5。</color>\n" +
        "<color=#ff6666>副作用：每秒-0.1水分 持续15分钟。</color>";

    private static Sprite? _cachedIcon;

    public static bool IsTwoATwoBTGRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsTwoATwoBTGRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<TwoATwoBTGItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<TwoATwoBTGItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "2a2btg-icon";
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
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(TwoATwoBTGItemSystem).GetMethod(
                nameof(TwoATwoBTGUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered 2A2-(b-TG) ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register 2A2-(b-TG): {ex.Message}");
            return false;
        }
    }

    private static void TwoATwoBTGUseAction(Body body, Item item)
    {
        InjectorSound.Play();
        Plugin.Log.LogInfo("2A2-(b-TG) useAction invoked by game native system.");

        TwoATwoBTGEffectController.Attach(body).ActivateOrRefresh();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied 2A2-(b-TG): carry weight +7, mood +5, water drain -0.1/s activated.");
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

        var useMethod = typeof(TwoATwoBTGItemSystem).GetMethod(
            nameof(TwoATwoBTGUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "2a2btg.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "2a2btg.webp");
                if (!File.Exists(iconPath)) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "2a2btg-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load 2A2-(b-TG) icon: {ex.Message}");
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
/// 2A2-(b-TG) 物品标记组件。
/// </summary>
public sealed class TwoATwoBTGItemMarker : MonoBehaviour
{
    public string itemKey = TwoATwoBTGItemSystem.ItemKey;
    public string displayName = TwoATwoBTGItemSystem.DisplayName;
    public string description = TwoATwoBTGItemSystem.Description;
}

/// <summary>
/// 2A2-(b-TG) 效果控制器：
/// 增益期（1200s / 20min）：负重上限 +7u
/// 副作用（900s / 15min）：每秒 -0.1 水分（前 15 分钟与增益并存）
/// 立即：心情 +5
/// </summary>
public sealed class TwoATwoBTGEffectController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Delay,       // 1s 生效延迟
        Buff,        // 1200s 负重增益 + 前 900s 水分消耗
    }

    internal const float ActivationDelay = 1f;
    internal const float BuffDuration = 1200f;              // 20 分钟
    internal const float DrainDuration = 900f;              // 15 分钟水分消耗
    internal const float CarryWeightBonus = 7f;             // 负重上限 +7u
    internal const float MoodBoost = 5f;                    // 心情 +5
    internal const float WaterDrainPerSec = 0.1f;           // 每秒消耗水分

    private Body? _body;
    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _drainTimer;
    private float _drainAccumulator;
    private bool _drainActive;

    internal static TwoATwoBTGEffectController? ActiveInstance;
    internal bool IsCarryWeightActive => _phase == Phase.Buff;

    public static TwoATwoBTGEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<TwoATwoBTGEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<TwoATwoBTGEffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;
        if (isRefresh)
            StimBuffIndicator.ShowOneTimeEffect(TwoATwoBTGItemSystem.ItemKey, "二次注射 计时器已刷新");

        // 心情 +5（可叠加）
        BoostMood();

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
            StimBuffIndicator.HideBuff(TwoATwoBTGItemSystem.ItemKey);
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
            TwoATwoBTGItemSystem.ItemKey,
            "2A2-(b-TG)",
            TryGetIcon(),
            _phaseTimer + BuffDuration,
            BuffDuration + ActivationDelay,
            new Color(0.2f, 0.6f, 0.5f), // 青绿色（负重+适应）
            positiveDescs: new[] { "负重上限+7u" },
            negativeDescs: Array.Empty<string>());

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Buff;
            _phaseTimer = BuffDuration;
            _drainTimer = DrainDuration;
            _drainAccumulator = 0f;
            _drainActive = true;
            ActiveInstance = this;

            Plugin.Log.LogInfo($"[2A2-(b-TG)] Buff phase: carry weight +{CarryWeightBonus}u for {BuffDuration}s, " 
                + $"water drain {WaterDrainPerSec}/s for {DrainDuration}s");
        }
    }

    private void UpdateBuff()
    {
        var dt = Time.deltaTime;
        _phaseTimer -= dt;

        // 负重上限 +7u 由 Harmony Postfix 在 HandlePeriodicChecks 中处理

        // ===== 前 15 分钟：水分消耗 =====
        if (_drainActive)
        {
            _drainTimer -= dt;
            _drainAccumulator += dt;
            while (_drainAccumulator >= 1f)
            {
                _drainAccumulator -= 1f;
                DrainWater();
            }

            if (_drainTimer <= 0f)
            {
                _drainActive = false;
                Plugin.Log.LogInfo("[2A2-(b-TG)] Water drain ended (15 min elapsed).");
            }
        }

        // 更新 buff 显示（消耗期间橙色，之后绿色）
        var tintColor = _drainActive
            ? new Color(0.9f, 0.4f, 0.1f)   // 橙色（副作用进行中）
            : new Color(0.2f, 0.6f, 0.5f);   // 青绿色（纯增益）

        StimBuffIndicator.ShowBuff(
            TwoATwoBTGItemSystem.ItemKey,
            "2A2-(b-TG)",
            TryGetIcon(),
            _phaseTimer,
            BuffDuration,
            tintColor,
            positiveDescs: new[] { "负重上限+7u" },
            negativeDescs: _drainActive ? new[] { "每秒-0.1水分" } : Array.Empty<string>());

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Idle;
            ActiveInstance = null;
            StimBuffIndicator.HideBuff(TwoATwoBTGItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[2A2-(b-TG)] Effect ended.");
        }
    }

    /// <summary>
    /// 立即提升心情 +5。
    /// </summary>
    private void BoostMood()
    {
        if (_body == null) return;

        try
        {
            _body.happiness = Mathf.Min(100f, _body.happiness + MoodBoost);
            Plugin.Log.LogInfo($"[2A2-(b-TG)] Mood boosted by +{MoodBoost} (now {_body.happiness:F1}).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[2A2-(b-TG)] Mood boost failed: {ex.Message}");
        }

        StimBuffIndicator.ShowOneTimeEffect(TwoATwoBTGItemSystem.ItemKey, $"心情+{MoodBoost}");
    }

    /// <summary>
    /// 每秒扣除水分。
    /// </summary>
    private void DrainWater()
    {
        if (_body == null) return;

        try
        {
            _body.thirst = Mathf.Max(0f, _body.thirst - WaterDrainPerSec);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[2A2-(b-TG)] DrainWater failed: {ex.Message}");
        }
    }

    private static Sprite? TryGetIcon()
    {
        var method = typeof(TwoATwoBTGItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改 2A2-(b-TG) 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class TwoATwoBTGHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<TwoATwoBTGItemMarker>();
        if (marker == null) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}

// 负重加成已合并到 MuleItemSystem.MuleEncumberancePatch.GetEncumberanceBonus() 中
