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
/// ETG-c 再生兴奋剂系统。
/// 核心机制：在 Item.GlobalItems 注册自定义 ItemInfo，设置 useAction 委托指向自定义方法。
/// 游戏原生 UseItem 系统会自动调用该委托，无需 Harmony 拦截 UseItem。
/// </summary>
public static class EtgCItemSystem
{
    public const string EtgItemKey = "etg_c";
    public const string EtgBaseGameItemId = "syringe";

    public const string EtgDisplayName = "eTG-change再生兴奋剂注射器【eTG-c】";
    public const string EtgDescription =
        "强大的再生过程促进剂。用于伤员受伤后或重伤员运输过程中的快速恢复，只允许专业医师和护理人员使用。写着‘TerraGroup 实验室开发’\n\n" +
        "<color=#54ff9f>效果：每秒恢复每个部位2点肌肉健康，血容量每秒回升50ml，持续60秒。</color>\n" +
        "<color=#ff6666>副作用：效果结束后20秒内持续消耗饱食度与水分，并引发胸口剧烈疼痛。</color>";

    private static Sprite? _cachedIcon;

    public static bool IsEtgRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(EtgItemKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 配置发放的 ETG-c 物品实例。
    /// 关键：修改 item.id 为 "etg_c"，使 Item.Stats 自动从 GlobalItems 查询到我们注册的 ItemInfo。
    /// 必须在 AutoPickUpItem 之后调用。
    /// </summary>
    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsEtgRequest(request)) return;

        EnsureRegisteredInItemTable();

        // 修改 id — Item.Stats 属性会自动从 GlobalItems["etg_c"] 查询
        item.id = EtgItemKey;

        // 满耐久
        item.SetCondition(1f);

        // 标记组件
        var marker = item.gameObject.GetComponent<EtgStimItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<EtgStimItemMarker>();
        marker.itemKey = EtgItemKey;
        marker.displayName = EtgDisplayName;
        marker.description = EtgDescription;

        // 自定义图标
        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "etg-c-icon";
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
    /// 在 Item.GlobalItems 注册 etg_c 的 ItemInfo。
    /// 克隆 syringe 的 ItemInfo，设置 useAction 委托指向 EtgUseAction。
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

            if (map.Contains(EtgItemKey))
            {
                return true;
            }

            // 克隆 syringe 的 ItemInfo
            ItemInfo? clone = null;
            if (map.Contains(EtgBaseGameItemId))
            {
                var source = map[EtgBaseGameItemId] as ItemInfo;
                clone = CloneItemInfo(source);
            }
            if (clone == null)
                clone = CreateFallbackItemInfo();

            // 覆盖为 ETG-c 配置
            clone.fullName = EtgDisplayName;
            clone.description = EtgDescription;
            clone.category = "ModStim";
            clone.weight = 0.1f;
            clone.value = 17;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            // 关键：设置 useAction 委托指向我们的自定义方法
            // 游戏原生 UseItem 系统会调用 useAction.Invoke(body, item)
            var useMethod = typeof(EtgCItemSystem).GetMethod(
                nameof(EtgUseAction),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (useMethod != null)
            {
                clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
                    typeof(ItemInfo.Use), useMethod);
            }
            clone.useLimbAction = null;

