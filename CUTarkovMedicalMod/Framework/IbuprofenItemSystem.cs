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
/// 布洛芬止痛药系统。
/// 非甾体抗炎药（NSAID），用于治疗疼痛、发烧和炎症。对犬科动物毒性较大。
/// 液体药品，容器 10ml，每次使用 2ml（5 次）。
/// 效果：抵抗力+50持续7分钟；1分钟内感染线性减少到15%；体温-2°C；耐力恢复+20%持续7分钟。
/// 副作用：心情-3；第7、10分钟各有10%概率呕吐。
/// 过量：10分钟内服用第二次→延迟1分钟后1分钟内胸头疼痛+70、肌肉-50；2分钟后每秒-2大脑完整度持续2分钟。
/// 模式：物品栏饮用（useAction）。
/// </summary>
public static class IbuprofenItemSystem
{
    public const string ItemKey = "ibuprofen";
    public const string BaseGameItemId = "bruisekit";
    public const string LiquidId = "ibuprofen_liquid";

    public static string DisplayName => I18n.Tr("ibuprofen.name");
    public static string Description => I18n.Tr("ibuprofen.desc");

    private const float TotalMl = 10f;
    private const float MlPerUse = 2f;

    // 效果常量
    private const float ImmunityDuration = 420f;      // 7 分钟
    internal const float InfectionReduceDuration = 60f; // 1 分钟
    internal const float InfectionTargetRatio = 0.15f;  // 减少到 15%
    private const float TempReduce = 2f;               // 体温 -2°C
    internal const float PainReduceDuration = 30f;      // 半分钟
    internal const float PainTargetRatio = 0.3f;        // 疼痛减少到 30%
    internal const float StaminaRecoveryDuration = 420f; // 7 分钟
    internal const float StaminaRecoveryBonus = 0.2f;   // +20% 耐力恢复
    internal const float SepsisReduceDuration = 60f;     // 1 分钟线性
    internal const float SepsisReduceAmount = 20f;

    // 副作用常量
    private const float HappinessCost = 3f;
    internal const float VomitChance = 0.10f;           // 10%

    // 过量常量
    internal const float OverdoseWindow = 600f;          // 10 分钟内再次服用触发过量
    internal const float OverdosePainDelay = 60f;        // 延迟 1 分钟
    internal const float OverdosePainDuration = 60f;     // 持续 1 分钟
    internal const float OverdosePainAmount = 70f;       // 疼痛 +70
    internal const float OverdoseMuscleAmount = 20f;     // 肌肉 -20
    internal const float OverdoseVomitDelay = 60f;       // 延迟 1 分钟
    internal const float OverdoseVomitDuration = 240f;   // 持续 4 分钟
    internal const float OverdoseVomitInterval = 60f;    // 每分钟呕吐一次

    // 三重过量常量（过量效果开始后10分钟内再次服用）
    internal const float TripleOverdoseWindow = 600f;         // 10 分钟内再次服用触发三重过量
    internal const float TripleOverdosePainDelay = 60f;      // 延迟 1 分钟
    internal const float TripleOverdosePainDuration = 60f;   // 持续 1 分钟
    internal const float TripleOverdosePainAmount = 100f;    // 疼痛 +100
    internal const float TripleOverdoseMuscleAmount = 50f;   // 肌肉 -50
    internal const float TripleOverdoseBrainDelay = 120f;    // 2 分钟后
    internal const float TripleOverdoseBrainDuration = 120f; // 持续 2 分钟
    internal const float TripleOverdoseBrainLossPerSec = 2f; // 每秒 -2 大脑完整度

    // 银色
    internal static readonly Color SilverColor = new Color(0.75f, 0.75f, 0.78f, 1f);

    private static Sprite? _cachedIcon;

    public static bool IsIbuprofenRequest(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsIbuprofenRequest(request)) return;

        EnsureRegisteredInItemTable();
        EnsureLiquidRegistered();
        InjectMoodleIcon();

        item.id = ItemKey;
        item.SetCondition(1f);

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

