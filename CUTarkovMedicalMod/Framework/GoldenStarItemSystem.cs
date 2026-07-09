using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 金星药膏系统。
/// 越南金星药膏，由樟脑、薄荷醇、薄荷、白千层等植物提炼而成。
/// 液体药膏，容器 10ml，每次使用 2ml（5 次）。
/// 效果：使用部位消毒 30 秒。
/// 副作用：表皮 -5；心情 -5；5 秒后止痛 15 秒（疼痛降至 10%）；意识清醒度锁定 ≤65 持续 10 秒。
/// 模板：基于原版 reliefcream（LiquidItemInfo + ApplyToLimb）。
/// </summary>
public static class GoldenStarItemSystem
{
    public const string ItemKey = "goldenstar";
    public const string BaseGameItemId = "bruisekit";
    public const string LiquidId = "goldenstar_liquid";

    public static string DisplayName => I18n.Tr("goldenstar.name");
    public static string Description => I18n.Tr("goldenstar.desc");

    private const float TotalMl = 10f;       // 容器容量
    private const float MlPerUse = 2f;       // 每次使用量

    // 效果常量
    private const float DisinfectDuration = 30f;   // 消毒 30 秒

    // 副作用常量
    private const float SkinDamage = 5f;           // 表皮 -5
    private const float PainReliefDelay = 5f;      // 5 秒后开始止痛
    private const float PainReliefDuration = 15f;  // 止痛持续 15 秒
    private const float PainReliefTarget = 0.1f;   // 疼痛降至原来 10%
    private const float PainReliefLerpRate = 0.15f;// Lerp 速率
    private const float ConcLockDelay = 5f;        // 5 秒后开始锁定
    private const float ConcLockDuration = 10f;    // 锁定持续 10 秒
    private const float ConcLockMax = 65f;         // 意识清醒度上限

    // 纯白色
    internal static readonly Color WhiteColor = new Color(1f, 1f, 1f, 1f);

    private static Sprite? _cachedIcon;