            map[EtgItemKey] = clone;
            Plugin.Log.LogInfo("Registered ETG-c ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register ETG-c: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// ETG-c 使用效果 — 由游戏原生 UseItem 系统通过 useAction 委托调用。
    /// 签名必须匹配 ItemInfo.Use 委托: void(Body body, Item item)
    /// </summary>
    private static void EtgUseAction(Body body, Item item)
    {
        Plugin.Log.LogInfo("ETG-c useAction invoked by game native system.");

        // 激活 60 秒再生效果
        EtgStimEffectController.Attach(body).ActivateOrRefresh();

        // 消耗物品
        try { body.DropItem(item); } catch { }
        UnityEngine.Object.Destroy(item.gameObject);

        Plugin.Log.LogInfo("Applied ETG-c stim effect for 60 seconds.");
    }

    private static ItemInfo CreateFallbackItemInfo()
    {
        var info = new ItemInfo
        {
            fullName = EtgDisplayName,
            description = EtgDescription,
            category = "ModStim",
            usable = true,
            usableOnLimb = false,
            usableWithLMB = false,
            combineable = true,
            destroyAtZeroCondition = true,
            scaleWeightWithCondition = false,
            weight = 0.1f,
            value = 17,
            tags = "drug,medicine,medical,stim,combine,craft"
        };
        info.SetTags();

        // 设置 useAction 委托
        var useMethod = typeof(EtgCItemSystem).GetMethod(
            nameof(EtgUseAction), BindingFlags.Static | BindingFlags.NonPublic);
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
            var iconPath = Path.Combine(assetDir, "etg.png");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.Combine(assetDir, "etg.webp");
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
            _cachedIcon.name = "etg-c-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load ETG icon: {ex.Message}");
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
/// Item.SetupItems postfix — 重新注册 ETG-c（GlobalItems 可能被重建）。
/// </summary>
[HarmonyPatch(typeof(Item), nameof(Item.SetupItems))]
public static class EtgStimRegistryPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        EtgCItemSystem.EnsureRegisteredInItemTable();
        ZagustinItemSystem.EnsureRegisteredInItemTable();
        MorphineItemSystem.EnsureRegisteredInItemTable();
        SJ12ItemSystem.EnsureRegisteredInItemTable();
        MuleItemSystem.EnsureRegisteredInItemTable();
        PropitalItemSystem.EnsureRegisteredInItemTable();
        PnbItemSystem.EnsureRegisteredInItemTable();
        Sj1ItemSystem.EnsureRegisteredInItemTable();
        ObdolbosItemSystem.EnsureRegisteredInItemTable();
        Sj9ItemSystem.EnsureRegisteredInItemTable();
        BluebloodItemSystem.EnsureRegisteredInItemTable();
        Xtg12ItemSystem.EnsureRegisteredInItemTable();
        MildronateItemSystem.EnsureRegisteredInItemTable();
        TwoATwoBTGItemSystem.EnsureRegisteredInItemTable();
        Obdolbos2ItemSystem.EnsureRegisteredInItemTable();
    }
}

/// <summary>
/// ETG-c 物品标记组件。
/// </summary>
public sealed class EtgStimItemMarker : MonoBehaviour
{
    public string itemKey = EtgCItemSystem.EtgItemKey;
    public string displayName = EtgCItemSystem.EtgDisplayName;
    public string description = EtgCItemSystem.EtgDescription;
}

/// <summary>
/// ETG-c 效果控制器：每秒每部位恢复 2 点肌肉健康度，血容量向 5.00L 移动 50ml，持续 60 秒。
/// 60 秒后进入 20 秒负面效果：每秒 -1 饱食度与水份，胸口 +15 疼痛。
/// </summary>
public sealed class EtgStimEffectController : MonoBehaviour
{
    private const float HealPerSecondPerLimb = 2f;
    private const float DurationSeconds = 60f;
    private const float TargetBloodVolume = 5.0f;
    private const float BloodAdjustPerSecond = 0.05f;
    private const float MaxLimbHealth = 100f;

    private const float DebuffDurationSeconds = 20f;
    private const float DebuffHungerDrain = 1f;
    private const float DebuffThirstDrain = 1f;
    private const float DebuffChestPain = 40f;

    private Body? _body;
    private float _remaining;
    private float _accumulator;
    private bool _isDebuffPhase;
    private float _debuffRemaining;

