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
/// 人造血（蓝血）注射器系统。
/// 效果：立即止住并预防所有出血2分钟；一次性毒素-70%；一次性辐射-10gy。
/// 副作用：延迟3分钟后免疫力-40%、33%概率呕吐、每秒-0.3饱食度持续1分钟。
/// </summary>
public static class BluebloodItemSystem
{
    public const string ItemKey = "blueblood";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("blueblood.name");
    public static string Description => I18n.Tr("blueblood.desc");

    private static Sprite? _cachedIcon;

    public static bool IsBluebloodRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsBluebloodRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<BluebloodItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<BluebloodItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "blueblood-icon";
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

            var useMethod = typeof(BluebloodItemSystem).GetMethod(
                nameof(BluebloodUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Blueblood ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Blueblood: {ex.Message}");
            return false;
        }
    }

    private static void BluebloodUseAction(Body body, Item item)
    {
        InjectorSound.Play();
        Plugin.Log.LogInfo("Blueblood useAction invoked.");

        BluebloodEffectController.Attach(body).Activate();

        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Blueblood injected — bleeding prevention + detox + anti-rad active.");
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

        var useMethod = typeof(BluebloodItemSystem).GetMethod(
            nameof(BluebloodUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "blueblood.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "blueblood.webp");
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
            _cachedIcon.name = "blueblood-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Blueblood icon: {ex.Message}");
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
/// 人造血物品标记组件。
/// </summary>
public sealed class BluebloodItemMarker : MonoBehaviour
{
    public string itemKey = BluebloodItemSystem.ItemKey;
    public string displayName = BluebloodItemSystem.DisplayName;
    public string description = BluebloodItemSystem.Description;
}

/// <summary>
/// 人造血效果控制器：
/// 0-120s：预防所有出血（每帧 blockedBleeding = true）。
/// 延迟 180s 后：免疫力 -40%、33% 概率呕吐、每秒 -0.3 饱食度持续 60s。
/// </summary>
public sealed class BluebloodEffectController : MonoBehaviour
{
    internal const float BleedingPreventionDuration = 120f;       // 2 分钟
    internal const float SideEffectDelay = 180f;                  // 3 分钟延迟
    internal const float SideEffectDuration = 60f;                // 1 分钟
    internal const float ToxinReducePct = 0.7f;                   // 毒素 -70%
    internal const float RadiationReduce = 33f;                   // 辐射 -10gy（内部单位~3.3:1 换算）
    internal const float ImmunityReduceAmount = 40f;               // 免疫力 -40（百分比单位）
    internal const float VomitChance = 0.33f;                     // 33% 呕吐概率
    internal const float HungerDrainPerSec = 0.3f;                // 每秒 -0.3 饱食度

    internal const float TotalDuration = SideEffectDelay + SideEffectDuration; // 240s

    private Body? _body;
    private float _timer;
    private float _sideEffectDelayTimer;
    private float _sideEffectRemaining;
    private float _tickAccumulator;
    private bool _sideEffectStarted;
    private bool _bleedingPhase;

    public static BluebloodEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<BluebloodEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<BluebloodEffectController>();
        controller._body = body;
        return controller;
    }

    public void Activate()
    {
        bool isRefresh = enabled;
        if (isRefresh)
            StimBuffIndicator.ShowOneTimeEffect(BluebloodItemSystem.ItemKey, I18n.Tr("blueblood.ot.0"));

        // === 立即正面效果（可叠加）===
        // 1) 立即止住所有出血
        StopAllBleeding(_body!);

        // 2) 一次性毒素 -70%
        _body!.venomCurrent *= (1f - ToxinReducePct);
        _body.venomTotal *= (1f - ToxinReducePct);
        Plugin.Log.LogInfo($"[Blueblood] Toxin -{(int)(ToxinReducePct * 100)}%: venomCurrent={_body.venomCurrent:F1}, venomTotal={_body.venomTotal:F1}.");

        // 3) 一次性辐射 -10gy
        _body.radiationSickness = Mathf.Max(0f, _body.radiationSickness - RadiationReduce);
        Plugin.Log.LogInfo($"[Blueblood] Radiation -{RadiationReduce}gy (now {_body.radiationSickness:F1}).");

        StimBuffIndicator.ShowOneTimeEffect(BluebloodItemSystem.ItemKey, I18n.Tr("blueblood.ot.1"));
        StimBuffIndicator.ShowOneTimeEffect(BluebloodItemSystem.ItemKey, I18n.Tr("blueblood.ot.2"));

        _timer = TotalDuration;
        _sideEffectDelayTimer = SideEffectDelay;
        _sideEffectRemaining = 0f;
        _tickAccumulator = 0f;
        _sideEffectStarted = false;
        _bleedingPhase = true;

        enabled = true;

        StimBuffIndicator.ShowBuff(
            BluebloodItemSystem.ItemKey,
            I18n.Tr("blueblood.buff"),
            TryGetBluebloodIcon(),
            _timer,
            TotalDuration,
            new Color(0.15f, 0.4f, 0.9f), // 深蓝色（人造血）
            positiveDescs: I18n.TrAll("blueblood.pos.0"),
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

        // 出血预防阶段：每帧保持 blockedBleeding
        if (_bleedingPhase)
        {
            var limbs = _body.limbs;
            if (limbs != null)
            {
                foreach (var limb in limbs)
                {
                    if (limb == null || limb.dismembered) continue;
                    limb.blockedBleeding = true;
                }
            }

            // 检查出血预防阶段是否结束
            if (_timer <= TotalDuration - BleedingPreventionDuration)
            {
                _bleedingPhase = false;
                Plugin.Log.LogInfo("[Blueblood] Bleeding prevention phase ended.");
            }
        }

        // 副作用阶段
        if (!_sideEffectStarted)
        {
            _sideEffectDelayTimer -= Time.deltaTime;

            StimBuffIndicator.ShowBuff(
                BluebloodItemSystem.ItemKey,
                I18n.Tr("blueblood.buff"),
                TryGetBluebloodIcon(),
                _timer,
                TotalDuration,
                new Color(0.15f, 0.4f, 0.9f), // 深蓝色
                positiveDescs: I18n.TrAll("blueblood.pos.0"),
                negativeDescs: Array.Empty<string>());

            if (_sideEffectDelayTimer <= 0f)
            {
                StartSideEffects();
            }
        }
        else
        {
            _sideEffectRemaining -= Time.deltaTime;

            StimBuffIndicator.ShowBuff(
                BluebloodItemSystem.ItemKey,
                I18n.Tr("blueblood.buff"),
                TryGetBluebloodIcon(),
                _timer,
                TotalDuration,
                new Color(0.9f, 0.4f, 0.2f), // 橙红色（副作用阶段）
                positiveDescs: Array.Empty<string>(),
                negativeDescs: I18n.TrAll("blueblood.neg.0", "blueblood.neg.1", "blueblood.neg.2"));

            // 每秒扣除饱食度
            _tickAccumulator += Time.deltaTime;
            while (_tickAccumulator >= 1f && _sideEffectRemaining > 0f)
            {
                _tickAccumulator -= 1f;
                _body.hunger = Mathf.Max(0f, _body.hunger - HungerDrainPerSec);
            }
        }
    }

    private void StartSideEffects()
    {
        _sideEffectStarted = true;
        _sideEffectRemaining = SideEffectDuration;
        _tickAccumulator = 0f;

        // 免疫力 -40（百分比单位，通过 ImmunityReductionManager 持续应用，持续整个副作用阶段）
        try
        {
            ImmunityReductionManager.AddReduction(_body!, ImmunityReduceAmount, SideEffectDuration);
            Plugin.Log.LogInfo($"[Blueblood] Immunity -{ImmunityReduceAmount} for {SideEffectDuration}s (via ImmunityReductionManager).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Blueblood] Failed to reduce immunity: {ex.Message}");
        }

        // 33% 概率呕吐
        if (UnityEngine.Random.value < VomitChance)
        {
            try
            {
                if (_body!.vomiter != null)
                {
                    _body.vomiter.Vomit();
                    Plugin.Log.LogInfo("[Blueblood] Vomiting triggered (33% chance).");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Blueblood] Failed to trigger vomit: {ex.Message}");
            }
        }
        else
        {
            Plugin.Log.LogInfo("[Blueblood] Vomit not triggered (67% rolled).");
        }

        Plugin.Log.LogInfo($"[Blueblood] Side effect phase started: hunger -{HungerDrainPerSec}/s for {SideEffectDuration}s.");
    }

    private void Cleanup()
    {
        // 清除免疫力降低（安全措施，即使定时器未到期也确保恢复）
        if (_body != null)
            ImmunityReductionManager.ClearReductions(_body);

        StimBuffIndicator.HideBuff(BluebloodItemSystem.ItemKey);
        enabled = false;
        Plugin.Log.LogInfo("[Blueblood] Effect ended.");
    }

    /// <summary>
    /// 立即止住所有出血（外部出血 + 内出血 + 血胸）。
    /// </summary>
    private static void StopAllBleeding(Body body)
    {
        var limbs = body.limbs;
        if (limbs != null)
        {
            foreach (var limb in limbs)
            {
                if (limb == null || limb.dismembered) continue;
                limb.bleedAmount = 0f;
                limb.blockedBleeding = true;
            }
        }

        body.internalBleeding = 0f;
        body.hemothorax = 0f;

        Plugin.Log.LogInfo("[Blueblood] All bleeding stopped.");
    }

    private static Sprite? TryGetBluebloodIcon()
    {
        var method = typeof(BluebloodItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 修改人造血物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class BluebloodHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<BluebloodItemMarker>();
        if (marker == null) return;
            if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
