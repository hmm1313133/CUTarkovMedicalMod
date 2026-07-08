using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// M.U.L.E. 兴奋剂注射器系统。
/// 核心机制：增加负重上限 +15，代价是持续肌肉损伤和患病。
///
/// 负重系统（反编译确认）：
/// - Body.maxEncumberance（float, public）— 负重上限，初始值 11
///   HandlePeriodicChecks 每 0.5 秒重算：基础11 ± 饥饿/渴惩罚 + 技能加成 × encumbrancecap
///   ⚠ 直接设置会被重算覆盖 → 通过 Harmony Transpiler 在重算时追加 +15
/// - Body.totalEncumberance — 当前总负重（所有物品 totalWeight 之和）
/// - Body.overEncumberance — 超重比例 = totalEncumberance / maxEncumberance - 1，Clamp01
/// - encumbered moodle（读 overEncumberance）：>0.85→4, >0.55→3, >0.3→2, >0→1
/// - overEncumberance 影响移动速度（get_legSpeedMult）、跳跃（Jump）、攻击速度（Attack）、体力恢复
///
/// 减益：立即 +10 患病，每秒各肢体肌肉健康 -0.2，持续 25 分钟；效果期间意识清醒度限制在 90 以下。
/// </summary>
public static class MuleItemSystem
{
    public const string ItemKey = "mule";
    public const string BaseGameItemId = "syringe";