    public static bool IsGoldenStarRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsGoldenStarRequest(request)) return;

        EnsureRegisteredInItemTable();
        EnsureLiquidRegistered();
        InjectMoodleIcon();

        item.id = ItemKey;
        item.SetCondition(1f);

        // 填充 WaterContainerItem
        var wat = item.GetComponent<WaterContainerItem>();
        if (wat != null)
        {
            wat.stack = new List<LiquidStack> { new LiquidStack(LiquidId, TotalMl) };
            wat.UpdateCondition();
        }
        else
        {
            wat = item.gameObject.AddComponent<WaterContainerItem>();
            wat.stack = new List<LiquidStack> { new LiquidStack(LiquidId, TotalMl) };
        }

        var marker = item.gameObject.GetComponent<GoldenStarItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<GoldenStarItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite, 2f);
                if (adjusted != null)
                {
                    adjusted.name = "goldenstar-icon";
                    sr.sprite = adjusted;
                }
                else
                {
                    sr.sprite = icon;
                }
            }
        }

        Plugin.Log.LogInfo($"[GoldenStar] Configured spawned item '{ItemKey}' (condition={item.condition}).");
    }

    public static bool EnsureRegisteredInItemTable()
    {
        try
        {
            EnsureLiquidRegistered();

            var globalItemsField = typeof(Item).GetField("GlobalItems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (globalItemsField == null) return false;

            var map = globalItemsField.GetValue(null) as System.Collections.IDictionary;
            if (map == null) return false;

            if (map.Contains(ItemKey)) return true;

            LiquidItemInfo? clone = null;
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
            clone.weight = 0.2f;
            clone.value = 8;
            clone.usable = false;
            clone.usableOnLimb = true;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            // LiquidItemInfo 字段
            clone.capacity = TotalMl;
            clone.autoFill = false;
            clone.defaultContents = new List<LiquidStack>
            {
                new LiquidStack(LiquidId, TotalMl)
            };

            // useLimbAction：ApplyToLimb 模式（与 reliefcream 一致）
            clone.useAction = null;
            clone.useLimbAction = GoldenStarUseLimbAction;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Golden Star ItemInfo with custom useLimbAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Golden Star: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 肢体使用 — 调用 WaterContainerItem.ApplyToLimb，消耗液体并触发 onHealthUse。
    /// </summary>
    private static void GoldenStarUseLimbAction(Limb limb, Item item)
    {
        try
        {
            EnsureLiquidRegistered();

            var wat = item.GetComponent<WaterContainerItem>();
            if (wat == null)
            {
                Plugin.Log.LogWarning("[GoldenStar] WaterContainerItem not found on item!");
                return;
            }

            // ApplyToLimb 消耗 2ml 液体并调用 onHealthUse(2ml, limb)
            wat.ApplyToLimb(limb, MlPerUse);
            PlayUseSound(item, "vg");
            Plugin.Log.LogInfo($"[GoldenStar] Applied {MlPerUse}ml to limb {limb.name}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GoldenStar] Failed to apply: {ex.Message}");
        }
    }

    private static AudioClip? _cachedUseSound;
    private static string? _cachedUseSoundName;

    private static void PlayUseSound(Item item, string soundName)
    {
        try
        {
            if (_cachedUseSound == null || _cachedUseSoundName != soundName)
            {
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
                var soundPath = Path.Combine(assemblyDir, "Framework", "Assets", $"{soundName}.wav");
                if (File.Exists(soundPath))
                {
                    using var uwr = UnityWebRequestMultimedia.GetAudioClip("file:///" + soundPath, AudioType.WAV);
                    uwr.SendWebRequest();
                    while (!uwr.isDone) { }
                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        _cachedUseSound = DownloadHandlerAudioClip.GetContent(uwr);
                        _cachedUseSoundName = soundName;
                    }
                }
            }
            if (_cachedUseSound != null)
                Sound.Play(_cachedUseSound, item.transform.position, true);
        }
        catch { }
    }

    #region Liquid Registration

    /// <summary>
    /// 注册自定义液体 "goldenstar_liquid" 到 Liquids.Registry。
    /// onHealthUse 回调应用消毒和副作用。
    /// </summary>
    private static void EnsureLiquidRegistered()
    {
        if (Liquids.Registry.ContainsKey(LiquidId)) return;

        Liquids.Registry[LiquidId] = new LiquidType
        {
            localeName = LiquidId,
            color = WhiteColor,
            valuePerLiter = 80f,
            injectable = false,
            injectionSickness = 0f,
            healthUsable = true,
            onHealthUse = delegate(float ml, Limb limb)
            {
                if (limb == null) return;

                // === 立即效果 ===
                // 1) 消毒 30 秒
                limb.SetDisinfect(DisinfectDuration);

                // 2) 表皮健康度 -5
                limb.skinHealth = Mathf.Max(0f, limb.skinHealth - SkinDamage);

                // 3) 心情 -5
                if (limb.body != null)
                    limb.body.happiness = Mathf.Max(-100f, limb.body.happiness - 5f);

                // === 延迟副作用（通过效果控制器处理）===
                GoldenStarEffectController.Attach(limb).Activate();
            }
        };

        Plugin.Log.LogInfo($"[GoldenStar] Registered custom liquid '{LiquidId}' in Liquids.Registry.");
    }

    #endregion

    #region Helper Methods

    private static LiquidItemInfo CreateFallbackItemInfo()
    {
        var info = new LiquidItemInfo
        {
            fullName = DisplayName,
            description = Description,
            category = "ModStim",
            usable = false,
            usableOnLimb = true,
            usableWithLMB = false,
            combineable = true,
            destroyAtZeroCondition = true,
            scaleWeightWithCondition = false,
            weight = 0.2f,
            value = 8,
            tags = "drug,medicine,medical,stim,combine,craft",
            useLimbAction = GoldenStarUseLimbAction,
            capacity = TotalMl,
            autoFill = false,
            defaultContents = new List<LiquidStack>
            {
                new LiquidStack(LiquidId, TotalMl)
            }
        };
        info.SetTags();
        return info;
    }

    private static LiquidItemInfo CloneItemInfo(ItemInfo? source)
    {
        if (source == null) return CreateFallbackItemInfo();

        var clone = new LiquidItemInfo
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
            rec = new Recognition(8),
            qualities = source.qualities,
            capacity = (source is LiquidItemInfo li) ? li.capacity : TotalMl,
            autoFill = (source is LiquidItemInfo li2) ? li2.autoFill : false,
            defaultContents = new List<LiquidStack>()
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

    #endregion

    #region Icon

    private static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var assetDir = Path.Combine(assemblyDir, "Framework", "Assets");
            var iconPath = Path.Combine(assetDir, "goldenstar.png");
            bool found = File.Exists(iconPath);

            if (!found)
            {
                iconPath = Path.Combine(assetDir, "goldenstar.webp");
                found = File.Exists(iconPath);
                if (!found) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return null;

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "goldenstar-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Golden Star icon: {ex.Message}");
            return null;
        }
    }

    private static Sprite? CreateSpriteMatchingBaseSize(Texture2D texture, Sprite? baseSprite, float sizeMultiplier = 1f)
    {
        if (texture == null) return null;
        if (baseSprite == null)
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f / sizeMultiplier);

        var baseRect = baseSprite.rect;
        var basePpu = baseSprite.pixelsPerUnit > 0f ? baseSprite.pixelsPerUnit : 32f;
        var widthScale = baseRect.width > 0f ? texture.width / baseRect.width : 1f;
        var heightScale = baseRect.height > 0f ? texture.height / baseRect.height : 1f;
        var dominantScale = Mathf.Max(1f, Mathf.Max(widthScale, heightScale));
        return Sprite.Create(texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), basePpu * dominantScale / sizeMultiplier);
    }

    public static Sprite? TryGetGoldenStarIcon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetGoldenStarIcon();
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

            if (!icons.ContainsKey(ItemKey))
                icons[ItemKey] = icon;
        }
        catch { }
    }

    #endregion
}

