using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 阻止原版武器/弹药/弹匣在世界生成、商人出售和合成配方中出现。
///
/// 需要拦截的三个系统：
/// 1. ItemLootPool.InitializePool — 物品战利池（世界容器和随机掉落的物品来源）
///    池是 Dictionary&lt;category, List&lt;itemId&gt;&gt;，InitializePool 从 Item.GlobalItems 按 category 分组
///    Postfix 在池初始化后移除被封禁的物品ID
///
/// 2. TraderScript.GenerateInventory — 商人库存生成
///    character==1（武器商）硬编码添加 smallmagazine/riflemagazine/boxof12gauge + pistol/rifle/shotgun
///    其他商人通过 GenerateSingleItemList 从 ItemLootPool 按类别随机选
///    Postfix 在库存生成后移除被封禁的 TraderItem
///
/// 3. Recipes.SetUpRecipes — 合成配方
///    Postfix 在配方加载后移除 result.id 为被封禁物品的配方
///
/// 被封禁的原版物品ID：
/// - 弹药：556round, 9mmround, 12gauge, boxof12gauge
/// - 武器：pistol, rifle, shotgun, makeshiftrifle
/// - 弹匣：smallmagazine, riflemagazine
/// </summary>
public static class VanillaBlockPatch
{
    /// <summary>被封禁的原版物品ID集合</summary>
    internal static readonly HashSet<string> BlockedVanillaIds = new(StringComparer.OrdinalIgnoreCase)
    {
        // 弹药
        "556round",
        "9mmround",
        "12gauge",
        "boxof12gauge",
        // 武器
        "pistol",
        "rifle",
        "shotgun",
        "makeshiftrifle",
        // 弹匣
        "smallmagazine",
        "riflemagazine",
    };

    /// <summary>判断物品ID是否被封禁</summary>
    public static bool IsBlocked(string itemId) => BlockedVanillaIds.Contains(itemId);

    // === 1. 物品战利池拦截 ===

    [HarmonyPatch(typeof(ItemLootPool), nameof(ItemLootPool.InitializePool))]
    public static class ItemLootPoolPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                // ItemLootPool.pool 是 static Dictionary<string, List<string>>
                // key = category, value = 该类别下所有物品ID列表
                var poolField = AccessTools.Field(typeof(ItemLootPool), "pool");
                if (poolField == null)
                {
                    Plugin.Log.LogWarning("[VanillaBlock] Could not find ItemLootPool.pool field.");
                    return;
                }

                var pool = poolField.GetValue(null) as Dictionary<string, List<string>>;
                if (pool == null)
                {
                    Plugin.Log.LogWarning("[VanillaBlock] ItemLootPool.pool is null.");
                    return;
                }

                int removedTotal = 0;
                foreach (var categoryList in pool.Values)
                {
                    removedTotal += categoryList.RemoveAll(id => IsBlocked(id));
                }

                Plugin.Log.LogInfo($"[VanillaBlock] Removed {removedTotal} blocked vanilla items from ItemLootPool.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[VanillaBlock] ItemLootPool patch failed: {ex}");
            }
        }
    }

    // === 2. 商人库存拦截 ===

    [HarmonyPatch(typeof(TraderScript), nameof(TraderScript.GenerateInventory))]
    public static class TraderInventoryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TraderScript __instance)
        {
            try
            {
                // TraderScript.items 是 List<TraderItem>
                var itemsField = AccessTools.Field(typeof(TraderScript), "items");
                if (itemsField == null)
                {
                    Plugin.Log.LogWarning("[VanillaBlock] Could not find TraderScript.items field.");
                    return;
                }

                var items = itemsField.GetValue(__instance) as List<TraderItem>;
                if (items == null)
                {
                    Plugin.Log.LogWarning("[VanillaBlock] TraderScript.items is null.");
                    return;
                }

                // TraderItem.id 是物品ID字段
                var idField = AccessTools.Field(typeof(TraderItem), "id");
                if (idField == null)
                {
                    Plugin.Log.LogWarning("[VanillaBlock] Could not find TraderItem.id field.");
                    return;
                }

                int removed = 0;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    var traderItem = items[i];
                    var itemId = idField.GetValue(traderItem) as string;
                    if (itemId != null && IsBlocked(itemId))
                    {
                        items.RemoveAt(i);
                        removed++;
                    }
                }

                if (removed > 0)
                    Plugin.Log.LogInfo($"[VanillaBlock] Removed {removed} blocked vanilla items from trader (character={__instance.character}).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[VanillaBlock] TraderInventory patch failed: {ex}");
            }
        }
    }

    // Note: Recipe blocking is handled inside RecipePatch.Postfix (same SetUpRecipes target),
    // to ensure correct execution order (block first, then add custom recipes, then re-index).

    // === 3. Utils.Create 拦截（安全网） ===
    // Utils.Create(string id, Vector2 pos, float rot) 通过 Resources.Load 加载预制体并实例化
    // 拦截被封禁的 ID，直接返回 null 不创建

    [HarmonyPatch(typeof(Utils), "Create", typeof(string), typeof(Vector2), typeof(float))]
    public static class UtilsCreateBlockPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(string id, ref GameObject __result)
        {
            if (id != null && IsBlocked(id))
            {
                Plugin.Log.LogInfo($"[VanillaBlock] Blocked Utils.Create for '{id}'.");
                __result = null;
                return false;
            }
            return true;
        }
    }

    // === 4. Item.Start 拦截（终极防线） ===
    // GenerateCollapsedPods/GenerateLifePods 实例化的预制体（如 LifepodCollapsed）包含子物体
    // 这些子物体带有 Item 组件，id 字段在预制体中已序列化
    // Item.Start 在物品完全初始化后被调用，此时可以安全销毁被封禁的物品
    // Item.Start 会将物品加入 Item.allItems 静态列表，销毁前需先移除

    [HarmonyPatch(typeof(Item), "Start")]
    public static class ItemStartBlockPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Item __instance)
        {
            try
            {
                if (string.IsNullOrEmpty(__instance.id) || !IsBlocked(__instance.id))
                    return;

                // 从 allItems 静态列表中移除（Start 刚添加了它）
                var allItemsField = AccessTools.Field(typeof(Item), "allItems");
                if (allItemsField != null)
                {
                    var allItems = allItemsField.GetValue(null) as List<Item>;
                    allItems?.Remove(__instance);
                }

                Plugin.Log.LogInfo($"[VanillaBlock] Destroyed blocked item '{__instance.id}' spawned in world (likely prefab child).");
                UnityEngine.Object.Destroy(__instance.gameObject);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[VanillaBlock] Item.Start block patch failed: {ex}");
            }
        }
    }
}