    public const string DisplayName = "M.U.L.E. 兴奋剂注射器【M.U.L.E】";
    public const string Description =
        "军用负重增强兴奋剂。通过刺激肌肉纤维和神经系统，显著提升负重能力，适合携大量战利品撤离；标有 M. U. L. E 的记号。大大的黄色叹号后写着许许多多的副作用，TerraGroup 实验室开发。\n\n" +
        "<color=#54ff9f>效果：1秒后生效，持续40分钟，负重上限 +15U（可与其他针剂叠加）。</color>\n" +
        "<color=#ff6666>副作用：+10患病；每秒各肢体肌肉健康 -0.2，持续25分钟；效果期间意识清醒度上限锁定在90。</color>";

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
        item.SetCondition(1f);

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
            clone.category = "ModStim";
            clone.weight = 0.1f;
            clone.value = 20;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
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
    /// 激活效果控制器，负重上限 +15 持续 40 分钟，肌肉损伤 + 患病持续 25 分钟。
    /// </summary>
    private static void MuleUseAction(Body body, Item item)
    {
        InjectorSound.Play();
        Plugin.Log.LogInfo("M.U.L.E. useAction invoked by game native system.");

        MuleEffectController.Attach(body).ActivateOrRefresh();

        // 消耗物品
        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo($"Applied M.U.L.E.: encumberance boost +{MuleEffectController.CarryWeightBonus} for {MuleEffectController.BuffDuration}s.");
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
            value = 20,
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
/// 增益持续 40 分钟，减益持续 25 分钟（同时生效）。
///
/// 增益（负重上限 +15）：
/// Body.maxEncumberance 在 HandlePeriodicChecks 中每 0.5 秒被重算覆盖。
/// 通过 Harmony Transpiler 直接修改该赋值指令，在写入前加上 15，
/// 确保加成只在重算时应用一次，且 overEncumberance 基于加成后的上限计算，
/// 从而正确减轻 encumbered moodle 和移动惩罚。
///
/// 减益（立即 +10 患病，每秒 -0.2 肌肉健康/肢体，意识清醒度 < 90）：
/// 全部非断肢部位每秒同时扣除 muscleHealth 0.2，模拟药物对全身肌肉的损伤。
/// 效果期间意识清醒度（conscious）被限制在 90 以下。
/// 25 分钟后减益结束，仅剩负重加成持续到 40 分钟。同时意识上限恢复。
/// </summary>
public sealed class MuleEffectController : MonoBehaviour
{
    internal const float ActivationDelay = 1f;            // 生效延迟
    internal const float BuffDuration = 2400f;            // 增益持续 40 分钟
    internal const float DebuffDuration = 1500f;           // 减益持续 25 分钟
    internal const float CarryWeightBonus = 15f;          // 负重上限 +15
    internal const float MuscleDrainPerSecond = 0.2f;      // 每秒每个肢体肌肉健康减少
    internal const float SicknessOnUse = 10f;              // 立即增加患病
    internal const float ConsciousLimit = 90f;             // 意识清醒度上限（效果期间）

    private Body? _body;
    private float _buffRemaining;
    private float _debuffRemaining;
    private float _delayTimer;
    private float _drainAccumulator;
    private bool _active;
    private static FieldInfo? _consciousField;

    /// <summary>
    /// 当前活跃的 M.U.L.E. 控制器实例（静态），供 Harmony 补丁读取。
    /// </summary>
    internal static MuleEffectController? ActiveInstance;

    /// <summary>
    /// 负重加成是否生效（延迟期已过且效果仍在持续）。
    /// </summary>
    internal bool IsEncumberanceActive => _active && _delayTimer <= 0f && _buffRemaining > 0f;

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
        bool isRefresh = enabled;

        if (isRefresh)
        {
            StimBuffIndicator.ShowOneTimeEffect(MuleItemSystem.ItemKey, "二次注射 正面效果不叠加");
            Plugin.Log.LogInfo("[MULE] Refresh: timer reset, negatives re-trigger.");
        }

        _delayTimer = ActivationDelay;
        _buffRemaining = BuffDuration;
        _debuffRemaining = DebuffDuration;
        _drainAccumulator = 0f;
        _active = true;
        ActiveInstance = this;
        enabled = true;

        // 立即 +10 患病（每次注射都触发）
        if (_body != null)
            _body.sicknessAmount += SicknessOnUse;

        StimBuffIndicator.ShowOneTimeEffect(MuleItemSystem.ItemKey, "患病+10", isNegative: true);

        StimBuffIndicator.ShowBuff(
            MuleItemSystem.ItemKey,
            "M.U.L.E.",
            TryGetMuleIcon(),
            _delayTimer + _buffRemaining,
            _delayTimer + BuffDuration,
            new Color(0.95f, 0.75f, 0.2f), // 金黄色（负重）
            positiveDescs: new[] { "最大负重+20kg", "移动速度+10%", "力量+5" },
            negativeDescs: new[] { "每秒肌肉-0.2", "意识清醒≤90%" });
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || !_active)
        {
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
                _delayTimer + _buffRemaining,
                ActivationDelay + BuffDuration,
                new Color(0.95f, 0.75f, 0.2f),
                positiveDescs: new[] { "最大负重+20kg", "移动速度+10%", "力量+5" },
                negativeDescs: new[] { "每秒肌肉-0.2", "意识清醒≤90%" });
            if (_delayTimer <= 0f)
                Plugin.Log.LogInfo($"[M.U.L.E.] Effect active: +{CarryWeightBonus} encumberance for {BuffDuration}s, muscle drain {MuscleDrainPerSecond}/s per limb for {DebuffDuration}s");
            return;
        }

        // 效果期
        _buffRemaining -= Time.deltaTime;
        _debuffRemaining -= Time.deltaTime;

        // 肌肉健康持续扣除（减益期）
        if (_debuffRemaining > 0f)
        {
            _drainAccumulator += Time.deltaTime;
            while (_drainAccumulator >= 1f)
            {
                _drainAccumulator -= 1f;
                DrainAllLimbsMuscle();
            }
        }

        // 意识清醒度限制在 90 以下（整个效果期间）
        ClampConscious();

        StimBuffIndicator.ShowBuff(
            MuleItemSystem.ItemKey,
            "M.U.L.E.",
            TryGetMuleIcon(),
            _buffRemaining,
            BuffDuration,
            new Color(0.95f, 0.75f, 0.2f),
            positiveDescs: new[] { "最大负重+20kg", "移动速度+10%", "力量+5" },
            negativeDescs: _debuffRemaining > 0f
                ? new[] { "每秒肌肉-0.2", "意识清醒≤90%" }
                : new[] { "意识清醒≤90%" });