/// <summary>
/// 金星药膏物品标记组件。
/// </summary>
public sealed class GoldenStarItemMarker : MonoBehaviour
{
    public string itemKey = GoldenStarItemSystem.ItemKey;
    public string displayName = GoldenStarItemSystem.DisplayName;
    public string description = GoldenStarItemSystem.Description;
}

/// <summary>
/// 金星药膏效果控制器。
/// 延迟 5 秒后：止痛 15 秒（疼痛降至 10%）+ 意识清醒度锁定 ≤65 持续 10 秒。
/// 疼痛和意识都由游戏每帧更新，需要每帧覆盖。
/// </summary>
public sealed class GoldenStarEffectController : MonoBehaviour
{
    private Limb? _limb;
    private Body? _body;
    private float _painReliefTimer;
    private float _concLockTimer;
    private float _painReliefRemaining;
    private float _concLockRemaining;
    private bool _painReliefActive;
    private bool _concLockActive;

    public static GoldenStarEffectController Attach(Limb limb)
    {
        var controller = limb.gameObject.GetComponent<GoldenStarEffectController>();
        if (controller == null)
            controller = limb.gameObject.AddComponent<GoldenStarEffectController>();
        controller._limb = limb;
        controller._body = limb.body;
        return controller;
    }

    public void Activate()
    {
        _painReliefTimer = 5f;   // 5 秒后开始止痛
        _concLockTimer = 5f;     // 5 秒后开始锁定意识
        _painReliefRemaining = 15f;
        _concLockRemaining = 10f;
        _painReliefActive = false;
        _concLockActive = false;
        enabled = true;

        Plugin.Log.LogInfo("[GoldenStar] Effect controller activated: pain relief in 5s, consciousness lock in 5s.");
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_limb == null || _body == null)
        {
            enabled = false;
            return;
        }

        bool anyActive = false;

        // 止痛延迟计时
        if (!_painReliefActive)
        {
            _painReliefTimer -= Time.deltaTime;
            if (_painReliefTimer <= 0f)
            {
                _painReliefActive = true;
                Plugin.Log.LogInfo("[GoldenStar] Pain relief started (15s).");
            }
        }

        // 止痛效果：每帧将 pain 向 pain*0.1 做 Lerp(15%)
        if (_painReliefActive)
        {
            _painReliefRemaining -= Time.deltaTime;
            if (_painReliefRemaining > 0f && _limb != null)
            {
                _limb.pain = Mathf.Lerp(_limb.pain, _limb.pain * 0.1f, 0.15f);
                anyActive = true;
            }
            else
            {
                _painReliefActive = false;
                Plugin.Log.LogInfo("[GoldenStar] Pain relief ended.");
            }
        }
        else
        {
            anyActive = true; // 还在延迟期
        }

        // 意识锁定延迟计时
        if (!_concLockActive)
        {
            _concLockTimer -= Time.deltaTime;
            if (_concLockTimer <= 0f)
            {
                _concLockActive = true;
                Plugin.Log.LogInfo("[GoldenStar] Consciousness lock started (10s, max 65).");
            }
        }

        // 意识锁定：每帧 clamp consciousness ≤ 65
        if (_concLockActive)
        {
            _concLockRemaining -= Time.deltaTime;
            if (_concLockRemaining > 0f && _body != null)
            {
                if (_body.consciousness > 65f)
                    _body.consciousness = 65f;
                anyActive = true;
            }
            else
            {
                _concLockActive = false;
                Plugin.Log.LogInfo("[GoldenStar] Consciousness lock ended.");
            }
        }
        else
        {
            anyActive = true; // 还在延迟期
        }

        if (!anyActive)
        {
            enabled = false;
            Plugin.Log.LogInfo("[GoldenStar] All effects ended.");
        }
    }
}

/// <summary>
/// 修改金星药膏物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class GoldenStarHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<GoldenStarItemMarker>();
        if (marker == null) return;
        if (!item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
