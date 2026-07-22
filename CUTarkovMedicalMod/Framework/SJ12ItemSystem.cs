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
/// 效果：体温 -4°C，饱食度和水分每秒 +0.2（最高105），持续 10 分钟。
/// 副作用：立即 +4 患病、体重 -2kg；10 分钟后体温 +4°C 持续 2 分钟。
///
/// 体温系统（反编译确认）：
/// - Body.temperature（float, public）— 核心体温，正常 37°C
/// - Body.HandleBodyTemperature(Painkillers) — 原生体温管理，每1秒执行：
///     temperature = Mathf.Lerp(temperature, WorldGeneration.world.ambientTemperature, 0.003 / insulation)
///     + 代谢产热（temp < 36.5 时 +Max(hunger*0.01, 0.3)*0.03*recovery）
///     + 恢复（+0.04*recovery）
///     - 湿度散热（-wetness*0.001）
/// - SJ12 在 Update 中直接修改 body.temperature，与原生 lerp 并行博弈。
/// - WorldGeneration.world.ambientTemperature（float, public field）— 世界环境温度
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

    internal static Sprite? TryLoadIcon()
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
/// 增益期（600s）：体温每秒 -0.05°C（下限 = 扎针时体温 - 4°C），每秒恢复能量0.2和水分0.2（最高105）。
/// 减益期（120s）：体温每秒 +0.05°C（上限 = 原始体温 + 4°C）。
/// 使用瞬间 +4 患病、体重 -2kg。
///
/// 体温系统：直接在 Update 中修改 body.temperature，与原生 lerp 并行运行。
/// 原生系统持续将体温拉向环境温度，SJ12 持续拉向目标值，两者博弈。
/// 玩家可通过提高环境温度（火堆、加热器等）对抗 SJ12 降温。
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
    internal const float BuffTempOffset = 4f;        // 降温幅度（下限 = 初始体温 - 4）
    internal const float DebuffTempOffset = 4f;      // 升温幅度（上限 = 降温目标体温 + 4）
    internal const float TempChangePerSecond = 0.05f; // 每秒体温变化量
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
    /// 返回当前体温偏移量。
    /// Buff: -BuffTempOffset, Debuff: +DebuffTempOffset, 其他: 0
    /// </summary>
    public float GetCurrentTempOffset()
    {
        if (_phase == Phase.Buff) return -BuffTempOffset;
        if (_phase == Phase.Debuff) return DebuffTempOffset;
        return 0f;
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
        // 体温每秒 -0.05°C，下限 = 初始体温 - 4°C
        var dt = Time.deltaTime;
        var minTemp = _initialTemp - BuffTempOffset;
        _body!.temperature = Mathf.Max(minTemp, _body.temperature - TempChangePerSecond * dt);

        _tickAccumulator += dt;
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
        // 体温每秒 +0.15°C，上限 = 原始体温 + 4°C
        var dt = Time.deltaTime;
        var maxTemp = _initialTemp + DebuffTempOffset;
        _body!.temperature = Mathf.Min(maxTemp, _body.temperature + 0.15f * dt);

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

    private static Sprite? _iconSprite;
        private static Sprite? TryGetSJ12Icon()
    {
        if (_iconSprite != null) return _iconSprite; var method = typeof(SJ12ItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return _iconSprite = method?.Invoke(null, null) as Sprite;
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

        if (item.Stats?.rec == null || !item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