        if (_buffRemaining <= 0f)
        {
            _active = false;
            ActiveInstance = null;
            StimBuffIndicator.HideBuff(MuleItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[M.U.L.E.] Effect ended. Encumberance bonus removed.");
        }
    }

    /// <summary>
    /// 每秒对所有非断肢部位扣除 muscleHealth。
    /// </summary>
    private void DrainAllLimbsMuscle()
    {
        if (_body == null || _body.limbs == null) return;

        foreach (var limb in _body.limbs)
        {
            if (limb == null || limb.dismembered) continue;
            limb.muscleHealth = Mathf.Max(0f, limb.muscleHealth - MuscleDrainPerSecond);
        }
    }

    /// <summary>
    /// 限制意识清醒度（conscious）在 90 以下，模拟药物对神经系统的抑制。
    /// </summary>
    private void ClampConscious()
    {
        if (_body == null) return;

        try
        {
            if (_consciousField == null)
            {
                _consciousField = typeof(Body).GetField("conscious",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (_consciousField == null) return;
            }

            var current = (float)(_consciousField.GetValue(_body) ?? 0f);
            if (current > ConsciousLimit)
                _consciousField.SetValue(_body, ConsciousLimit);
        }
        catch { }
    }

    private void OnDisable()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    private void OnDestroy()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;
    }

    private static Sprite? TryGetMuleIcon()
    {
        var method = typeof(MuleItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }
}

/// <summary>
/// 用 Harmony Transpiler 直接修改 Body.HandlePeriodicChecks 中 maxEncumberance 的赋值。
///
/// 原 Postfix 的问题：
/// - HandlePeriodicChecks 每帧在 Body.Update 中被调用；
/// - 但 maxEncumberance 只在 halfSecondCheckTime &gt; 0.5f 的分支里每 0.5 秒重算一次；
/// - Postfix 每帧都追加加成，导致非重算帧在已加成值上再次累加，
///   maxEncumberance 在 0.5 秒周期内膨胀，表现为负重上限"乱跳"。
/// - 同时 overEncumberance 在方法内部、Postfix 之前已计算，基于未加成上限，
///   所以移动惩罚等并未真正降低。
///
/// Transpiler 修复：在 stfld Body.maxEncumberance 之前插入 call 到 GetEncumberanceBonus()
/// 并 add，使写入的值 = 原计算值 + 加成。这样：
/// - 加成只在 0.5 秒重算时应用一次；
/// - overEncumberance 随后基于加成后的上限计算，移动惩罚正确降低；
/// - 效果结束后下一周期自动恢复原始上限。
/// </summary>
[HarmonyPatch(typeof(Body), "HandlePeriodicChecks")]
public static class MuleEncumberancePatch
{
    /// <summary>
    /// 返回当前 M.U.L.E. 负重加成值：生效时为 +15，否则为 0。
    /// </summary>
    public static float GetEncumberanceBonus()
    {
        var bonus = 0f;
        var muleController = MuleEffectController.ActiveInstance;
        if (muleController != null && muleController.IsEncumberanceActive)
            bonus += MuleEffectController.CarryWeightBonus;
        var twoAController = TwoATwoBTGEffectController.ActiveInstance;
        if (twoAController != null && twoAController.IsCarryWeightActive)
            bonus += TwoATwoBTGEffectController.CarryWeightBonus;
        var obd2Controller = Obdolbos2EffectController.ActiveInstance;
        if (obd2Controller != null && obd2Controller.IsCarryWeightActive)
            bonus += Obdolbos2EffectController.CarryWeightBonus;
        return bonus;
    }

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var bonusMethod = AccessTools.Method(typeof(MuleEncumberancePatch), nameof(GetEncumberanceBonus));
        var maxEncumberanceField = AccessTools.Field(typeof(Body), nameof(Body.maxEncumberance));

        return new CodeMatcher(instructions)
            .MatchForward(false, new CodeMatch(OpCodes.Stfld, maxEncumberanceField))
            .ThrowIfInvalid("Could not find maxEncumberance assignment in Body.HandlePeriodicChecks")
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Call, bonusMethod),
                new CodeInstruction(OpCodes.Add))
            .InstructionEnumeration();
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

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
