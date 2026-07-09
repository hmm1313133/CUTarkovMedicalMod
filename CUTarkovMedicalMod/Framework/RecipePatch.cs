using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 拦截 Recipes.SetUpRecipes - 在原版配方加载后追加自定义子弹合成配方。
///
/// 合成系统结构（反编译所得）：
/// - Recipes.recipes: 静态 List&lt;Recipe&gt;
/// - Recipe: INT(智力要求), result(RecipeResult), items(List&lt;RecipeItem&gt;), category(RecipeCategory)
/// - RecipeResult: id(物品ID), amount(数量,默认1), resultCondition(默认1)
/// - RecipeItem 两种匹配模式：
///   A) specific=true → 精确匹配 Item.id == specificId
///   B) specific=false → 性质匹配，检查物品 ItemInfo.qualities 列表是否包含 RecipeItem.quality
/// - CraftingQuality: struct { string id, float amount } — 性质ID+强度
///   原版使用的性质ID: heatsource, firestarter, flammable, cutting, hammering,
///   nails, foliage, produce, dressing, rippable, bandage, container, tool 等
/// - RecipeCategory: Materials=0, Tools=1, Medicine=2, Utilities=3, Food=4
///
/// 关键注意事项：
/// - SetUpRecipes 末尾有后处理循环自动为有 specificId 的 RecipeItem 设置 specific=true，
///   但此 Postfix 在循环之后运行，必须手动设置 specific=true
/// - 同种材料需要多个时，必须添加多个 RecipeItem 条目（系统没有"数量"字段）
/// - 液体材料的 minimumCondition 表示需要的液体量（毫升）
/// - 非液体材料的 minimumCondition 表示最低耐久度（0~1 范围）
/// </summary>
[HarmonyPatch(typeof(Recipes), nameof(Recipes.SetUpRecipes))]
public static class RecipePatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            var recipes = Recipes.recipes;
            if (recipes == null)
            {
                Plugin.Log.LogWarning("[RecipePatch] Recipes.recipes is null!");
                return;
            }

            // === 移除被封禁的原版配方 ===
            // 必须在添加自定义配方之前执行，确保索引连续
            int blockedRecipes = 0;
            for (int i = recipes.Count - 1; i >= 0; i--)
            {
                var recipe = recipes[i];
                if (recipe.result != null && VanillaBlockPatch.IsBlocked(recipe.result.id))
                {
                    recipes.RemoveAt(i);
                    blockedRecipes++;
                }
            }

            // 重建配方索引（移除后有间隙）
            for (int i = 0; i < recipes.Count; i++)
            {
                recipes[i].index = i;
            }

            if (blockedRecipes > 0)
                Plugin.Log.LogInfo($"[RecipePatch] Removed {blockedRecipes} blocked vanilla weapon/ammo/mag recipes.");

            // === 自定义子弹合成配方 ===
            // category = Materials(0), INT = 9

            // 338ucw: 2废料管+3易燃粉末+30ml生化流体+4弹壳+锤打工具 → 4发
            AddRecipe(recipes, Ammo338UCWItemSystem.ItemKey, 4,
                Specific("scraptube", 2),
                Specific("flammablepowder", 3),
                Liquid("biochem", 30f),
                Specific("casing", 4),
                ByQuality("hammering"));

            // 76239sp: 2废料管+2易燃粉末+10ml生化流体+4弹壳+锤打工具 → 4发
            AddRecipe(recipes, Ammo76239SPItemSystem.ItemKey, 4,
                Specific("scraptube", 2),
                Specific("flammablepowder", 2),
                Liquid("biochem", 10f),
                Specific("casing", 4),
                ByQuality("hammering"));

            // 76251bpz: 2废料管+1废料板+3易燃粉末+10ml生化流体+4弹壳+锤打工具 → 4发
            AddRecipe(recipes, Ammo76251BPZItemSystem.ItemKey, 4,
                Specific("scraptube", 2),
                Specific("scrappanel", 1),
                Specific("flammablepowder", 3),
                Liquid("biochem", 10f),
                Specific("casing", 4),
                ByQuality("hammering"));

            // 12g85 鹿弹: 1废料管+2废料板+1塑料块+2易燃粉末+5ml生化流体+4弹壳+锤打工具 → 4发
            AddRecipe(recipes, Ammo12g85ItemSystem.ItemKey, 4,
                Specific("scraptube", 1),
                Specific("scrappanel", 2),
                Specific("plasticchunk", 1),
                Specific("flammablepowder", 2),
                Liquid("biochem", 5f),
                Specific("casing", 4),
                ByQuality("hammering"));

            // 5728sb193: 1加工铜+2废料管+2易燃粉末+5ml生化流体+5弹壳+锤打工具 → 5发
            AddRecipe(recipes, Ammo5728SB193ItemSystem.ItemKey, 5,
                Specific("processedcopper", 1),
                Specific("scraptube", 2),
                Specific("flammablepowder", 2),
                Liquid("biochem", 5f),
                Specific("casing", 5),
                ByQuality("hammering"));

            // 50copper: 1加工铜+1废料管+1废料板+2易燃粉末+5ml生化流体+5弹壳+锤打工具 → 5发
            AddRecipe(recipes, Ammo50CopperItemSystem.ItemKey, 5,
                Specific("processedcopper", 1),
                Specific("scraptube", 1),
                Specific("scrappanel", 1),
                Specific("flammablepowder", 2),
                Liquid("biochem", 5f),
                Specific("casing", 5),
                ByQuality("hammering"));

            // 919pso: 1废料管+1废料板+2易燃粉末+15ml生化流体+7弹壳+锤打工具 → 7发
            AddRecipe(recipes, Ammo919PSOItemSystem.ItemKey, 7,
                Specific("scraptube", 1),
                Specific("scrappanel", 1),
                Specific("flammablepowder", 2),
                Liquid("biochem", 15f),
                Specific("casing", 7),
                ByQuality("hammering"));

            // 55645fmj: 2废料管+1废料板+1塑料块+2易燃粉末+20ml生化流体+5弹壳+锤打工具 → 5发
            AddRecipe(recipes, Ammo55645FMJItemSystem.ItemKey, 5,
                Specific("scraptube", 2),
                Specific("scrappanel", 1),
                Specific("plasticchunk", 1),
                Specific("flammablepowder", 2),
                Liquid("biochem", 20f),
                Specific("casing", 5),
                ByQuality("hammering"));

            // === 自定义弹匣合成配方 ===
            // 所有弹匣统一配方：3废料板+1弹匣基座+10ml生化流体+切割工具+锤打工具 → 1个

            AddRecipe(recipes, AXMCMagItemSystem.ItemKey, 1,
                Specific("scrappanel", 3),
                Specific("magazinebase", 1),
                Liquid("biochem", 10f),
                ByQuality("cutting"),
                ByQuality("hammering"));

            AddRecipe(recipes, DVL10MagItemSystem.ItemKey, 1,
                Specific("scrappanel", 3),
                Specific("magazinebase", 1),
                Liquid("biochem", 10f),
                ByQuality("cutting"),
                ByQuality("hammering"));

            AddRecipe(recipes, AKMMagItemSystem.ItemKey, 1,
                Specific("scrappanel", 3),
                Specific("magazinebase", 1),
                Liquid("biochem", 10f),
                ByQuality("cutting"),
                ByQuality("hammering"));

            AddRecipe(recipes, DeagleMagItemSystem.ItemKey, 1,
                Specific("scrappanel", 3),
                Specific("magazinebase", 1),
                Liquid("biochem", 10f),
                ByQuality("cutting"),
                ByQuality("hammering"));

            AddRecipe(recipes, Glock17MagItemSystem.ItemKey, 1,
                Specific("scrappanel", 3),
                Specific("magazinebase", 1),
                Liquid("biochem", 10f),
                ByQuality("cutting"),
                ByQuality("hammering"));

            AddRecipe(recipes, M4A1MagItemSystem.ItemKey, 1,
                Specific("scrappanel", 3),
                Specific("magazinebase", 1),
                Liquid("biochem", 10f),
                ByQuality("cutting"),
                ByQuality("hammering"));

            AddRecipe(recipes, P90MagItemSystem.ItemKey, 1,
                Specific("scrappanel", 3),
                Specific("magazinebase", 1),
                Liquid("biochem", 10f),
                ByQuality("cutting"),
                ByQuality("hammering"));

            Plugin.Log.LogInfo("[RecipePatch] Added 8 custom ammo recipes + 7 magazine recipes.");

            // 注入自定义弹药名称到 Language.main 字典
            LocalePatch.InjectCustomEntries();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[RecipePatch] Failed to add recipes: {ex}");
        }
    }

    // === 材料定义辅助方法 ===

    /// <summary>精确匹配指定物品ID，合成后消耗。数量>1时生成多个条目。</summary>
    private static RecipeItem[] Specific(string itemId, int count)
    {
        var items = new RecipeItem[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = new RecipeItem(0f)
            {
                specific = true,
                specificId = itemId,
                minimumCondition = 0f,
                destroyItem = true,
            };
        }
        return items;
    }

    /// <summary>液体材料：精确匹配液体ID，minimumCondition=需要量(ml)，合成后消耗。</summary>
    private static RecipeItem[] Liquid(string liquidId, float amount)
    {
        return new[]
        {
            new RecipeItem(0f)
            {
                specific = true,
                specificId = liquidId,
                isLiquid = true,
                minimumCondition = amount,
                destroyItem = true,
            },
        };
    }

    /// <summary>性质匹配：任何带有指定 CraftingQuality 性质的物品都可以，合成后不消耗（工具类）。</summary>
    private static RecipeItem[] ByQuality(string qualityId)
    {
        return new[]
        {
            new RecipeItem(0f)
            {
                specific = false,
                quality = new CraftingQuality(qualityId, 1f),
                minimumCondition = 0f,
                destroyItem = false,
            },
        };
    }

    // === 配方添加 ===

    /// <summary>
    /// 添加弹药合成配方。
    /// materials 参数是 RecipeItem[] 数组（每个数组可含多个条目），会被展平为一个 List。
    /// </summary>
    private static void AddRecipe(List<Recipe> recipes, string ammoId, int resultAmount, params RecipeItem[][] materials)
    {
        var allItems = new List<RecipeItem>();
        foreach (var group in materials)
        {
            allItems.AddRange(group);
        }

        var recipe = new Recipe
        {
            INT = 9,
            result = new RecipeResult
            {
                id = ammoId,
                amount = resultAmount,
                resultCondition = 1f,
            },
            items = allItems,
            category = (Recipes.RecipeCategory)0, // Materials
        };

        // SetUpRecipes 后处理循环会为每个 Recipe 设置 index = 列表位置，
        // Postfix 在循环之后运行，需手动设置 index
        recipe.index = recipes.Count;
        recipes.Add(recipe);
        Plugin.Log.LogInfo($"[RecipePatch] Added recipe: -> {resultAmount}x {ammoId} (index={recipe.index}, items={allItems.Count})");
    }
}
