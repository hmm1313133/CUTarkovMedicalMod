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
/// SJ12 TGLabs 战斗兴奋剂注射器系统。
/// 核心机制：降低角色体温（-4°C），持续恢复能量与水分（"吃喝针"）。
/// 药效结束后有反向升温减益（+6°C 过热）。
///
/// 体温系统（反编译确认）：
/// - Body.temperature（float, public）— 核心体温，正常 37°C
/// - Body.HandleBodyTemperature(Painkillers) — 原生体温管理，lerp 向 ambientTemperature
/// - MoodleManager.AddAllMoodles 读取 Body.temperature：
///     热 >41.5→hot4, >40.25→hot3, >39→hot2, >38→hot1
///     冷 <28→cold4, <32.5→cold3, <34→cold2, <35.5→cold1
/// - PlayerCamera.HandleScreenShaders: _FrostAmount / _OverheatAmount ← body.temperature
/// - Body.Eat(amount, weightGain) / Body.Drink(amount) — 原生能量/水分恢复
///
/// 设计：直接设置 Body.temperature 字段，原生 moodle 和着色器自动响应。
/// </summary>
public static class SJ12ItemSystem
{
    public const string ItemKey = "sj12";
    public const string BaseGameItemId = "syringe";

    public const string DisplayName = "SJ12 TGLabs战斗兴奋剂注射器【SJ12】";
    public const string Description =
        "TGLabs 战斗兴奋剂。通过特殊配方降低体温以提升感知，同时持续补充能量与水分，俗称\"吃喝针\"。\n\n" +
        "<color=#4fc3f7>效果：1秒后生效，持续600秒。体温降至31.5°C（低于热感应侦测阈值），每秒恢复能量0.1、水分0.1。</color>\n" +
        "<color=#ff6666>副作用：药效消退后体温反升至40.5°C，持续180秒，引发过热警告。</color>";

    private static Sprite? _cachedIcon;

    public static bool IsSJ12Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的 SJ12 物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsSJ12Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(100f);

        var marker = item.gameObject.GetComponent<SJ12ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<SJ12ItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "sj12-icon";
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
    /// 在 Item.GlobalItems 注册 SJ12 的 ItemInfo。
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
            clone.category = "drug";
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = true;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(SJ12ItemSystem).GetMethod(
                nameof(SJ12UseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered SJ12 ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register SJ12: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// SJ12 使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// 激活效果控制器，管理降温→恢复→过热减益的完整生命周期。
    /// </summary>
    private static void SJ12UseAction(Body body, Item item)
    {
        Plugin.Log.LogInfo("SJ12 useAction invoked by game native system.");

        SJ12EffectController.Attach(body).ActivateOrRefresh();

        // 消耗物品
        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied SJ12: temperature regulation effect activated.");
    }

    #region Helper Methods

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "drug",
            usable = true,
            usableOnLimb = false,
            usableWithLMB = true,
            combineable = true,
            destroyAtZeroCondition = true,
            scaleWeightWithCondition = false,
            weight = 0.1f,
            tags = "drug,medicine,medical,stim,combine,craft"
        };
        info.SetTags();

        var useMethod = typeof(SJ12ItemSystem).GetMethod(
            nameof(SJ12UseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "sj12.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "sj12.webp");
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
            _cachedIcon.name = "sj12-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load SJ12 icon: {ex.Message}");
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
/// SJ12 物品标记组件。
/// </summary>
public sealed class SJ12ItemMarker : MonoBehaviour
{
    public string itemKey = SJ12ItemSystem.ItemKey;
    public string displayName = SJ12ItemSystem.DisplayName;
    public string description = SJ12ItemSystem.Description;
}

/// <summary>
/// SJ12 效果控制器：
/// 三个阶段管理体温变化，直接设置 Body.temperature 字段。
/// 原生 MoodleManager（cold1-4 / hot1-4）和 PlayerCamera 着色器（_FrostAmount / _OverheatAmount）
/// 会自动读取 Body.temperature 并显示对应效果。
///
/// 阶段设计（基于反编译确认的游戏体温机制）：
/// 1. 延迟期（1s）：等待药效发作
/// 2. 增益期（600s）：体温 lerp 向 31.5°C
///    - 低于炮塔热感应触发线 32°C（TurretScript.Update: temperature > 32 → 侦测玩家）
///    - 触发 cold3 moodle（<32.5）但不至 cold4（<28，危险）
///    - 每秒恢复能量0.1、水分0.1
/// 3. 减益期（180s）：体温 lerp 向 40.5°C
///    - 触发 hot3 moodle（>40.25）警告，但不至 hot4（>41.5，危险）
///    - 不触发脑损伤（>42°C 扣 brainHealth）和失血（>41°C 扣 bloodVolume）
///    - 会增加 wetness 出汗（>37.5°C）
///
/// 体温 lerp 在 LateUpdate 中执行（原生 HandleBody 在 Update 中运行后），
/// 以 Lerp 向目标温度的方式施加药物影响，同时不完全覆盖环境温度的自然变化。
/// </summary>
public sealed class SJ12EffectController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Delay,       // 1s 生效延迟
        Buff,        // 600s 降温 + 能量/水分恢复
        Debuff       // 180s 反向升温
    }

    internal const float ActivationDelay = 1f;       // 生效延迟
    internal const float BuffDuration = 600f;        // 增益持续（10分钟）
    internal const float DebuffDuration = 180f;      // 减益持续（3分钟，缩短避免过热致死）
    internal const float BuffTargetTemp = 31.5f;     // 增益期目标体温（低于炮塔触发线32°C，规避热感应）
    internal const float DebuffTargetTemp = 40.5f;   // 减益期目标体温（hot3警告，不触发脑损伤/失血）
    internal const float NormalTemp = 37f;           // 正常体温
    internal const float TempLerpStrength = 1.5f;    // 体温 lerp 强度（足以维持目标，对抗原生 lerp）
    internal const float EnergyRestorePerSecond = 0.1f;
    internal const float WaterRestorePerSecond = 0.1f;

    private Body? _body;
    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _tickAccumulator;

    public static SJ12EffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<SJ12EffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<SJ12EffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        _phase = Phase.Delay;
        _phaseTimer = ActivationDelay;
        _tickAccumulator = 0f;
        enabled = true;

        StimBuffIndicator.ShowBuff(
            SJ12ItemSystem.ItemKey,
            "SJ12",
            TryGetSJ12Icon(),
            BuffDuration + ActivationDelay,
            BuffDuration + ActivationDelay,
            new Color(0.4f, 0.85f, 1f)); // 冰蓝色（降温）
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _phase == Phase.Idle)
        {
            StimBuffIndicator.HideBuff(SJ12ItemSystem.ItemKey);
            enabled = false;
            return;
        }

