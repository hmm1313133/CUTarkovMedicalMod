using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 拦截 RecipeResult.SpawnResult —— 为自定义物品（弹药/弹匣）提供正确的生成逻辑。
///
/// 原版逻辑（非液体结果）：
///   Utils.Create(this.id, playerBodyPos, 0)  →  Resources.Load(id) + Instantiate
/// 自定义物品 ID 不在游戏 Resources 中，Resources.Load 返回 null，
/// 导致 Instantiate(null) → GetComponent&lt;Item&gt;() 返回 null → 物品无法生成。
///
/// 此 Prefix 在检测到自定义物品 ID 时，跳过原版的 Utils.Create，
/// 改用克隆原版基础预制体 + ConfigureCustomItem 的方式生成物品。
/// </summary>
[HarmonyPatch(typeof(RecipeResult), nameof(RecipeResult.SpawnResult))]
public static class RecipeSpawnPatch
{
    /// <summary>SpawnResult 方法参数：recipeInt（配方要求的智力值）</summary>
    private static readonly MethodInfo? BodyAutoPickUpItemMethod =
        AccessTools.Method(typeof(Body), nameof(Body.AutoPickUpItem));

    [HarmonyPrefix]
    public static bool Prefix(RecipeResult __instance, int recipeInt)
    {
        var resultId = __instance.id;

        // 只拦截自定义物品 ID，原版物品走原版逻辑
        if (resultId == null || !ConsoleSpawnPatch.IsCustomItemKey(resultId))
            return true;

        // 液体结果也走原版逻辑（我们的自定义弹药/弹匣都不是液体）
        if (__instance.isLiquid)
            return true;

        try
        {
            // 获取玩家 Body
            var body = PlayerCamera.main?.body;
            if (body == null)
            {
                Plugin.Log.LogWarning("[RecipeSpawn] No player body found, falling back to original.");
                return true;
            }

            // 计算智力差值和物品耐久倍率（复刻原版 SpawnResult 的逻辑）
            // 原版: diff = body.skills.INT - recipeInt
            // 原版: 如果 diff < -3，什么都不做（直接 ret）
            // 原版: 如果 diff == -1，conditionMult = Random.Range(0.2, 0.9)
            // 原版: 如果 diff < 0（且 <= -3），随机决定是否受伤
            // 原版: 其他情况 conditionMult = 1
            int diff = body.skills.INT - recipeInt;
            float conditionMult = 1f;

            if (diff < -3)
            {
                // 智力差距太大，什么都不生成（原版直接 ret）
                Plugin.Log.LogInfo($"[RecipeSpawn] INT diff {diff} too low, no result for '{resultId}'.");
                return false;
            }

            if (diff < 0)
            {
                if (diff == -1)
                {
                    conditionMult = UnityEngine.Random.Range(0.2f, 0.9f);
                }
                else
                {
                    // diff == -2 或 -3：原版有随机受伤逻辑
                    // 这里简化处理：如果随机 < 0.5，什么都不生成
                    if (UnityEngine.Random.value < 0.5f)
                    {
                        Plugin.Log.LogInfo($"[RecipeSpawn] INT diff {diff}, random fail, no result for '{resultId}'.");
                        return false;
                    }
                    // 受伤效果（复刻原版：对5个肢体增加疼痛、降低skinHealth、增加出血）
                    try
                    {
                        body.DoGoreSound();
                        for (int i = 5; i < 8; i++)
                        {
                            var limb = body.limbs[i];
                            if (limb != null)
                            {
                                limb.pain += 40f;
                                limb.skinHealth -= 15f;
                                limb.bleedAmount += UnityEngine.Random.Range(2f, 5f);
                            }
                        }
                    }
                    catch { /* 受伤逻辑失败不影响物品生成 */ }
                }
            }

            // 获取基础预制体名
            if (!ConsoleSpawnPatch.CustomItemPrefabs.TryGetValue(resultId, out var prefabName))
            {
                Plugin.Log.LogWarning($"[RecipeSpawn] No prefab mapping for '{resultId}', falling back.");
                return true;
            }

            // 加载基础预制体
            var prefab = Resources.Load<GameObject>(prefabName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[RecipeSpawn] Prefab '{prefabName}' not found for '{resultId}'.");
                return true;
            }

            // 生成指定数量的物品
            int amount = __instance.amount;
            float resultCondition = __instance.resultCondition;
            var spawnPos = body.transform.position + new Vector3(0.5f, 0f, 0f);

            for (int i = 0; i < amount; i++)
            {
                var go = UnityEngine.Object.Instantiate(prefab, spawnPos, Quaternion.identity);
                var item = go.GetComponent<Item>();
                if (item == null)
                {
                    UnityEngine.Object.Destroy(go);
                    Plugin.Log.LogWarning($"[RecipeSpawn] Spawned object has no Item component for '{resultId}'.");
                    continue;
                }

                // 配置自定义物品（复用 ConsoleSpawnPatch 的逻辑）
                var request = new MedicalGrantRequest(resultId, resultId, 1, "RecipeSpawn", prefabName);
                ConsoleSpawnPatch.ConfigureCustomItem(item, request);

                // 设置耐久（复刻原版：condition = resultCondition * conditionMult）
                item.condition = resultCondition * conditionMult;

                // 合成出来的弹匣内没有子弹
                var ammo = item.GetComponent<AmmoScript>();
                if (ammo != null && ammo.itemType == AmmoScript.AmmoItemType.Magazine)
                {
                    ammo.rounds = 0;
                    Plugin.Log.LogInfo($"[RecipeSpawn] Crafted magazine '{resultId}' ammo set to 0/{ammo.maxRounds}.");
                }

                // 自动拾取（复刻原版 Body.AutoPickUpItem）
                try
                {
                    body.AutoPickUpItem(item);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[RecipeSpawn] AutoPickUpItem failed for '{resultId}': {ex.Message}");
                }
            }

            Plugin.Log.LogInfo($"[RecipeSpawn] Spawned {amount}x '{resultId}' (conditionMult={conditionMult:F2}).");
            return false; // 跳过原版逻辑
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[RecipeSpawn] Failed to spawn '{resultId}': {ex}");
            return true; // 出错时回退到原版逻辑
        }
    }
}
