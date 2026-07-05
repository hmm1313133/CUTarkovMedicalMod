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
/// M.U.L.E. 兴奋剂注射器系统。
/// 核心机制：增加负重上限 +50%，代价是持续生命恢复减少。
///
/// 负重系统（反编译确认）：
/// - Body.maxEncumberance（float, public）— 负重上限，初始值 11
///   HandlePeriodicChecks 每 0.5 秒重算：基础11 ± 饥饿/渴惩罚 + 技能加成 × encumbrancecap
///   ⚠ 直接设置会被重算覆盖 → 必须在 LateUpdate 中持续追加加成
/// - Body.totalEncumberance — 当前总负重（所有物品 totalWeight 之和）
/// - Body.overEncumberance — 超重比例 = totalEncumberance / maxEncumberance - 1，Clamp01
/// - encumbered moodle（读 overEncumberance）：>0.85→4, >0.55→3, >0.3→2, >0→1
/// - overEncumberance 影响移动速度（get_legSpeedMult）、跳跃（Jump）、攻击速度（Attack）、体力恢复
///
/// 生命恢复：Body.HandleBody 中 brainHealth 每帧 += 0.003 * healingrate（当 brainHealth>0）
/// M.U.L.E. 减益：每秒扣 brainHealth 0.1（抵消并反转自然恢复）
/// </summary>
public static class MuleItemSystem
{
    public const string ItemKey = "mule";
    public const string BaseGameItemId = "syringe";

    public const string DisplayName = "M.U.L.E. 兴奋剂注射器【M.U.L.E】";
    public const string Description =
        "军用负重增强兴奋剂。通过刺激肌肉纤维和神经系统，临时大幅提升负重能力，适合携大量战利品撤离。\n\n" +
        "<color=#4fc3f7>效果：1秒后生效，持续900秒。负重上限 +50%。</color>\n" +
        "<color=#ff6666>副作用：药物持续损伤肌肉组织，每秒随机部位健康 -0.1。</color>";

    private static Sprite? _cachedIcon;

