using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// AI-2 急救组合注射器系统。
/// 苏联制式急救装备，多剂量注射器（100ml，每次10ml）。
/// 效果（每10ml）：辐射-1Gy，阿片+0.2，内出血-2%。
/// 副作用（每10ml）：+3患病，-10%免疫力，-1水分饱食度。
/// 模板：基于原版 syringe。
/// </summary>
public static class AI2ItemSystem
{
    public const string ItemKey = "ai2";
    public const string BaseGameItemId = "syringe";
    public const string LiquidId = "ai2_liquid";

    public static string DisplayName => I18n.Tr("ai2.name");
    public static string Description => I18n.Tr("ai2.desc");

    private const float MlPerUse = 10f;
    private const float TotalMl = 100f;
    private const float ConditionPerUse = MlPerUse / TotalMl; // 0.1

    // 效果常量（每10ml）
    private const float RadiationReduceInternal = 3.3f;   // -1Gy（内部~3.3:1换算）
    private const float OpiateDosePerUse = 0.2f;           // 阿片+0.2
    private const float InternalBleedReduce = 2f;        // 内出血每10ml减2（范围0-25）

    // 副作用常量（每10ml）
    private const float SicknessPerUse = 3f;                // +3患病
    private const float ImmunityReduceAmount = 10f;         // -10免疫力（百分比单位）
    private const float ImmunityReduceDuration = 300f;      // 持续5分钟
    private const float FoodWaterCost = 1f;                 // -1水分饱食度

    // 乳白色
    internal static readonly Color MilkyWhite = new Color(0.95f, 0.92f, 0.85f, 1f);

    private static Sprite? _cachedIcon;

    public static bool IsAi2Request(MedicalGrantRequest request)
        => request.ItemKey.Equals(ItemKey, StringComparison.OrdinalIgnoreCase);

    public static void ConfigureSpawnedItem(Item item, MedicalGrantRequest request)
    {
        if (!IsAi2Request(request)) return;

        EnsureRegisteredInItemTable();
        EnsureLiquidRegistered();
        InjectMoodleIcon();

        item.id = ItemKey;

        // 填充 WaterContainerItem（预制件已含此组件，但 "syringe" defaultContents 为空）
        var wat = item.GetComponent<WaterContainerItem>();
        if (wat != null)
        {
            wat.stack = new List<LiquidStack> { new LiquidStack(LiquidId, TotalMl) };
            wat.UpdateCondition();
        }
        else
        {
            // 如果预制件没有 WaterContainerItem，手动添加
            wat = item.gameObject.AddComponent<WaterContainerItem>();
            wat.stack = new List<LiquidStack> { new LiquidStack(LiquidId, TotalMl) };
        }

        var marker = item.gameObject.GetComponent<AI2ItemMarker>();
        if (marker == null)
            marker = item.gameObject.AddComponent<AI2ItemMarker>();

        var icon = TryLoadIcon();
        if (icon != null)
        {
            var sr = item.gameObject.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var adjusted = CreateSpriteMatchingBaseSize(icon.texture, sr.sprite);
                if (adjusted != null)
                {
                    adjusted.name = "ai2-icon";
                    sr.sprite = adjusted;
                }
                else
                {
                    sr.sprite = icon;
                }
            }
            Plugin.Log.LogInfo($"[AI-2] Icon loaded successfully: {icon.texture.width}x{icon.texture.height}");
        }
        else
        {
            Plugin.Log.LogWarning("[AI-2] Icon failed to load, keeping default sprite.");
        }

        Plugin.Log.LogInfo($"[AI-2] Configured spawned item '{ItemKey}' (condition={item.condition}).");
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
            clone.usable = false;
            clone.usableOnLimb = true;
            clone.usableWithLMB = false;
            clone.combineable = true;
            clone.destroyAtZeroCondition = true;
            clone.scaleWeightWithCondition = false;
            clone.tags = MergeTags(clone.tags, "drug,medicine,medical,stim,combine,craft");
            clone.SetTags();

            // LiquidItemInfo 字段
            clone.capacity = TotalMl; // 100ml
            clone.autoFill = false;
            clone.defaultContents = new List<LiquidStack>
            {
                new LiquidStack(LiquidId, TotalMl)
            };

            // 使用 useLimbAction 启动 SyringeMinigame，像原生注射器一样在医疗界面按需注射
            clone.useAction = null;
            clone.useLimbAction = Ai2UseLimbAction;

