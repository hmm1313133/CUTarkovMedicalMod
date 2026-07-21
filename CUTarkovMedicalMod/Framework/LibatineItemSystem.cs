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
/// 力百汀系统。
/// 复合型广谱抗生素，用于治疗细菌感染。
/// 液体药品，容器 2ml，每次使用 2ml（1 次）。
/// 效果：抵抗力+80（ImmunityBonusManager）持续5分钟；1分钟内每个肢体感染线性减少到60%。
/// 副作用：心情-3；第5/7/10分钟各有5%概率呕吐。
/// 模式：物品栏饮用（useAction），基于抗生素模式。
/// </summary>
public static class LibatineItemSystem
{
    public const string ItemKey = "libatine";
    public const string BaseGameItemId = "bruisekit";
    public const string LiquidId = "libatine_liquid";

    public static string DisplayName => I18n.Tr("libatine.name");
    public static string Description => I18n.Tr("libatine.desc");

    private const float TotalMl = 2f;        // 容器容量
    private const float MlPerUse = 2f;       // 每次使用量

    // 效果常量
    private const float ImmunityDuration = 300f;       // 5 分钟
    private const float InfectionReduceDuration = 60f; // 1 分钟
    private const float InfectionTargetRatio = 0.6f;   // 减少到 60%

    // 副作用常量
    private const float HappinessCost = 3f;            // 心情 -3
    private const float VomitChance = 0.05f;           // 5% 概率

    // 银色
    internal static readonly Color SilverColor = new Color(0.75f, 0.75f, 0.78f, 1f);

    private static Sprite? _cachedIcon;