    public static EtgStimEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<EtgStimEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<EtgStimEffectController>();
        controller._body = body;
        return controller;
    }

    public void ActivateOrRefresh()
    {
        bool isRefresh = enabled;

        if (isRefresh)
        {
            StimBuffIndicator.ShowOneTimeEffect(EtgCItemSystem.EtgItemKey, "二次注射 正面效果不叠加");
            Plugin.Log.LogInfo("[eTG-c] Refresh: timer reset, negatives re-trigger.");
        }

        _remaining = DurationSeconds;
        _accumulator = 0f;
        _isDebuffPhase = false;
        enabled = true;

        // 显示 buff 图标
        StimBuffIndicator.ShowBuff(
            EtgCItemSystem.EtgItemKey,
            "eTG-c",
            TryGetEtgIcon(),
            _remaining,
            DurationSeconds,
            new Color(0.4f, 1f, 0.6f), // 绿色（再生）
            positiveDescs: new[] { "全部肢体再生", "血容量回升至5L" });
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null)
        {
            StimBuffIndicator.HideBuff(EtgCItemSystem.EtgItemKey);
            enabled = false;
            return;
        }

        if (!_isDebuffPhase)
        {
            // === 正面 buff 阶段 ===
            if (_remaining <= 0f)
            {
                StimBuffIndicator.HideBuff(EtgCItemSystem.EtgItemKey);
                StartDebuffPhase();
                return;
            }

            _accumulator += Time.deltaTime;
            while (_accumulator >= 1f && _remaining > 0f)
            {
                _accumulator -= 1f;
                _remaining -= 1f;
                TickEffect();
            }

            StimBuffIndicator.ShowBuff(
                EtgCItemSystem.EtgItemKey,
                "eTG-c",
                TryGetEtgIcon(),
                _remaining,
                DurationSeconds,
                new Color(0.4f, 1f, 0.6f),
                positiveDescs: new[] { "全部肢体再生", "血容量回升至5L" });

            if (_remaining <= 0f)
            {
                StimBuffIndicator.HideBuff(EtgCItemSystem.EtgItemKey);
                StartDebuffPhase();
            }
        }
        else
        {
            // === 负面 debuff 阶段 ===
            // 显示负面效果
            StimBuffIndicator.ShowBuff(
                EtgCItemSystem.EtgItemKey,
                "eTG-c(副作用)",
                TryGetEtgIcon(),
                _debuffRemaining,
                DebuffDurationSeconds,
                new Color(1f, 0.3f, 0.3f), // 红色（副作用警告）
                positiveDescs: Array.Empty<string>(),
                negativeDescs: new[] { "每秒-1饱食/水分" });

            if (_debuffRemaining <= 0f)
            {
                StimBuffIndicator.HideBuff(EtgCItemSystem.EtgItemKey);
                enabled = false;
                return;
            }

            _accumulator += Time.deltaTime;
            while (_accumulator >= 1f && _debuffRemaining > 0f)
            {
                _accumulator -= 1f;
                _debuffRemaining -= 1f;
                DebuffTick();
            }

            if (_debuffRemaining <= 0f)
            {
                enabled = false;
            }
        }
    }

    private void StartDebuffPhase()
    {
        _isDebuffPhase = true;
        _debuffRemaining = DebuffDurationSeconds;
        _accumulator = 0f;
        ApplyChestPain();
        Plugin.Log.LogInfo("ETG-c debuff phase started: -1 hunger/thirst per second, +40 chest pain for 20s.");
    }

    private void ApplyChestPain()
    {
        var chestLimb = FindChestLimb();
        if (chestLimb != null)
        {
            chestLimb.pain += DebuffChestPain;
            StimBuffIndicator.ShowOneTimeEffect(EtgCItemSystem.EtgItemKey, $"胸口疼痛+{DebuffChestPain}", isNegative: true);
            Plugin.Log.LogInfo($"ETG-c debuff: applied +{DebuffChestPain} pain to chest.");
        }
    }

    private Limb? FindChestLimb()
    {
        var limbs = _body!.limbs;
        if (limbs == null) return null;

        foreach (var limb in limbs)
        {
            if (limb == null || limb.dismembered) continue;
            if (limb.isVital && !limb.isHead)
                return limb;
        }
        return null;
    }

    private void DebuffTick()
    {
        _body!.Eat(-DebuffHungerDrain, 0f);
        _body.Drink(-DebuffThirstDrain);
    }

    private static Sprite? TryGetEtgIcon()
    {
        // 通过反射调用 EtgCItemSystem 的私有 TryLoadIcon
        var method = typeof(EtgCItemSystem).GetMethod("TryLoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic);
        return method?.Invoke(null, null) as Sprite;
    }

    private void TickEffect()
    {
        var limbs = _body!.limbs;
        if (limbs != null)
        {
            foreach (var limb in limbs)
            {
                if (limb == null || limb.dismembered) continue;
                HealLimb(limb, HealPerSecondPerLimb);
            }
        }
        AdjustBloodVolume();
    }

    private void AdjustBloodVolume()
    {
        var currentBlood = _body!.bloodVolume;
        if (currentBlood >= TargetBloodVolume)
            return;

        var newBlood = Mathf.Min(currentBlood + BloodAdjustPerSecond, TargetBloodVolume);
        _body.bloodVolume = newBlood;
    }

    private static void HealLimb(Limb limb, float amount)
    {
        if (amount <= 0f) return;

        var muscleMissing = Mathf.Max(0f, MaxLimbHealth - limb.muscleHealth);
        limb.muscleHealth += Mathf.Min(amount, muscleMissing);
    }
}

/// <summary>
/// 修改物品悬浮提示为 ETG-c 自定义文本。
/// PlayerCamera.ItemHoverDescription 是静态方法，返回 ValueTuple&lt;string, string&gt;。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class EtgStimHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<EtgStimItemMarker>();
        if (marker == null) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