            map[ItemKey] = clone;
            Plugin.Log.LogInfo("Registered AI-2 ItemInfo with custom useAction delegate.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to register AI-2: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// AI-2 肢体使用 — 启动 SyringeMinigame，像原生注射器一样在医疗界面按需注射。
    /// 通过 WaterContainerItem.Inject 消耗液体并触发 onHealthUse 回调。
    /// </summary>
    private static void Ai2UseLimbAction(Limb limb, Item item)
    {
        try
        {
            EnsureLiquidRegistered();

            var wat = item.GetComponent<WaterContainerItem>();
            if (wat == null)
            {
                Plugin.Log.LogWarning("[AI-2] WaterContainerItem not found on item!");
                return;
            }

            MinigameBase.main.StartMinigame(
                new SyringeMinigame(delegate(float mult)
                {
                    // mult = 注射深度(0~1) × Time.deltaTime
                    // wat.Inject 按 mult*100ml 消耗液体，自动调用 onHealthUse 应用效果
                    wat.Inject(limb, mult * TotalMl);
                }, limb, MilkyWhite),
                item);

            Plugin.Log.LogInfo($"[AI-2] SyringeMinigame started for limb {limb.name}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[AI-2] Failed to start SyringeMinigame: {ex.Message}");
        }
    }

    #region Helper Methods

    /// <summary>
    /// 注册自定义液体 "ai2_liquid" 到 Liquids.Registry。
    /// onHealthUse 回调按 ml 量应用 AI-2 的效果和副作用。
    /// </summary>
    private static void EnsureLiquidRegistered()
    {
        if (Liquids.Registry.ContainsKey(LiquidId)) return;

        Liquids.Registry[LiquidId] = new LiquidType
        {
            localeName = "ai2_liquid",
            color = MilkyWhite,
            valuePerLiter = 70f,
            injectable = true,
            injectionSickness = 0f,
            onHealthUse = delegate(float ml, Limb limb)
            {
                var body = limb.body;
                if (body == null) return;

                // ml 是注射的液体量（ml），per10ml = 每10ml的比例
                float per10ml = ml * 0.1f;

                // === 效果（每10ml）===
                // 1) 辐射 -1Gy（内部单位 -3.3）
                body.radiationSickness = Mathf.Max(0f, body.radiationSickness - RadiationReduceInternal * per10ml);

                // 2) 阿片 +0.2
                var pk = body.GetComponent<Painkillers>();
                if (pk == null)
                {
                    pk = body.gameObject.AddComponent<Painkillers>();
                    pk.opiateAmount = OpiateDosePerUse * per10ml;
                }
                else
                {
                    pk.opiateAmount += OpiateDosePerUse * per10ml;
                }

                // 3) 内出血 -2%
                body.internalBleeding = Mathf.Max(0f, body.internalBleeding - InternalBleedReduce * per10ml);

                // === 副作用（每10ml）===
                // 1) +3患病
                body.sicknessAmount += SicknessPerUse * per10ml;

                // 2) -10免疫力（百分比单位，通过 ImmunityReductionManager 持续应用，防止被游戏重算覆盖）
                ImmunityReductionManager.AddReduction(body, ImmunityReduceAmount * per10ml, ImmunityReduceDuration);

                // 3) -1水分饱食度
                body.Eat(-FoodWaterCost * per10ml, 0f);
                body.Drink(-FoodWaterCost * per10ml);
            }
        };

        Plugin.Log.LogInfo($"[AI-2] Registered custom liquid '{LiquidId}' in Liquids.Registry.");
    }


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
            value = 7,
            tags = "drug,medicine,medical,stim,combine,craft",
            useLimbAction = Ai2UseLimbAction,
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
            rec = source.rec,
            qualities = source.qualities,
            // LiquidItemInfo 字段（后续在 EnsureRegisteredInItemTable 中覆盖）
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
            Plugin.Log.LogInfo($"[AI-2] Looking for icon in: {assetDir}");

            var iconPath = Path.Combine(assetDir, "ai2.png");
            bool found = File.Exists(iconPath);
            Plugin.Log.LogInfo($"[AI-2] ai2.png exists: {found}");

            if (!found)
            {
                iconPath = Path.Combine(assetDir, "ai2.webp");
                found = File.Exists(iconPath);
                Plugin.Log.LogInfo($"[AI-2] ai2.webp exists: {found}");
                if (!found) return null;
            }

            var bytes = File.ReadAllBytes(iconPath);
            Plugin.Log.LogInfo($"[AI-2] Read {bytes.Length} bytes from {Path.GetFileName(iconPath)}");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                Plugin.Log.LogWarning($"[AI-2] ImageConversion.LoadImage failed for {Path.GetFileName(iconPath)}");
                return null;
            }
            Plugin.Log.LogInfo($"[AI-2] Texture loaded: {texture.width}x{texture.height}");

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            _cachedIcon = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 32f);
            _cachedIcon.name = "ai2-icon";
            return _cachedIcon;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Failed to load AI-2 icon: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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

    public static Sprite? TryGetAI2Icon() => TryLoadIcon();

    public static void InjectMoodleIcon()
    {
        var icon = TryGetAI2Icon();
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
/// AI-2 物品标记组件。
/// </summary>
public sealed class AI2ItemMarker : MonoBehaviour
{
    public string itemKey = AI2ItemSystem.ItemKey;
    public string displayName = AI2ItemSystem.DisplayName;
    public string description = AI2ItemSystem.Description;
}

/// <summary>
/// 修改 AI-2 物品悬浮提示。
/// </summary>
[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.ItemHoverDescription))]
public static class AI2HoverPatch
{
    [HarmonyPostfix]
    public static void Postfix(Item item, ref ValueTuple<string, string> __result)
    {
        if (item == null) return;

        var marker = item.GetComponent<AI2ItemMarker>();
        if (marker == null) return;

        __result.Item1 = marker.displayName;
        HoverDescriptionHelper.StripEffectsWhenNotExpanded(ref __result);
    }
}

/// <summary>
/// 阻止从 AI-2 注射器倒出液体到其他容器。
/// 游戏 CanCombine 对两个 WaterContainerItem 物品始终允许合并，
/// 因此需要 Prefix 拦截 CombineLiquids，检查源容器(wat2)是否为 AI-2 注射器。
/// </summary>
[HarmonyPatch(typeof(Body), nameof(Body.CombineLiquids))]
public static class AI2LiquidPourBlockPatch
{
    [HarmonyPrefix]
    public static bool Prefix(WaterContainerItem wat1, WaterContainerItem wat2)
    {
        if (wat2 != null && wat2.item != null)
        {
            var marker = wat2.item.GetComponent<AI2ItemMarker>();
            if (marker != null)
            {
                Plugin.Log.LogInfo("[AI-2] Blocked liquid pour attempt from AI-2 syringe.");
                return false;
            }
        }
        return true;
    }
}