    public static bool IsLibatineRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsLibatineRequest(request)) return;

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

        var marker = item.gameObject.GetComponent<LibatineItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<LibatineItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite, 2f);
                if (adjusted != null)
                {
                    adjusted.name = "libatine-icon";
                    sr.sprite = adjusted;
                }
                else
                {
                    sr.sprite = icon;
                }
            }
        }

        Plugin.Log.LogInfo($"[Libatine] Configured spawned item '{ItemKey}' (condition={item.condition}).");
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
            clone.value = 7;
            clone.usable = true;
            clone.usableOnLimb = false;
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

            // useAction：饮用模式（物品栏直接使用）
            clone.useAction = LibatineUseAction;
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Libatine ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Libatine: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 物品栏使用 — 饮用液体，消耗 2ml 并触发 onDrink 回调。
    /// </summary>
    private static void LibatineUseAction(Body body, Item item)
    {

        try
        {
            EnsureLiquidRegistered();

            var wat = item.GetComponent<WaterContainerItem>();
            if (wat == null)
            {
                Plugin.Log.LogWarning("[Libatine] WaterContainerItem not found on item!");
                return;
            }

            // 饮用消耗液体并调用 onDrink 回调
            wat.Drink(body, MlPerUse, "drink");
            PlayUseSound(item, "bottle");
            Plugin.Log.LogInfo($"[Libatine] Used {MlPerUse}ml, effects applied.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Libatine] Failed to use: {ex.Message}");
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
    /// 注册自定义液体 "libatine_liquid" 到 Liquids.Registry。
    /// onDrink 回调应用抵抗力提升、感染减少和延迟呕吐副作用。
    /// </summary>
    private static void EnsureLiquidRegistered()
    {
        // 注册液体数据（通过 CUCoreLib 支持多人网络同步）
        if (!Liquids.Registry.ContainsKey(LiquidId))
        {
            LiquidRegistry.Register(LiquidId, new CustomLiquidInfo
            {
                name = "Libatine",
                color = SilverColor,
                valuePerLiter = 70f,
                injectable = false,
                injectionSickness = 0f,
                healthUsable = false,
            });
            Plugin.Log.LogInfo($"[Libatine] Registered custom liquid '{LiquidId}' in Liquids.Registry.");
        }

        // 每次都重设回调——CUCoreLib 的 ApplyNetworkSnapshot 会在网络同步时
        // 用无回调的 LiquidType 覆盖 Liquids.Registry，导致 onDrink 变空。
        var lt = Liquids.Registry[LiquidId];
        lt.onDrink = delegate(float ml, Body body)
            {
                if (body == null) return;

                // === 立即效果 ===
                // 1) 抵抗力 +80 持续 5 分钟（通过 ImmunityBonusManager，支持多来源叠加）
                ImmunityBonusManager.AddBonus(body, 80f, ImmunityDuration, ItemKey);

                // 2) 心情 -3
                body.happiness = Mathf.Max(-100f, body.happiness - HappinessCost);

                // === 延迟效果（通过效果控制器处理）===
                // - 1分钟内每个肢体感染线性减少到60%
                // - 第5/7/10分钟各有5%概率呕吐
                LibatineEffectController.Attach(body).Activate();

                Plugin.Log.LogInfo($"[Libatine] Effects applied: immunity+80 for {ImmunityDuration}s, infection reduce over {InfectionReduceDuration}s, happiness-{HappinessCost}.");
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
            usable = true,
            usableOnLimb = false,
            usableWithLMB = false,
            combineable = true,
            destroyAtZeroCondition = true,
            scaleWeightWithCondition = false,
            weight = 0.2f,
            value = 7,
            tags = "drug,medicine,medical,stim,combine,craft",
            useAction = LibatineUseAction,
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
            var iconPath = Path.Combine(assetDir, "libatine.png");
            bool found = File.Exists(iconPath);

            if (!found)
            {
                iconPath = Path.Combine(assetDir, "augmentin.webp");
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
            _cachedIcon.name = "libatine-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Libatine icon: {ex.Message}");
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

    public static Sprite? TryGetLibatineIcon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetLibatineIcon();
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
/// 力百汀物品标记组件。
/// </summary>
public sealed class LibatineItemMarker : MonoBehaviour
{
    public string itemKey = LibatineItemSystem.ItemKey;
    public string displayName = LibatineItemSystem.DisplayName;
    public string description = LibatineItemSystem.Description;
}

/// <summary>
/// 力百汀效果控制器。
/// 1) 1分钟内每个肢体感染线性减少到60%。
/// 2) 第5/7/10分钟各有5%概率呕吐。
/// 3) 5分钟后清除 antibioticImmunityTime。
/// </summary>
public sealed class LibatineEffectController : MonoBehaviour
{
    private Body? _body;
    private float _timer;

    // 感染减少
    private Dictionary<Limb, float> _initialInfections = new();
    private bool _infectionReduceActive;
    private float _infectionReduceRemaining;

    // 呕吐时间点（秒）
    private static readonly float[] VomitTimes = { 300f, 420f, 600f }; // 5min, 7min, 10min
    private bool[] _vomitChecked = { false, false, false };

    // 总持续时间（10分钟 + 余量）
    private const float TotalDuration = 620f;

    public static LibatineEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<LibatineEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<LibatineEffectController>();
        controller._body = body;
        return controller;
    }

    public void Activate()
    {
        _timer = 0f;
        _infectionReduceActive = true;
        _infectionReduceRemaining = 60f; // 1 分钟
        _vomitChecked[0] = false;
        _vomitChecked[1] = false;
        _vomitChecked[2] = false;

        // 记录每个肢体的初始感染值
        _initialInfections.Clear();
        if (_body != null && _body.limbs != null)
        {
            foreach (var limb in _body.limbs)
            {
                if (limb == null || limb.dismembered) continue;
                _initialInfections[limb] = limb.infectionAmount;
            }
        }

        enabled = true;
        Plugin.Log.LogInfo("[Libatine] Effect controller activated: infection reduce 60s, vomit checks at 5/7/10min.");
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null)
        {
            enabled = false;
            return;
        }

        _timer += Time.deltaTime;

        // === 感染线性减少 ===
        if (_infectionReduceActive)
        {
            _infectionReduceRemaining -= Time.deltaTime;
            float progress = Mathf.Clamp01(1f - _infectionReduceRemaining / 60f);

            foreach (var kvp in _initialInfections)
            {
                var limb = kvp.Key;
                if (limb == null || limb.dismembered) continue;
                float initial = kvp.Value;
                float target = initial * 0.6f;
                limb.infectionAmount = Mathf.Lerp(initial, target, progress);
            }

            if (_infectionReduceRemaining <= 0f)
            {
                _infectionReduceActive = false;
                // 确保最终值精确为60%
                foreach (var kvp in _initialInfections)
                {
                    var limb = kvp.Key;
                    if (limb == null || limb.dismembered) continue;
                    limb.infectionAmount = kvp.Value * 0.6f;
                }
                Plugin.Log.LogInfo("[Libatine] Infection reduce complete (60s -> 60%).");
            }
        }

        // === 延迟呕吐检查 ===
        for (int i = 0; i < VomitTimes.Length; i++)
        {
            if (!_vomitChecked[i] && _timer >= VomitTimes[i])
            {
                _vomitChecked[i] = true;
                float roll = UnityEngine.Random.value;
                Plugin.Log.LogInfo($"[Libatine] Vomit check at {VomitTimes[i] / 60f:F0}min: roll={roll:F3}, threshold={0.05f}");
                if (roll < 0.05f)
                {
                    try
                    {
                        _body.vomiter?.Vomit();
                        Plugin.Log.LogInfo($"[Libatine] Vomiting triggered (5% chance at {VomitTimes[i] / 60f:F0}min).");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[Libatine] Vomit failed: {ex.Message}");
                    }
                }
            }
        }

        // === 结束 ===
        if (_timer >= TotalDuration)
        {
            if (_body != null)
                ImmunityBonusManager.ClearBonus(_body, LibatineItemSystem.ItemKey);
            enabled = false;
            Plugin.Log.LogInfo("[Libatine] All effects ended.");
        }
    }

    private void OnDisable()
    {
        // 清除抵抗力加成
        if (_body != null)
            ImmunityBonusManager.ClearBonus(_body, LibatineItemSystem.ItemKey);
    }
}

/// <summary>
/// 修改力百汀物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class LibatineHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<LibatineItemMarker>();
        if (marker == null) return;
        if (item.Stats?.rec == null || !item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
