using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// SJ9 定制体温抑制剂注射器系统。
/// 效果：体温锁定在31°C持续20min。
/// 副作用：立即+15患病、韧性永久-2；延迟10min后胸口持续疼痛15、胸口肌肉每秒-0.2持续10min。
/// 用于夜间行动，降低热成像可见度。
/// </summary>
public static class Sj9ItemSystem
{
    public const string ItemKey = "sj9";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("sj9.name");
    public static string Description => I18n.Tr("sj9.desc");

    private static Sprite? _cachedIcon;

    public static bool IsSj9Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsSj9Request(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<Sj9ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<Sj9ItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "sj9-icon";
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
            clone.value = 16;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,stim,combine,craft");
            clone.SetTags();

            var useMethod = typeof(Sj9ItemSystem).GetMethod(
                nameof(Sj9UseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered SJ9 ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register SJ9: {ex.Message}");
            return false;
        }
    }

    private static void Sj9UseAction(Body body, Item item)
    {

        InjectorSound.Play();
        Plugin.Log.LogInfo("SJ9 useAction invoked.");

        Sj9EffectController.Attach(body).Activate();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("SJ9 injected — temperature lock + stealth mode active (20min).");
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
            value = 16,
            tags = "drug,medicine,stim,combine,craft",
            rec = new Recognition(13)
        };
        info.SetTags();

        var useMethod = typeof(Sj9ItemSystem).GetMethod(
            nameof(Sj9UseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "sj9.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "sj9.webp");
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
            _cachedIcon.name = "sj9-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load SJ9 icon: {ex.Message}");
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
/// SJ9 物品标记组件。
/// </summary>
public sealed class Sj9ItemMarker : MonoBehaviour
{
    public string itemKey = Sj9ItemSystem.ItemKey;
    public string displayName = Sj9ItemSystem.DisplayName;
    public string description = Sj9ItemSystem.Description;
}

/// <summary>
/// SJ9 效果控制器：
/// 体温锁定 31°C 持续 20min（1200s）。
/// 延迟 10min 后：胸口持续疼痛 15、胸口肌肉每秒 -0.2 持续 10min。
/// </summary>
public sealed class Sj9EffectController : MonoBehaviour
{
    internal const float TotalDuration = 1200f;              // 20 分钟
    internal const float SideEffectDelay = 600f;             // 10 分钟延迟
    internal const float SideEffectDuration = 600f;          // 10 分钟
    internal const float TargetTemperature = 31f;
    internal const float SicknessOnUse = 15f;                // 立即患病+15
    internal const int ResLevelPenalty = -2;                 // 韧性永久-2
    internal const float ChestPainOnDelay = 15f;             // 胸口疼痛+15
    internal const float ChestMuscleDrainPerSec = 0.2f;      // 胸口肌肉每秒-0.2

    private Body? _body;
    private float _timer;
    private float _sideEffectTimer;
    private float _tickAccumulator;
    private bool _sideEffectStarted;
    private Limb? _chestLimb;

    public static Sj9EffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<Sj9EffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<Sj9EffectController>();
        controller._body = body;
        return controller;
    }

    public void Activate()
    {
        bool isRefresh = enabled;

        if (isRefresh)
        {
            StimBuffIndicator.ShowOneTimeEffect(Sj9ItemSystem.ItemKey, I18n.Tr("sj9.ot.0"));
            Plugin.Log.LogInfo("[SJ9] Refresh: timer reset, negatives re-trigger.");
        }

        // 立即副作用（每次注射都触发）
        _body!.sicknessAmount += SicknessOnUse;
        SkillEffectHelper.AdjustLevel(_body, SkillEffectHelper.StatRES, ResLevelPenalty);
        StimBuffIndicator.ShowOneTimeEffect(Sj9ItemSystem.ItemKey, I18n.Tr("sj9.ot.1"), isNegative: true);
        StimBuffIndicator.ShowOneTimeEffect(Sj9ItemSystem.ItemKey, I18n.Tr("sj9.ot.2"), isNegative: true);
        Plugin.Log.LogInfo($"[SJ9] Immediate: sickness +{SicknessOnUse} (now {_body.sicknessAmount}), RES {ResLevelPenalty} permanent.");

        _timer = TotalDuration;
        _sideEffectTimer = SideEffectDelay;
        _sideEffectStarted = false;
        _tickAccumulator = 0f;

        // 查找胸口肢体
        _chestLimb = FindChestLimb(_body);

        enabled = true;

        StimBuffIndicator.ShowBuff(
            Sj9ItemSystem.ItemKey,
            I18n.Tr("sj9.buff"),
            TryGetSj9Icon(),
            _timer,
            TotalDuration,
            new Color(0.2f, 0.6f, 0.9f), // 冰蓝色
            positiveDescs: I18n.TrAll("sj9.pos.0"),
            negativeDescs: Array.Empty<string>());
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

        if (_timer <= 0f)
        {
            Cleanup();
            return;
        }

        // 锁定体温
        _body.temperature = TargetTemperature;

        // 检查是否进入副作用阶段
        if (!_sideEffectStarted)
        {
            UpdateBuffOnly();
        }
        else
        {
            UpdateSideEffect();
        }
    }

    private void UpdateBuffOnly()
    {
        _sideEffectTimer -= Time.deltaTime;

        StimBuffIndicator.ShowBuff(
            Sj9ItemSystem.ItemKey,
            I18n.Tr("sj9.buff"),
            TryGetSj9Icon(),
            _timer,
            TotalDuration,
            new Color(0.2f, 0.6f, 0.9f),
            positiveDescs: I18n.TrAll("sj9.pos.0"),
            negativeDescs: Array.Empty<string>());

        if (_sideEffectTimer <= 0f)
        {
            _sideEffectStarted = true;
            _sideEffectTimer = SideEffectDuration;
            _tickAccumulator = 0f;
            Plugin.Log.LogInfo($"[SJ9] Side effect phase: chest persistent pain {ChestPainOnDelay}, muscle -{ChestMuscleDrainPerSec}/s for {SideEffectDuration}s.");
        }
    }

    private void UpdateSideEffect()
    {
        StimBuffIndicator.ShowBuff(
            Sj9ItemSystem.ItemKey,
            I18n.Tr("sj9.buff"),
            TryGetSj9Icon(),
            _timer,
            TotalDuration,
            new Color(0.9f, 0.4f, 0.2f), // 橙红色（副作用阶段）
            positiveDescs: Array.Empty<string>(),
            negativeDescs: I18n.TrAll("sj9.neg.0", "sj9.neg.1"));

        // 副作用计时器到期后停止肌肉损伤，仅维持体温锁定
        if (_sideEffectTimer <= 0f)
            return;

        _sideEffectTimer -= Time.deltaTime;

        // 延迟初始化 _chestLimb（Eff.Res 恢复后可能为 null）
        if (_chestLimb == null && _body != null)
            _chestLimb = FindChestLimb(_body);

        // 每秒胸口肌肉 -0.2 + 持续疼痛维持在 15
        _tickAccumulator += Time.deltaTime;
        while (_tickAccumulator >= 1f)
        {
            _tickAccumulator -= 1f;

            if (_chestLimb != null && !_chestLimb.dismembered)
            {
                _chestLimb.muscleHealth = Mathf.Max(0f, _chestLimb.muscleHealth - ChestMuscleDrainPerSec);
                _chestLimb.pain = Mathf.Max(ChestPainOnDelay, _chestLimb.pain);
            }
        }

        if (_sideEffectTimer <= 0f)
        {
            Plugin.Log.LogInfo("[SJ9] Side effect phase ended.");
        }
    }

    private void Cleanup()
    {
        StimBuffIndicator.HideBuff(Sj9ItemSystem.ItemKey);
        enabled = false;
        Plugin.Log.LogInfo("[SJ9] Effect ended.");
    }

    /// <summary>
    /// 查找胸口肢体：优先使用 Body.LimbByName("chest")，回退到 limbs[1]。
    /// </summary>
    private static Limb? FindChestLimb(Body body)
    {
        try
        {
            var method = typeof(Body).GetMethod("LimbByName",
                BindingFlags.Public | BindingFlags.Instance);
            if (method != null)
            {
                var result = method.Invoke(body, new object[] { "chest" }) as Limb;
                if (result != null) return result;

                result = method.Invoke(body, new object[] { "torso" }) as Limb;
                if (result != null) return result;
            }
        }
        catch { }

        // 回退：limbs[1] 经反编译分析为胸部
        var limbs = body.limbs;
        if (limbs != null && limbs.Length > 1)
            return limbs[1];

        return null;
    }

    private static Sprite? TryGetSj9Icon()
    {
        var method = typeof(Sj9ItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改 SJ9 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class Sj9HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<Sj9ItemMarker>();
        if (marker == null) return;
            if (item.Stats?.rec == null || !item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
