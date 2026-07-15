using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// SJ12 TGLabs 战斗兴奋剂注射器系统。
/// 效果：体温 -4°C，饱食度和水分每秒 +0.2（最高105），+2 韧性等级，持续 10 分钟。
/// 副作用：立即 +4 患病、体重 -2kg；10 分钟后体温 +4°C 持续 2 分钟。
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

    public static string DisplayName => I18n.Tr("sj12.name");
    public static string Description => I18n.Tr("sj12.desc");

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
        item.SetCondition(1f);

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

        InjectorSound.Play();
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
/// 增益期（600s）：体温偏移 -4°C（相对于环境温度），每秒恢复能量0.2和水分0.2（最高105）。
/// 减益期（120s）：体温偏移 +4°C（相对于环境温度）。
/// 使用瞬间 +4 患病、体重 -2kg。
///
/// 体温系统：
/// - SJ12TemperaturePatch（Harmony Prefix/Postfix on HandleBodyTemperature）
///   临时偏移 ambientTemperature，使游戏原生 lerp 自然向 (ambient + offset) 进行。
///   其他体温机制（建筑加热、衣物隔热等）正常工作，不被覆盖。
/// - LateUpdate 仅做补充性加速推进（TempLerpStrength），加速偏移生效。
///   一旦体温接近偏移目标，补充推进为零，完全由原生系统维护。
/// </summary>
public sealed class SJ12EffectController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Delay,       // 1s 生效延迟
        Buff,        // 600s 降温 + 能量/水分恢复
        Debuff       // 120s 反向升温
    }

    internal const float ActivationDelay = 1f;
    internal const float BuffDuration = 600f;        // 10 分钟
    internal const float DebuffDuration = 120f;      // 2 分钟
    internal const float BuffTempOffset = 4f;        // 降温幅度
    internal const float DebuffTempOffset = 4f;      // 升温幅度
    internal const float TempLerpStrength = 1.5f;
    internal const float EnergyRestorePerSecond = 0.2f;
    internal const float WaterRestorePerSecond = 0.2f;
    internal const float MaxEnergyWater = 105f;
    internal const float SicknessOnUse = 4f;

    internal const float WeightLossOnUse = 6f;           // weightOffset 3:1 比例，6f = 实际 -2kg

    private Body? _body;
    private Phase _phase = Phase.Idle;
    private float _phaseTimer;
    private float _tickAccumulator;
    private float _initialTemp;        // 注射时的体温，Buff/Debuff 都相对此值偏移

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
        bool isRefresh = enabled;

        if (isRefresh)
        {
            StimBuffIndicator.ShowOneTimeEffect(SJ12ItemSystem.ItemKey, I18n.Tr("sj12.ot.0"));
            Plugin.Log.LogInfo("[SJ12] Refresh: positive effects skipped, timer reset.");
        }

        // 立即副作用：+4 患病，体重 -2kg（一次性）
        _body!.sicknessAmount += SicknessOnUse;
        _body.weightOffset -= WeightLossOnUse;
        Plugin.Log.LogInfo($"[SJ12] Applied +{SicknessOnUse} sicknessAmount, -2kg weight (now weightOffset: {_body.weightOffset}).");
        StimBuffIndicator.ShowOneTimeEffect(SJ12ItemSystem.ItemKey, I18n.Tr("sj12.ot.1"));
        StimBuffIndicator.ShowOneTimeEffect(SJ12ItemSystem.ItemKey, I18n.Tr("sj12.ot.2"), isNegative: true);

        // 仅在首次激活时记录基准体温，refresh 时保留原值避免叠加降低
        if (!isRefresh)
            _initialTemp = _body!.temperature;

        _phase = Phase.Delay;
        _phaseTimer = ActivationDelay;
        _tickAccumulator = 0f;
        enabled = true;

        StimBuffIndicator.ShowBuff(
            SJ12ItemSystem.ItemKey,
            I18n.Tr("sj12.buff"),
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
    /// 返回当前体温偏移量（供 SJ12TemperaturePatch 使用）。
    /// Buff: -BuffTempOffset, Debuff: +DebuffTempOffset, 其他: 0
    /// </summary>
    public float GetCurrentTempOffset()
    {
        if (_phase == Phase.Buff) return -BuffTempOffset;
        if (_phase == Phase.Debuff) return DebuffTempOffset;
        return 0f;
    }

    private void LateUpdate()
    {
        if (_body == null || _phase == Phase.Idle || _phase == Phase.Delay) return;

        var offset = GetCurrentTempOffset();
        if (offset == 0f) return;

        // 补充性体温推进：加速偏移生效速度
        // 主体温控制由 SJ12TemperaturePatch 在 HandleBodyTemperature 中处理（偏移 ambientTemperature）
        // 这里仅加速初始过渡，不锁定体温
        var dt = Time.deltaTime;
        var ambient = SJ12TemperaturePatch.GetAmbientTemperature(_body);
        var desired = ambient + offset;

        if (offset < 0f) // Buff: cooling
        {
            // 体温高于目标时加速降温
            var remaining = _body.temperature - desired;
            if (remaining > 0f)
                _body.temperature -= Mathf.Min(remaining, dt * TempLerpStrength);
        }
        else // Debuff: heating
        {
            // 体温低于目标时加速升温
            var remaining = desired - _body.temperature;
            if (remaining > 0f)
                _body.temperature += Mathf.Min(remaining, dt * TempLerpStrength);
        }
    }

    private void UpdateDelay()
    {
        StimBuffIndicator.ShowBuff(
            SJ12ItemSystem.ItemKey,
            I18n.Tr("sj12.buff"),
            TryGetSJ12Icon(),
            _phaseTimer + BuffDuration,
            BuffDuration + ActivationDelay,
            new Color(0.4f, 0.85f, 1f),
            positiveDescs: I18n.TrAll("sj12.pos.0", "sj12.pos.1"),
            negativeDescs: I18n.TrAll("sj12.neg.0"));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Buff;
            _phaseTimer = BuffDuration;
            _tickAccumulator = 0f;

            Plugin.Log.LogInfo($"[SJ12] Buff phase: cooling by {BuffTempOffset}°C from {_initialTemp:F1} for {BuffDuration}s");
        }
    }

    private void UpdateBuff()
    {
        _tickAccumulator += Time.deltaTime;
        while (_tickAccumulator >= 1f)
        {
            _tickAccumulator -= 1f;

            // 饱食度每 tick +0.2，最高105
            _body!.hunger = Mathf.Min(MaxEnergyWater, _body.hunger + EnergyRestorePerSecond);

            // 水分每 tick +0.2，最高105
            _body.thirst = Mathf.Min(MaxEnergyWater, _body.thirst + WaterRestorePerSecond);
        }

        StimBuffIndicator.ShowBuff(
            SJ12ItemSystem.ItemKey,
            I18n.Tr("sj12.buff"),
            TryGetSJ12Icon(),
            _phaseTimer,
            BuffDuration,
            new Color(0.4f, 0.85f, 1f),
            positiveDescs: I18n.TrAll("sj12.pos.0", "sj12.pos.1"),
            negativeDescs: I18n.TrAll("sj12.neg.0"));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Debuff;
            _phaseTimer = DebuffDuration;
            Plugin.Log.LogInfo($"[SJ12] Debuff phase: overheating by {DebuffTempOffset}°C from {_initialTemp:F1} for {DebuffDuration}s");
        }
    }

    private void UpdateDebuff()
    {
        StimBuffIndicator.ShowBuff(
            SJ12ItemSystem.ItemKey,
            I18n.Tr("sj12.buff_side"),
            TryGetSJ12Icon(),
            _phaseTimer,
            DebuffDuration,
            new Color(1f, 0.45f, 0.2f), // 橙红色（过热警告）
            positiveDescs: Array.Empty<string>(),
            negativeDescs: I18n.TrAll("sj12.neg.1"));

        if (_phaseTimer <= 0f)
        {
            _phase = Phase.Idle;
            StimBuffIndicator.HideBuff(SJ12ItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[SJ12] Effect ended. Temperature returning to normal.");
        }
    }

    private void OnDisable()
    {
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

        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}

/// <summary>
/// Harmony 补丁：修改 HandleBodyTemperature 的 lerp 目标，使 SJ12 效果动态影响体温而不锁定。
///
/// 机制：Prefix 临时偏移 ambientTemperature（±SJ12Offset），使原生 lerp 自然向偏移后目标进行；
/// Postfix 立即恢复 ambientTemperature，避免影响其他读取。
/// SJ12 不再在 LateUpdate 中强制覆写体温（仅做补充性加速推进）。
/// 其他体温机制（建筑加热、衣物隔热等）正常工作。
/// </summary>
[HarmonyPatch(typeof(Body), "HandleBodyTemperature")]
public static class SJ12TemperaturePatch
{
    // ambientTemperature 是 Body 的非公开字段/属性，需要反射访问
    private static readonly FieldInfo? _ambientTempField = AccessTools.Field(typeof(Body), "ambientTemperature");
    private static readonly PropertyInfo? _ambientTempProp = AccessTools.Property(typeof(Body), "ambientTemperature");

    private static float? _originalAmbient;

    /// <summary>读取 Body.ambientTemperature（字段或属性）</summary>
    public static float GetAmbientTemperature(Body body)
    {
        if (_ambientTempField != null)
            return (float)_ambientTempField.GetValue(body);
        if (_ambientTempProp != null)
            return (float)_ambientTempProp.GetValue(body);
        // 回退：无法访问时返回当前体温（近似环境温度）
        Plugin.Log.LogWarning("[SJ12TempPatch] Cannot access Body.ambientTemperature, using body.temperature as fallback.");
        return body.temperature;
    }

    /// <summary>写入 Body.ambientTemperature（字段或可写属性）</summary>
    private static bool SetAmbientTemperature(Body body, float value)
    {
        if (_ambientTempField != null)
        {
            _ambientTempField.SetValue(body, value);
            return true;
        }
        if (_ambientTempProp != null && _ambientTempProp.CanWrite)
        {
            _ambientTempProp.SetValue(body, value);
            return true;
        }
        return false;
    }

    [HarmonyPrefix]
    public static void Prefix(Body __instance)
    {
        var sj12 = __instance.GetComponent<SJ12EffectController>();
        if (sj12 == null || !sj12.enabled) return;

        float offset = sj12.GetCurrentTempOffset();
        if (offset == 0f) return; // Delay/Idle phase, no offset

        // 临时偏移 ambientTemperature，使 HandleBodyTemperature lerp 向 (ambient + offset)
        var original = GetAmbientTemperature(__instance);
        if (SetAmbientTemperature(__instance, original + offset))
        {
            _originalAmbient = original;
            Plugin.Log.LogDebug($"[SJ12TempPatch] Prefix: shifted ambient from {original:F1} to {original + offset:F1} (offset={offset:F1})");
        }
        else
        {
            // ambientTemperature 不可写（只读属性），无法通过 Prefix 偏移
            // LateUpdate 将独立处理体温推进
            Plugin.Log.LogDebug("[SJ12TempPatch] ambientTemperature is read-only, Prefix shift skipped. LateUpdate will handle temperature.");
        }
    }

    [HarmonyPostfix]
    public static void Postfix(Body __instance)
    {
        if (!_originalAmbient.HasValue) return;

        // 恢复 ambientTemperature，避免影响同一帧中其他读取
        SetAmbientTemperature(__instance, _originalAmbient.Value);
        _originalAmbient = null;
    }
}
