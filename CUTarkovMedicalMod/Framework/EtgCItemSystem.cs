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
        "强大的再生过程促进剂。用于伤员受伤后或重伤员运输过程中的快速恢复，只允许专业医师和护理人员使用。有强副作用。\n\n" +
        "<color=#00ff66>效果：每秒恢复每个部位6点生命，持续40秒，快速愈合伤口。</color>\n" +
        "<color=#ff6666>副作用：持续消耗饱食度与水分，引发轻微再生性颤栗。</color>";

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
        item.SetCondition(100f);

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
            clone.category = "drug";
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = true;
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
/// ETG-c 效果控制器：每秒每部位恢复 10 HP，持续 60 秒，消耗饱食度。
/// </summary>
public sealed class EtgStimEffectController : MonoBehaviour
{
    private const float HealPerSecondPerLimb = 6f;
    private const float DurationSeconds = 40f;
    private const float HungerDrainPerSecond = 0.5f;
    private const float ThirstDrainPerSecond = 0.4f;
    private const float TremorIntensity = 0.08f;
    private const float MaxLimbHealth = 100f;

    private Body? _body;
    private float _remaining;
    private float _accumulator;

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
        _remaining = DurationSeconds;
        _accumulator = 0f;
        enabled = true;

        // 显示 buff 图标
        StimBuffIndicator.ShowBuff(
            EtgCItemSystem.EtgItemKey,
            "eTG-c",
            TryGetEtgIcon(),
            _remaining,
            DurationSeconds,
            new Color(0.4f, 1f, 0.6f)); // 绿色（再生）
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _remaining <= 0f)
        {
            if (_body != null) _body.miscShakeIntensity = 0f;
            StimBuffIndicator.HideBuff(EtgCItemSystem.EtgItemKey);
            enabled = false;
            return;
        }

        _accumulator += Time.deltaTime;
        while (_accumulator >= 1f && _remaining > 0f)
        {
            _accumulator -= 1f;
            _remaining -= 1f;
            TickEffect();
        }

        // 再生性颤栗
        _body.miscShakeIntensity = TremorIntensity;

        // 更新 buff 剩余时间
        StimBuffIndicator.ShowBuff(
            EtgCItemSystem.EtgItemKey,
            "eTG-c",
            TryGetEtgIcon(),
            _remaining,
            DurationSeconds,
            new Color(0.4f, 1f, 0.6f));

        if (_remaining <= 0f)
        {
            _body.miscShakeIntensity = 0f;
            StimBuffIndicator.HideBuff(EtgCItemSystem.EtgItemKey);
            enabled = false;
        }
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
        _body.Eat(-HungerDrainPerSecond, 0f);
        _body.Drink(-ThirstDrainPerSecond);
    }

    private static void HealLimb(Limb limb, float amount)
    {
        if (amount <= 0f) return;

        var skinMissing = Mathf.Max(0f, MaxLimbHealth - limb.skinHealth);
        var addSkin = Mathf.Min(amount, skinMissing);
        limb.skinHealth += addSkin;

        var remain = amount - addSkin;
        if (remain <= 0f) return;

        var muscleMissing = Mathf.Max(0f, MaxLimbHealth - limb.muscleHealth);
        limb.muscleHealth += Mathf.Min(remain, muscleMissing);
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

        __result = (marker.displayName, marker.description);
    }
}