    public static bool IsMuleRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的 M.U.L.E. 物品实例。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsMuleRequest(request)) return;

        EnsureRegisteredInItemTable();

        item.id = ItemKey;
        item.SetCondition(100f);

        var marker = item.gameObject.GetComponent<MuleItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<MuleItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "mule-icon";
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
    /// 在 Item.GlobalItems 注册 M.U.L.E. 的 ItemInfo。
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

            var useMethod = typeof(MuleItemSystem).GetMethod(
                nameof(MuleUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered M.U.L.E. ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register M.U.L.E.: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// M.U.L.E. 使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// 激活效果控制器，持续 900s 增加负重上限 +50% 并扣除生命恢复。
    /// </summary>
    private static void MuleUseAction(Body body, Item item)
    {
        Plugin.Log.LogInfo("M.U.L.E. useAction invoked by game native system.");

        MuleEffectController.Attach(body).ActivateOrRefresh();

        // 消耗物品
        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied M.U.L.E.: encumberance boost +50% for 900s.");
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

        var useMethod = typeof(MuleItemSystem).GetMethod(
            nameof(MuleUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "mule.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "M.U.L.E.webp");
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
            _cachedIcon.name = "mule-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load M.U.L.E. icon: {ex.Message}");
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
/// M.U.L.E. 物品标记组件。
/// </summary>
public sealed class MuleItemMarker : MonoBehaviour
{
    public string itemKey = MuleItemSystem.ItemKey;
    public string displayName = MuleItemSystem.DisplayName;
    public string description = MuleItemSystem.Description;
}

/// <summary>
/// M.U.L.E. 效果控制器：
/// 增益与减益同时生效，持续 900 秒。
///
/// 增益（+50% 负重上限）：
/// Body.maxEncumberance 在 HandlePeriodicChecks 中每 0.5 秒被重算覆盖，
/// 故用 Harmony Postfix 拦截 HandlePeriodicChecks，在重算后追加 50% 加成。
/// 这会使 overEncumberance 降低，从而减轻 encumbered moodle 和移动惩罚。
///
/// 减益（生命恢复 -0.1/s）：
/// 每秒随机选择一个非断肢、非要害部位，扣除 muscleHealth 和 skinHealth 各 0.1。
/// 这样模拟药物对全身肌肉组织的持续损伤，而非直接扣脑健康。
/// 900 秒累计扣除约 90 点健康（分散到各部位）。
/// </summary>
public sealed class MuleEffectController : MonoBehaviour
{
    internal const float ActivationDelay = 1f;       // 生效延迟
    internal const float Duration = 900f;            // 总持续（15分钟）
    internal const float EncumberanceBonusMult = 0.5f; // 负重上限加成 +50%
    internal const float HealthDrainPerSecond = 0.1f; // 每秒生命恢复减少 0.1

    private Body? _body;
    private float _remaining;
    private float _delayTimer;
    private bool _active;
    private object? _sideEffectToken;

    /// <summary>
    /// 当前活跃的 M.U.L.E. 控制器实例（静态），供 Harmony 补丁读取。
    /// </summary>
    internal static MuleEffectController? ActiveInstance;

    /// <summary>
    /// 负重加成是否生效（延迟期已过且效果仍在持续）。
    /// </summary>
    internal bool IsEncumberanceActive => _active && _delayTimer <= 0f && _remaining > 0f;

    public static MuleEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<MuleEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<MuleEffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        _delayTimer = ActivationDelay;
        _remaining = Duration;
        _active = true;
        ActiveInstance = this;
        enabled = true;

        StimBuffIndicator.ShowBuff(
            MuleItemSystem.ItemKey,
            "M.U.L.E.",
            TryGetMuleIcon(),
            _delayTimer + _remaining,
            _delayTimer + _remaining,
            new Color(0.95f, 0.75f, 0.2f)); // 金黄色（负重）
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || !_active)
        {
            CleanupSideEffect();
            StimBuffIndicator.HideBuff(MuleItemSystem.ItemKey);
            enabled = false;
            return;
        }

        // 延迟期
        if (_delayTimer > 0f)
        {
            _delayTimer -= Time.deltaTime;
            StimBuffIndicator.ShowBuff(
                MuleItemSystem.ItemKey,
                "M.U.L.E.",
                TryGetMuleIcon(),
                _delayTimer + _remaining,
                ActivationDelay + Duration,
                new Color(0.95f, 0.75f, 0.2f));
            if (_delayTimer <= 0f)
            {
                Plugin.Log.LogInfo($"[M.U.L.E.] Effect active: +{EncumberanceBonusMult*100}% encumberance, -{HealthDrainPerSecond}/s random limb health for {Duration}s");
                // 延迟期结束，注册副作用到统一管理器
                if (_sideEffectToken == null)
                {
                    _sideEffectToken = StimSideEffectManager.GetOrCreate(_body)
                        .Register(MuleItemSystem.ItemKey, 0f, 0f, HealthDrainPerSecond);
                }
            }
            return;
        }

        // 效果期
        _remaining -= Time.deltaTime;

        StimBuffIndicator.ShowBuff(
            MuleItemSystem.ItemKey,
            "M.U.L.E.",
            TryGetMuleIcon(),
            _remaining,
            Duration,
            new Color(0.95f, 0.75f, 0.2f));

        if (_remaining <= 0f)
        {
            CleanupSideEffect();
            _active = false;
            ActiveInstance = null;
            StimBuffIndicator.HideBuff(MuleItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[M.U.L.E.] Effect ended. Encumberance bonus removed.");
        }
    }

    private void CleanupSideEffect()
    {
        if (_sideEffectToken != null && _body != null)
        {
            var manager = _body.GetComponent<StimSideEffectManager>();
            manager?.Unregister(_sideEffectToken);
            _sideEffectToken = null;
        }
    }

    private void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
        CleanupSideEffect();
    }

    private void OnDestroy()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
        CleanupSideEffect();
    }

    private static Sprite? TryGetMuleIcon()
    {
        var method = typeof(MuleItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 拦截 Body.HandlePeriodicChecks（Postfix），在游戏重算 maxEncumberance 后追加 M.U.L.E. 加成。
/// 这样加成只在每次重算后应用一次，不会每帧累积膨胀。
/// </summary>
[HarmonyPatch(typeof(Body), "HandlePeriodicChecks")]
public static class MuleEncumberancePatch
{
    [HarmonyPostfix]
    public static void Postfix(Body __instance)
    {
        var controller = MuleEffectController.ActiveInstance;
        if (controller == null) return;

        // 仅在效果激活且延迟期已过时追加
        if (!controller.IsEncumberanceActive) return;

        // 在游戏重算的 maxEncumberance 基础上追加 50%
        __instance.maxEncumberance += __instance.maxEncumberance * MuleEffectController.EncumberanceBonusMult;
    }
}

/// <summary>
/// 修改 M.U.L.E. 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class MuleHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<MuleItemMarker>();
        if (marker == null) return;

        __result = (marker.displayName, marker.description);
    }
}
