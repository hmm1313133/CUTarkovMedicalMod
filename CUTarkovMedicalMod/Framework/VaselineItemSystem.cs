using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using CUCoreLib.Data;
using CUCoreLib.Registries;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 凡士林药膏系统。
/// 多用途软膏，防水和润滑特性。
/// 液体药膏，容器 10ml，每次使用 2ml（5 次）。
/// 效果：脏污度 -2，表皮健康度 +5。
/// 特殊效果：在左/右手使用时，爪子健康值 +10。
/// 模板：基于原版 bruisekit（Pattern C: LiquidItemInfo + ApplyToLimb）。
/// </summary>
public static class VaselineItemSystem
{
    public const string ItemKey = "vaseline";
    public const string BaseGameItemId = "bruisekit";
    public const string LiquidId = "vaseline_liquid";

    public static string DisplayName => I18n.Tr("vaseline.name");
    public static string Description => I18n.Tr("vaseline.desc");

    private const float TotalMl = 10f;       // 容器容量
    private const float MlPerUse = 2f;       // 每次使用量

    // 效果常量
    private const float DirtReduce = 2f;         // 脏污度 -2
    private const float SkinHeal = 5f;           // 表皮 +5
    private const float ClawHealOnArm = 10f;     // 爪子 +10（仅手臂）

    // 纯白色
    internal static readonly Color WhiteColor = new Color(1f, 1f, 1f, 1f);

    private static Sprite? _cachedIcon;

    public static bool IsVaselineRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsVaselineRequest(request)) return;

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

        var marker = item.gameObject.GetComponent<VaselineItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<VaselineItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite, 2f);
                if (adjusted != null)
                {
                    adjusted.name = "vaseline-icon";
                    sr.sprite = adjusted;
                }
                else
                {
                    sr.sprite = icon;
                }
            }
        }

        Plugin.Log.LogInfo($"[Vaseline] Configured spawned item '{ItemKey}' (condition={item.condition}).");
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
            clone.value = 10;
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
            clone.useLimbAction = VaselineUseLimbAction;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Vaseline ItemInfo with custom useLimbAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Vaseline: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 肢体使用 — 调用 WaterContainerItem.ApplyToLimb，消耗液体并触发 onHealthUse。
    /// </summary>
    private static void VaselineUseLimbAction(Limb limb, Item item)
    {
        try
        {
            EnsureLiquidRegistered();

            var wat = item.GetComponent<WaterContainerItem>();
            if (wat == null)
            {
                Plugin.Log.LogWarning("[Vaseline] WaterContainerItem not found on item!");
                return;
            }

            // ApplyToLimb 消耗 2ml 液体并调用 onHealthUse(2ml, limb)
            wat.ApplyToLimb(limb, MlPerUse);
            PlayUseSound(item, "vg");
            Plugin.Log.LogInfo($"[Vaseline] Applied {MlPerUse}ml to limb {limb.name}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Vaseline] Failed to apply: {ex.Message}");
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
    /// 注册自定义液体 "vaseline_liquid" 到 Liquids.Registry。
    /// onHealthUse 回调应用脏污度降低和表皮恢复，手臂使用时恢复爪子健康。
    /// </summary>
    private static void EnsureLiquidRegistered()
    {
        // 注册液体数据（通过 CUCoreLib 支持多人网络同步）
        if (!Liquids.Registry.ContainsKey(LiquidId))
        {
            LiquidRegistry.Register(LiquidId, new CustomLiquidInfo
            {
                name = "Vaseline",
                color = WhiteColor,
                valuePerLiter = 100f,
                injectable = false,
                injectionSickness = 0f,
                healthUsable = true,
            });
            Plugin.Log.LogInfo($"[Vaseline] Registered custom liquid '{LiquidId}' in Liquids.Registry.");
        }

        // 每次都重设回调——CUCoreLib 的 ApplyNetworkSnapshot 会在网络同步时
        // 用无回调的 LiquidType 覆盖 Liquids.Registry，导致 onHealthUse 变空。
        var lt = Liquids.Registry[LiquidId];
        lt.onHealthUse = delegate(float ml, Limb limb)
            {
                if (limb == null) return;

                var body = limb.body;
                if (body == null) return;

                // 1) 脏污度 -2
                body.dirtyness = Mathf.Max(0f, body.dirtyness - DirtReduce);

                // 2) 表皮健康度 +5
                limb.skinHealth = Mathf.Min(100f, limb.skinHealth + SkinHeal);

                // 3) 特殊效果：在手臂（左/右手）使用时，爪子健康值 +10
                if (limb.isArm)
                {
                    body.clawHealth = Mathf.Min(100f, body.clawHealth + ClawHealOnArm);
                    Plugin.Log.LogInfo($"[Vaseline] Applied to arm: clawHealth +{ClawHealOnArm} (now {body.clawHealth}).");
                }
            };
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
            value = 10,
            tags = "drug,medicine,medical,stim,combine,craft",
            useLimbAction = VaselineUseLimbAction,
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

    internal static Sprite? TryLoadIcon()
    {
        if (_cachedIcon != null) return _cachedIcon;

        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
            var assetDir = Path.Combine(assemblyDir, "Framework", "Assets");
            var iconPath = Path.Combine(assetDir, "vaseline.png");
            bool found = File.Exists(iconPath);

            if (!found)
            {
                iconPath = Path.Combine(assetDir, "Vaseline.webp");
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
            _cachedIcon.name = "vaseline-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Vaseline icon: {ex.Message}");
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

    public static Sprite? TryGetVaselineIcon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetVaselineIcon();
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
/// 凡士林物品标记组件。
/// </summary>
public sealed class VaselineItemMarker : MonoBehaviour
{
    public string itemKey = VaselineItemSystem.ItemKey;
    public string displayName = VaselineItemSystem.DisplayName;
    public string description = VaselineItemSystem.Description;
}

/// <summary>
/// 修改凡士林物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class VaselineHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<VaselineItemMarker>();
        if (marker == null) return;
        if (item.Stats?.rec == null || !item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