        _phaseTimer -= Time.deltaTime;

        switch (_phase)
        {
            case Phase.Delay:
                UpdateDelay();
                break;
            case Phase.Buff:
                UpdateBuff();
                break;
            case Phase.Debuff:
                UpdateDebuff();
                break;
        }
    }

    /// <summary>
    /// 在 LateUpdate 中应用体温 lerp，确保在原生 HandleBody 之后执行。
    /// </summary>
    private void LateUpdate()
    {
        if (_body == null || _phase == Phase.Idle || _phase == Phase.Delay) return;

        var targetTemp = _phase == Phase.Buff ? BuffTargetTemp : DebuffTargetTemp;
        var dt = Time.deltaTime;
        _body.temperature = Mathf.Lerp(
            _body.temperature,
            targetTemp,
            Mathf.Clamp01(dt * TempLerpStrength));
    }

    private void UpdateDelay()
    {
        // 延迟期显示 buff 图标（倒计时包含延迟）
        StimBuffIndicator.ShowBuff(
            SJ12ItemSystem.ItemKey,
            "SJ12",
            TryGetSJ12Icon(),
            _phaseTimer + BuffDuration,
            BuffDuration + ActivationDelay,
            new Color(0.4f, 0.85f, 1f));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Buff;
            _phaseTimer = BuffDuration;
            _tickAccumulator = 0f;
            Plugin.Log.LogInfo($"[SJ12] Buff phase started: cooling to {BuffTargetTemp}°C for {BuffDuration}s");
        }
    }

    private void UpdateBuff()
    {
        // 每秒恢复能量与水分（直接改字段，避免 Eat/Drink 副作用）
        _tickAccumulator += Time.deltaTime;
        while (_tickAccumulator >= 1f)
        {
            _tickAccumulator -= 1f;
            _body!.hunger = Mathf.Clamp(_body.hunger + EnergyRestorePerSecond, -50f, 125f);
            _body.thirst = Mathf.Clamp(_body.thirst + WaterRestorePerSecond, -50f, 250f);
        }

        StimBuffIndicator.ShowBuff(
            SJ12ItemSystem.ItemKey,
            "SJ12",
            TryGetSJ12Icon(),
            _phaseTimer,
            BuffDuration,
            new Color(0.4f, 0.85f, 1f));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Debuff;
            _phaseTimer = DebuffDuration;
            Plugin.Log.LogInfo($"[SJ12] Debuff phase started: overheating to {DebuffTargetTemp}°C for {DebuffDuration}s");
        }
    }

    private void UpdateDebuff()
    {
        StimBuffIndicator.ShowBuff(
            SJ12ItemSystem.ItemKey,
            "SJ12(过热)",
            TryGetSJ12Icon(),
            _phaseTimer,
            DebuffDuration,
            new Color(1f, 0.45f, 0.2f)); // 橙红色（过热警告）

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Idle;
            StimBuffIndicator.HideBuff(SJ12ItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[SJ12] Effect ended. Temperature returning to normal.");
        }
    }

    private static Sprite? TryGetSJ12Icon()
    {
        var method = typeof(SJ12ItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改 SJ12 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class SJ12HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<SJ12ItemMarker>();
        if (marker == null) return;

        __result = (marker.displayName, marker.description);
    }
}
