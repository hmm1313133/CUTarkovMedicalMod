using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// Zagustin 止血剂（紫针）系统。
/// 效果：立即止住所有出血，180秒内防止新出血。副作用：+50血液粘稠度，每秒-0.3水分持续2分钟。
/// </summary>
public static class ZagustinItemSystem
{
    public const string ItemKey = "zagustin";
    public const string BaseGameItemId = "syringe";

    public static string DisplayName => I18n.Tr("zagustin.name");
    public static string Description => I18n.Tr("zagustin.desc");

    private static Sprite? _cachedIcon;

    public static bool IsZagustinRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的 Zagustin 物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsZagustinRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(1f);

        var marker = item.gameObject.GetComponent<ZagustinItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<ZagustinItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "zagustin-icon";
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
    /// 在 Item.GlobalItems 注册 zagustin 的 ItemInfo。
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
            clone.value = 16;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,hemostatic,stim,combine,craft");
            clone.SetTags();

            // 设置 useAction 委托
            var useMethod = typeof(ZagustinItemSystem).GetMethod(
                nameof(ZagustinUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Zagustin ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Zagustin: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Zagustin 使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// </summary>
    private static void ZagustinUseAction(Body body, Item item)
    {

        InjectorSound.Play();
        Plugin.Log.LogInfo("Zagustin useAction invoked by game native system.");

        // 激活 180 秒止血效果
        ZagustinEffectController.Attach(body).ActivateOrRefresh();

        // 立即止住所有出血
        StopAllBleeding(body);

        // 消耗物品
        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied Zagustin hemostatic effect for 180 seconds.");
    }

    /// <summary>
    /// 立即止住所有出血（外部出血 + 内出血 + 血胸）。
    /// </summary>
    private static void StopAllBleeding(Body body)
    {
        // 外部出血：清除所有肢体的 bleedAmount
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

        // 内出血
        body.internalBleeding = 0f;

        // 血胸（胸腔积血）
        body.hemothorax = 0f;
    }

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
            tags = "drug,medicine,medical,hemostatic,stim,combine,craft",
            rec = new Recognition(13)
        };
        info.SetTags();

        var useMethod = typeof(ZagustinItemSystem).GetMethod(
            nameof(ZagustinUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "zagustin.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "zagustin.webp");
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
            _cachedIcon.name = "zagustin-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Zagustin icon: {ex.Message}");
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
}

/// <summary>
/// Zagustin 物品标记组件。
/// </summary>
public sealed class ZagustinItemMarker : MonoBehaviour
{
    public string itemKey = ZagustinItemSystem.ItemKey;
    public string displayName = ZagustinItemSystem.DisplayName;
    public string description = ZagustinItemSystem.Description;
}

/// <summary>
/// Zagustin 效果控制器：
/// - 180秒内所有肢体 blockedBleeding = true（防止新出血）
/// - 效果开始时血液粘稠度 +50（一次性）
/// - 前120秒持续扣除水分（每秒 -0.3）
/// </summary>
public sealed class ZagustinEffectController : MonoBehaviour
{
    private const float DurationSeconds = 180f;
    private const float SideEffectDurationSeconds = 120f; // 副作用持续 2 分钟
    private const float ThirstDrainPerSecond = 0.3f;
    private const float BloodViscosityIncrease = 50f;
    private const float TickInterval = 1f;

    private Body? _body;
    private float _remaining;
    private float _accumulator;
    private float _sideEffectRemaining;

    public static ZagustinEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<ZagustinEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<ZagustinEffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;

        if (isRefresh)
        {
            StimBuffIndicator.ShowOneTimeEffect(ZagustinItemSystem.ItemKey, I18n.Tr("zagustin.ot.0"));
            Plugin.Log.LogInfo("[Zagustin] Refresh: timer reset, negatives re-trigger.");
        }

        _remaining = DurationSeconds;
        _sideEffectRemaining = SideEffectDurationSeconds;
        _accumulator = 0f;
        enabled = true;

        // 一次性增加血液粘稠度（每次注射都触发）
        _body!.bloodViscosity += BloodViscosityIncrease;
        Plugin.Log.LogInfo($"Zagustin: bloodViscosity +{BloodViscosityIncrease} (now {_body.bloodViscosity}).");

        // 显示 buff 图标
        StimBuffIndicator.ShowOneTimeEffect(ZagustinItemSystem.ItemKey, I18n.Tr("zagustin.ot.1"));
        StimBuffIndicator.ShowOneTimeEffect(ZagustinItemSystem.ItemKey, I18n.Tr("zagustin.ot.2"));
        StimBuffIndicator.ShowBuff(
            ZagustinItemSystem.ItemKey,
            I18n.Tr("zagustin.buff"),
            TryGetZagustinIcon(),
            _remaining,
            DurationSeconds,
            new Color(0.7f, 0.3f, 0.9f), // 紫色（止血）
            positiveDescs: I18n.TrAll("zagustin.pos.0"),
            negativeDescs: I18n.TrAll("zagustin.neg.0"));
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _remaining <= 0f)
        {
            StimBuffIndicator.HideBuff(ZagustinItemSystem.ItemKey);
            enabled = false;
            return;
        }

        // 持续保持止血状态
        var limbs = _body.limbs;
        if (limbs != null)
        {
            foreach (var limb in limbs)
            {
                if (limb == null || limb.dismembered) continue;
                limb.blockedBleeding = true;
            }
        }

        _accumulator += Time.deltaTime;
        while (_accumulator >= TickInterval && _remaining > 0f)
        {
            _accumulator -= TickInterval;
            _remaining -= TickInterval;
            _sideEffectRemaining -= TickInterval;
            TickEffect();
        }

        // 更新 buff 图标剩余时间
        StimBuffIndicator.ShowBuff(
            ZagustinItemSystem.ItemKey,
            I18n.Tr("zagustin.buff"),
            TryGetZagustinIcon(),
            _remaining,
            DurationSeconds,
            new Color(0.7f, 0.3f, 0.9f),
            positiveDescs: I18n.TrAll("zagustin.pos.0"),
            negativeDescs: I18n.TrAll("zagustin.neg.0"));

        if (_remaining <= 0f)
        {
            StimBuffIndicator.HideBuff(ZagustinItemSystem.ItemKey);
            enabled = false;
        }
    }

    private static Sprite? TryGetZagustinIcon()
    {
        var method = typeof(ZagustinItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }

    private void TickEffect()
    {
        // 副作用：前 120 秒每秒扣除水分
        // 直接修改字段，不使用 Drink()，避免 KrokMP 拦截方法调用导致本地修改被覆盖
        if (_sideEffectRemaining > 0f)
        {
            _body!.thirst = Mathf.Max(0f, _body.thirst - ThirstDrainPerSecond);
        }
    }

    /// <summary>
    /// 效果结束时解除止血封锁，恢复正常出血机制。
    /// </summary>
    private void OnDisable()
    {
        if (_body == null) return;

        var limbs = _body.limbs;
        if (limbs != null)
        {
            foreach (var limb in limbs)
            {
                if (limb == null || limb.dismembered) continue;
                limb.blockedBleeding = false;
            }
        }
        Plugin.Log.LogInfo("[Zagustin] Effect ended, blockedBleeding released.");
    }
}

/// <summary>
/// 修改 Zagustin 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class ZagustinHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<ZagustinItemMarker>();
        if (marker == null) return;

        if (item.Stats?.rec == null || !item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