        var marker = item.gameObject.GetComponent<IbuprofenItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<IbuprofenItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite, 2f);
                if (adjusted != null)
                {
                    adjusted.name = "ibuprofen-icon";
                    sr.sprite = adjusted;
                }
                else
                {
                    sr.sprite = icon;
                }
            }
        }

        Plugin.Log.LogInfo($"[Ibuprofen] Configured spawned item '{ItemKey}' (condition={item.condition}).");
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
            clone.weight = 0.3f;
            clone.value = 8;
            clone.usable = true;
            clone.usableOnLimb = false;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            clone.capacity = TotalMl;
            clone.autoFill = false;
            clone.defaultContents = new List<LiquidStack>
            {
                new LiquidStack(LiquidId, TotalMl)
            };

            clone.useAction = IbuprofenUseAction;
            clone.useLimbAction = null;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered Ibuprofen ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register Ibuprofen: {ex.Message}");
            return false;
        }
    }

    private static void IbuprofenUseAction(Body body, Item item)
    {

        try
        {
            EnsureLiquidRegistered();

            var wat = item.GetComponent<WaterContainerItem>();
            if (wat == null)
            {
                Plugin.Log.LogWarning("[Ibuprofen] WaterContainerItem not found on item!");
                return;
            }

            wat.Drink(body, MlPerUse, "drink");
            PlayUseSound(item, "bottle");
            Plugin.Log.LogInfo($"[Ibuprofen] Used {MlPerUse}ml, effects applied.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[Ibuprofen] Failed to use: {ex.Message}");
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

    private static void EnsureLiquidRegistered()
    {
        // 注册液体数据（通过 CUCoreLib 支持多人网络同步）
        if (!Liquids.Registry.ContainsKey(LiquidId))
        {
            LiquidRegistry.Register(LiquidId, new CustomLiquidInfo
            {
                name = "Ibuprofen",
                color = SilverColor,
                valuePerLiter = 80f,
                injectable = false,
                injectionSickness = 0f,
                healthUsable = false,
            });
            Plugin.Log.LogInfo($"[Ibuprofen] Registered custom liquid '{LiquidId}' in Liquids.Registry.");
        }

        // 每次都重设回调——CUCoreLib 的 ApplyNetworkSnapshot 会在网络同步时
        // 用无回调的 LiquidType 覆盖 Liquids.Registry，导致 onDrink 变空。
        var lt = Liquids.Registry[LiquidId];
        lt.onDrink = delegate(float ml, Body body)
            {
                if (body == null) return;

                // === 立即效果 ===
                // 1) 抵抗力 +50 持续 7 分钟（通过 ImmunityBonusManager，支持多来源叠加）
                ImmunityBonusManager.AddBonus(body, 50f, ImmunityDuration, ItemKey);

                // 4) 耐力恢复 +20% 持续 7 分钟（通过 StaminaBonusManager，支持多来源叠加）
                StaminaBonusManager.AddBonus(body, StaminaRecoveryBonus, StaminaRecoveryDuration, ItemKey);

                // 3) 心情 -3
                body.happiness = Mathf.Max(-100f, body.happiness - HappinessCost);

                // === 延迟效果（通过效果控制器处理）===
                IbuprofenEffectController.Attach(body).Activate();

                Plugin.Log.LogInfo($"[Ibuprofen] Effects applied: immunity+50 for {ImmunityDuration}s, sepsis-20, happiness-{HappinessCost}.");
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
            weight = 0.3f,
            value = 8,
            tags = "drug,medicine,medical,stim,combine,craft",
            useAction = IbuprofenUseAction,
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
            var iconPath = Path.Combine(assetDir, "ibuprofen.png");
            bool found = File.Exists(iconPath);

            if (!found)
            {
                iconPath = Path.Combine(assetDir, "ibuprofen.webp");
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
            _cachedIcon.name = "ibuprofen-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load Ibuprofen icon: {ex.Message}");
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

    public static Sprite? TryGetIbuprofenIcon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetIbuprofenIcon();
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
/// 布洛芬物品标记组件。
/// </summary>
public sealed class IbuprofenItemMarker : MonoBehaviour
{
    public string itemKey = IbuprofenItemSystem.ItemKey;
    public string displayName = IbuprofenItemSystem.DisplayName;
    public string description = IbuprofenItemSystem.Description;
}

/// <summary>
/// 布洛芬效果控制器。
/// 正常效果：1分钟内感染减少到15%；第7/10分钟各有10%概率呕吐；7分钟后清除免疫力。
/// 过量效果（10分钟内第二次服用）：
///   t=60-120s: 胸部头部疼痛+70、肌肉-50（线性）
///   t=120-240s: 每秒-2大脑完整度
/// </summary>
public sealed class IbuprofenEffectController : MonoBehaviour
{
    private Body? _body;
    private float _timer;

    // 感染减少
    private Dictionary<Limb, float> _initialInfections = new();
    private bool _infectionReduceActive;
    private float _infectionReduceRemaining;

    // 疼痛减少
    private Dictionary<Limb, float> _initialPains = new();
    private bool _painReduceActive;
    private float _painReduceRemaining;

    // 呕吐时间点
    private static readonly float[] VomitTimes = { 420f, 600f }; // 7min, 10min
    private bool[] _vomitChecked = { false, false };

    // 败血症线性减少
    private bool _sepsisReduceActive;
    private float _sepsisReduceRemaining;

    // 过量效果
    private bool _overdoseTriggered;
    private bool _overdosePainActive;
    private float _overdosePainTimer;
    private List<Limb> _vitalLimbs = new();
    private bool _overdoseVomitActive;
    private float _overdoseVomitTimer;

    // 三重过量
    private bool _tripleOverdoseTriggered;
    private bool _triplePainActive;
    private float _triplePainTimer;
    private bool _tripleBrainActive;
    private float _tripleBrainTimer;
    private float _tripleBrainLossAccumulator;

    // 上次使用时间（实例字段，每个 Body 独立跟踪）
    private float _lastUseTime = -9999f;
    // 上次过量触发时间（实例字段，每个 Body 独立跟踪）
    private float _lastOverdoseTime = -9999f;

    // 总持续时间
    private const float NormalDuration = 620f; // 10min + 余量
    private const float OverdoseDuration = 320f; // 5min + 余量
    private const float TripleOverdoseDuration = 320f; // 5min + 余量

    public static IbuprofenEffectController Attach(Body body)
    {
        var controller = body.gameObject.GetComponent<IbuprofenEffectController>();
        if (controller == null)
            controller = body.gameObject.AddComponent<IbuprofenEffectController>();
        controller._body = body;
        return controller;
    }

    public void Activate()
    {
        float now = Time.time;
        bool overdose = (now - _lastUseTime) < IbuprofenItemSystem.OverdoseWindow;

        _timer = 0f;

        // 感染减少
        _infectionReduceActive = true;
        _infectionReduceRemaining = IbuprofenItemSystem.InfectionReduceDuration;
        _initialInfections.Clear();

        // 疼痛减少
        _painReduceActive = true;
        _painReduceRemaining = IbuprofenItemSystem.PainReduceDuration;
        _initialPains.Clear();
        _vitalLimbs.Clear();

        if (_body != null && _body.limbs != null)
        {
            foreach (var limb in _body.limbs)
            {
                if (limb == null || limb.dismembered) continue;
                _initialInfections[limb] = limb.infectionAmount;
                _initialPains[limb] = limb.pain;

                // 收集胸部和头部肢体（用于过量效果）
                if (limb.isHead || (limb.isVital && !limb.isHead))
                    _vitalLimbs.Add(limb);
            }
        }

        // 呕吐检查重置
        _vomitChecked[0] = false;
        _vomitChecked[1] = false;

        // 败血症线性减少
        _sepsisReduceActive = true;
        _sepsisReduceRemaining = IbuprofenItemSystem.SepsisReduceDuration;

        // 过量效果
        _overdoseTriggered = overdose;
        _overdosePainActive = false;
        _overdosePainTimer = 0f;
        _overdoseWarningShown = false;
        _overdoseVomitActive = false;
        _overdoseVomitTimer = 0f;

        // 三重过量检测：上次过量触发后10分钟内再次过量
        bool tripleOverdose = overdose && (now - _lastOverdoseTime) < IbuprofenItemSystem.TripleOverdoseWindow;
        _tripleOverdoseTriggered = tripleOverdose;
        _triplePainActive = false;
        _triplePainTimer = 0f;
        _tripleBrainActive = false;
        _tripleBrainTimer = 0f;
        _tripleBrainLossAccumulator = 0f;

        // 更新上次使用时间
        _lastUseTime = now;
        if (overdose)
            _lastOverdoseTime = now;

        enabled = true;

        if (tripleOverdose)
        {
            StimBuffIndicator.ShowOneTimeEffect(IbuprofenItemSystem.ItemKey, I18n.Tr("ibuprofen.ot.0"), isNegative: true);
            StimBuffIndicator.ShowOneTimeEffect(IbuprofenItemSystem.ItemKey, 
                string.Join("\n", I18n.TrAll("ibuprofen.neg.0", "ibuprofen.neg.1", "ibuprofen.neg.2", "ibuprofen.neg.3")), 
                isNegative: true);
            Plugin.Log.LogWarning("[Ibuprofen] TRIPLE OVERDOSE TRIGGERED! Third dose within overdose window.");
        }
        else if (overdose)
        {
            // 显示过量警告
            StimBuffIndicator.ShowOneTimeEffect(IbuprofenItemSystem.ItemKey, I18n.Tr("ibuprofen.ot.1"), isNegative: true);
            Plugin.Log.LogWarning("[Ibuprofen] OVERDOSE TRIGGERED! Second dose within 10 minutes.");
        }

        Plugin.Log.LogInfo($"[Ibuprofen] Effect controller activated. Overdose={overdose}.");
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

        // === 正常效果 ===

        // 感染线性减少
        if (_infectionReduceActive)
        {
            _infectionReduceRemaining -= Time.deltaTime;
            float progress = Mathf.Clamp01(1f - _infectionReduceRemaining / IbuprofenItemSystem.InfectionReduceDuration);

            foreach (var kvp in _initialInfections)
            {
                var limb = kvp.Key;
                if (limb == null || limb.dismembered) continue;
                float initial = kvp.Value;
                float target = initial * IbuprofenItemSystem.InfectionTargetRatio;
                limb.infectionAmount = Mathf.Lerp(initial, target, progress);
            }

            if (_infectionReduceRemaining <= 0f)
            {
                _infectionReduceActive = false;
                foreach (var kvp in _initialInfections)
                {
                    var limb = kvp.Key;
                    if (limb == null || limb.dismembered) continue;
                    limb.infectionAmount = kvp.Value * IbuprofenItemSystem.InfectionTargetRatio;
                }
                Plugin.Log.LogInfo("[Ibuprofen] Infection reduce complete (60s -> 50%).");
            }
        }

        // 疼痛线性减少（半分钟内减少到30%）
        if (_painReduceActive)
        {
            _painReduceRemaining -= Time.deltaTime;
            float progress = Mathf.Clamp01(1f - _painReduceRemaining / IbuprofenItemSystem.PainReduceDuration);

            foreach (var kvp in _initialPains)
            {
                var limb = kvp.Key;
                if (limb == null || limb.dismembered) continue;
                float initial = kvp.Value;
                float target = initial * IbuprofenItemSystem.PainTargetRatio;
                limb.pain = Mathf.Lerp(initial, target, progress);
            }

            if (_painReduceRemaining <= 0f)
            {
                _painReduceActive = false;
                foreach (var kvp in _initialPains)
                {
                    var limb = kvp.Key;
                    if (limb == null || limb.dismembered) continue;
                    limb.pain = kvp.Value * IbuprofenItemSystem.PainTargetRatio;
                }
                Plugin.Log.LogInfo("[Ibuprofen] Pain reduce complete (30s -> 30%).");
            }
        }

        // 败血症线性减少（1分钟-20）
        if (_sepsisReduceActive && _body != null)
        {
            _sepsisReduceRemaining -= Time.deltaTime;
            float reducePerSec = IbuprofenItemSystem.SepsisReduceAmount / IbuprofenItemSystem.SepsisReduceDuration;
            _body.septicShock = Mathf.Max(0f, _body.septicShock - reducePerSec * Time.deltaTime);

            if (_sepsisReduceRemaining <= 0f)
            {
                _sepsisReduceActive = false;
                Plugin.Log.LogInfo("[Ibuprofen] Sepsis reduce complete (60s -> -20).");
            }
        }

        // 呕吐检查
        for (int i = 0; i < VomitTimes.Length; i++)
        {
            if (!_vomitChecked[i] && _timer >= VomitTimes[i])
            {
                _vomitChecked[i] = true;
                float roll = UnityEngine.Random.value;
                Plugin.Log.LogInfo($"[Ibuprofen] Vomit check at {VomitTimes[i] / 60f:F0}min: roll={roll:F3}, threshold={IbuprofenItemSystem.VomitChance}");
                if (roll < IbuprofenItemSystem.VomitChance)
                {
                    try { _body?.vomiter?.Vomit(); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Ibuprofen] Vomit failed: {ex.Message}"); }
                }
            }
        }

        // 耐力恢复 +20%（仅当本物品是当前最强来源时才每帧追加，避免多来源重复叠加）
        if (_body != null && _body.staminaStrength != null
            && StaminaBonusManager.IsTopSource(_body, IbuprofenItemSystem.ItemKey))
        {
            var extraRecovery = _body.staminaStrength.Evaluate(_body.energy * 0.01f) * Time.deltaTime * IbuprofenItemSystem.StaminaRecoveryBonus;
            _body.stamina += extraRecovery;
        }

        // === 过量效果 ===
        if (_overdoseTriggered)
        {
            UpdateOverdose();
        }

        // === 结束判定 ===
        float totalDuration = _tripleOverdoseTriggered
            ? Mathf.Max(NormalDuration, TripleOverdoseDuration)
            : _overdoseTriggered
                ? Mathf.Max(NormalDuration, OverdoseDuration)
                : NormalDuration;

        if (_timer >= totalDuration)
        {
            if (_body != null)
            {
                ImmunityBonusManager.ClearBonus(_body, IbuprofenItemSystem.ItemKey);
                StaminaBonusManager.ClearBonus(_body, IbuprofenItemSystem.ItemKey);
            }
            enabled = false;
            Plugin.Log.LogInfo("[Ibuprofen] All effects ended.");
        }
    }

    private bool _overdoseWarningShown;

    private void UpdateOverdose()
    {
        if (_body == null) return;

        // 显示一次性过量警告
        if (!_overdoseWarningShown)
        {
            _overdoseWarningShown = true;
            StimBuffIndicator.ShowOneTimeEffect(IbuprofenItemSystem.ItemKey, I18n.Tr("ibuprofen.ot.1"), isNegative: true);
        }

        // 阶段1: t=60-120s 疼痛+70、肌肉-20（线性）
        if (_timer >= IbuprofenItemSystem.OverdosePainDelay && _timer < IbuprofenItemSystem.OverdosePainDelay + IbuprofenItemSystem.OverdosePainDuration)
        {
            if (!_overdosePainActive)
            {
                _overdosePainActive = true;
                _overdosePainTimer = 0f;
                Plugin.Log.LogInfo("[Ibuprofen] Overdose phase 1: pain+70, muscle-20 to chest/head (60s).");
            }

            _overdosePainTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(_overdosePainTimer / IbuprofenItemSystem.OverdosePainDuration);
            float painDelta = IbuprofenItemSystem.OverdosePainAmount * (progress * Time.deltaTime / IbuprofenItemSystem.OverdosePainDuration);
            float muscleDelta = IbuprofenItemSystem.OverdoseMuscleAmount * (progress * Time.deltaTime / IbuprofenItemSystem.OverdosePainDuration);

            foreach (var limb in _vitalLimbs)
            {
                if (limb == null || limb.dismembered) continue;
                limb.pain += painDelta;
                limb.muscleHealth = Mathf.Max(0f, limb.muscleHealth - muscleDelta);
            }
        }

        // 阶段2: t=60-300s 每分钟呕吐一次（延迟1分钟，持续4分钟）
        if (_timer >= IbuprofenItemSystem.OverdoseVomitDelay && _timer < IbuprofenItemSystem.OverdoseVomitDelay + IbuprofenItemSystem.OverdoseVomitDuration)
        {
            if (!_overdoseVomitActive)
            {
                _overdoseVomitActive = true;
                _overdoseVomitTimer = 0f;
                // 立即呕吐一次
                try { _body.vomiter?.Vomit(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Ibuprofen] Overdose vomit failed: {ex.Message}"); }
                Plugin.Log.LogInfo("[Ibuprofen] Overdose vomit phase started (every 60s for 4min).");
            }

            _overdoseVomitTimer += Time.deltaTime;
            if (_overdoseVomitTimer >= IbuprofenItemSystem.OverdoseVomitInterval)
            {
                _overdoseVomitTimer -= IbuprofenItemSystem.OverdoseVomitInterval;
                try { _body.vomiter?.Vomit(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Ibuprofen] Overdose vomit failed: {ex.Message}"); }
                Plugin.Log.LogInfo("[Ibuprofen] Overdose periodic vomit triggered.");
            }
        }

        // 过量效果结束
        if (_timer >= OverdoseDuration && _overdoseTriggered)
        {
            _overdoseTriggered = false;
            Plugin.Log.LogInfo("[Ibuprofen] Overdose effects ended.");
        }

        // === 三重过量效果 ===
        if (_tripleOverdoseTriggered)
        {
            UpdateTripleOverdose();
        }
    }

    private void UpdateTripleOverdose()
    {
        if (_body == null) return;

        // 三重过量阶段1: t=60-120s 疼痛+100、肌肉-50（线性）
        if (_timer >= IbuprofenItemSystem.TripleOverdosePainDelay && _timer < IbuprofenItemSystem.TripleOverdosePainDelay + IbuprofenItemSystem.TripleOverdosePainDuration)
        {
            if (!_triplePainActive)
            {
                _triplePainActive = true;
                _triplePainTimer = 0f;
                Plugin.Log.LogInfo("[Ibuprofen] Triple overdose phase 1: pain+100, muscle-50 to chest/head (60s).");
            }

            _triplePainTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(_triplePainTimer / IbuprofenItemSystem.TripleOverdosePainDuration);
            float painDelta = IbuprofenItemSystem.TripleOverdosePainAmount * (progress * Time.deltaTime / IbuprofenItemSystem.TripleOverdosePainDuration);
            float muscleDelta = IbuprofenItemSystem.TripleOverdoseMuscleAmount * (progress * Time.deltaTime / IbuprofenItemSystem.TripleOverdosePainDuration);

            foreach (var limb in _vitalLimbs)
            {
                if (limb == null || limb.dismembered) continue;
                limb.pain += painDelta;
                limb.muscleHealth = Mathf.Max(0f, limb.muscleHealth - muscleDelta);
            }
        }

        // 三重过量阶段2: t=120-240s 每秒-2大脑完整度
        if (_timer >= IbuprofenItemSystem.TripleOverdoseBrainDelay && _timer < IbuprofenItemSystem.TripleOverdoseBrainDelay + IbuprofenItemSystem.TripleOverdoseBrainDuration)
        {
            if (!_tripleBrainActive)
            {
                _tripleBrainActive = true;
                _tripleBrainTimer = 0f;
                Plugin.Log.LogInfo("[Ibuprofen] Triple overdose phase 2: brainHealth-2/s (120s).");
            }

            _tripleBrainTimer += Time.deltaTime;

            _tripleBrainLossAccumulator += Time.deltaTime * IbuprofenItemSystem.TripleOverdoseBrainLossPerSec;
            while (_tripleBrainLossAccumulator >= 1f)
            {
                _tripleBrainLossAccumulator -= 1f;
                _body.brainHealth = Mathf.Max(0f, _body.brainHealth - 1f);
            }
        }

        // 三重过量结束
        if (_timer >= TripleOverdoseDuration && _tripleOverdoseTriggered)
        {
            _tripleOverdoseTriggered = false;
            Plugin.Log.LogInfo("[Ibuprofen] Triple overdose effects ended.");
        }
    }

    private void OnDisable()
    {
        // 不清除 antibioticImmunityTime，游戏会自动递减
        StimBuffIndicator.HideBuff(IbuprofenItemSystem.ItemKey);
    }
}

/// <summary>
/// 修改布洛芬物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class IbuprofenHoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<IbuprofenItemMarker>();
        if (marker == null) return;
        if (item.Stats?.rec == null || !item.Stats.rec.recognizable) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}
