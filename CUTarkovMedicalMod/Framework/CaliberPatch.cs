using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 口径注册表 - 管理枪械与弹药的口径映射，实现口径细分检查。
///
/// 原版 GunScript.LoadMag 只检查 ammoType (Rifle/Shotgun/Pistol)，
/// 导致所有 Rifle 弹药可以互相混用（556能装进338的枪）。
/// 本注册表为自定义枪和弹药建立口径映射，阻止不匹配的装弹操作。
/// </summary>
public static class CaliberRegistry
{
    // === 枪 ItemKey -> 口径 ===
    public static readonly Dictionary<string, string> GunCalibers = new(StringComparer.OrdinalIgnoreCase)
    {
        { AXMCItemSystem.ItemKey,   "338lm"   },
        { DVL10ItemSystem.ItemKey,  "762x51"  },
        { SKSItemSystem.ItemKey,    "762x39"  },
        { MP133ItemSystem.ItemKey,  "12g"     },
        { AKMItemSystem.ItemKey,    "762x39"  },
        { DeagleItemSystem.ItemKey, "50ae"    },
        { Glock17ItemSystem.ItemKey, "9x19"    },
        { M4A1ItemSystem.ItemKey,    "556x45"  },
        { P90ItemSystem.ItemKey,      "5728"    },
    };

    // === 弹药 (Round) ItemKey -> 口径 ===
    public static readonly Dictionary<string, string> AmmoCalibers = new(StringComparer.OrdinalIgnoreCase)
    {
        { Ammo338UCWItemSystem.ItemKey,    "338lm"   },
        { Ammo76251BPZItemSystem.ItemKey,  "762x51"  },
        { Ammo76239SPItemSystem.ItemKey,   "762x39"  },
        { Ammo12g85ItemSystem.ItemKey,     "12g"     },
        { Ammo50CopperItemSystem.ItemKey,  "50ae"    },
        { Ammo45FMJItemSystem.ItemKey,     "45acp"   },
        { Ammo919PSOItemSystem.ItemKey,    "9x19"    },
        { Ammo55645FMJItemSystem.ItemKey,  "556x45"  },
        { Ammo5728SB193ItemSystem.ItemKey, "5728"    },
    };

    // === 自定义弹匣 ItemKey -> 对应弹药 (Round) ItemKey ===
    public static readonly Dictionary<string, string> MagToAmmoMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { AXMCMagItemSystem.ItemKey,  Ammo338UCWItemSystem.ItemKey   },
        { DVL10MagItemSystem.ItemKey, Ammo76251BPZItemSystem.ItemKey },
        { AKMMagItemSystem.ItemKey,   Ammo76239SPItemSystem.ItemKey  },
        { DeagleMagItemSystem.ItemKey, Ammo50CopperItemSystem.ItemKey },
        { Glock17MagItemSystem.ItemKey, Ammo919PSOItemSystem.ItemKey },
        { M4A1MagItemSystem.ItemKey,   Ammo55645FMJItemSystem.ItemKey },
        { P90MagItemSystem.ItemKey,     Ammo5728SB193ItemSystem.ItemKey },
    };

    /// <summary>
    /// 检查弹药是否可以装入枪械（基于口径）。
    /// 规则：
    /// - 自定义枪只接受口径匹配的自定义弹药
    /// - 自定义弹药只装入口径匹配的自定义枪
    /// - 原版枪 + 原版弹药：放行原版逻辑
    /// </summary>
    public static bool IsCaliberAllowed(string gunId, string ammoId)
    {
        var gunCaliber = GetGunCaliber(gunId);
        var ammoCaliber = GetAmmoCaliber(ammoId);

        // 两者都是自定义：口径必须匹配
        if (gunCaliber != null && ammoCaliber != null)
            return gunCaliber == ammoCaliber;

        // 自定义枪 + 原版弹药：阻止
        if (gunCaliber != null)
            return false;

        // 原版枪 + 自定义弹药：阻止
        if (ammoCaliber != null)
            return false;

        // 两者都是原版：放行
        return true;
    }

    public static string? GetGunCaliber(string gunId)
        => GunCalibers.TryGetValue(gunId, out var c) ? c : null;

    public static string? GetAmmoCaliber(string ammoId)
        => AmmoCalibers.TryGetValue(ammoId, out var c) ? c : null;

    /// <summary>
    /// 获取自定义弹匣对应的弹药 ItemKey，非自定义弹匣返回 null。
    /// </summary>
    public static string? GetAmmoForMag(string magId)
        => MagToAmmoMap.TryGetValue(magId, out var ammoId) ? ammoId : null;
}

// ===== AmmoScript.LoadRound 补丁 =====

/// <summary>
/// 拦截 AmmoScript.LoadRound - 阻止错误口径的子弹装入自定义弹匣。
/// 原版逻辑只检查 ammo.ammoType == ammoType，导致556子弹可以装入AXMC弹匣。
/// </summary>
[HarmonyPatch(typeof(AmmoScript), nameof(AmmoScript.LoadRound))]
public static class AmmoLoadRoundPatch
{
    [HarmonyPrefix]
    public static bool Prefix(AmmoScript __instance, AmmoScript ammo)
    {
        var magItem = __instance.GetComponent<Item>();
        if (magItem == null) return true;

        // 非自定义弹匣，放行原版逻辑
        var expectedAmmoId = CaliberRegistry.GetAmmoForMag(magItem.id);
        if (expectedAmmoId == null) return true;

        // 自定义弹匣：检查子弹口径
        var roundItem = ammo.GetComponent<Item>();
        var roundId = roundItem?.id ?? "";

        if (!string.Equals(roundId, expectedAmmoId, StringComparison.OrdinalIgnoreCase))
        {
            Plugin.Log.LogInfo($"[CaliberPatch] Blocked round '{roundId}' from magazine '{magItem.id}' (expected '{expectedAmmoId}').");
            return false;
        }

        return true; // 口径匹配，放行原版逻辑
    }
}

// ===== AmmoScript.UnloadRound 补丁 =====

/// <summary>
/// 拦截 AmmoScript.UnloadRound - 从自定义弹匣退弹时生成正确的自定义子弹。
/// 原版逻辑硬编码 AmmoTypeToItem(Rifle) = "556round"，导致从AXMC弹匣退弹得到556子弹。
/// </summary>
[HarmonyPatch(typeof(AmmoScript), nameof(AmmoScript.UnloadRound))]
public static class AmmoUnloadRoundPatch
{
    [HarmonyPrefix]
    public static bool Prefix(AmmoScript __instance)
    {
        var magItem = __instance.GetComponent<Item>();
        if (magItem == null) return true;

        // 非自定义弹匣，放行原版逻辑
        var expectedAmmoId = CaliberRegistry.GetAmmoForMag(magItem.id);
        if (expectedAmmoId == null) return true;

        // 自定义弹匣：手动退弹生成正确子弹
        if (__instance.rounds > 0)
        {
            var body = PlayerCamera.main?.body;
            if (body != null)
            {
                SpawnCustomRound(expectedAmmoId, __instance.transform.position, body);
                Sound.Play("gunloadshell", __instance.transform.position);
            }
            __instance.rounds--;
        }

        return false; // 跳过原版方法
    }

    /// <summary>
    /// 生成自定义子弹物品并自动拾取。
    /// </summary>
    private static void SpawnCustomRound(string ammoId, Vector3 position, Body body)
    {
        try
        {
            // 确定基础预制体
            var basePrefabId = ammoId switch
            {
                _ when ammoId == Ammo338UCWItemSystem.ItemKey    => Ammo338UCWItemSystem.BaseGameItemId,
                _ when ammoId == Ammo76251BPZItemSystem.ItemKey  => Ammo76251BPZItemSystem.BaseGameItemId,
                _ when ammoId == Ammo76239SPItemSystem.ItemKey   => Ammo76239SPItemSystem.BaseGameItemId,
                _ when ammoId == Ammo12g85ItemSystem.ItemKey     => Ammo12g85ItemSystem.BaseGameItemId,
                _ when ammoId == Ammo50CopperItemSystem.ItemKey  => Ammo50CopperItemSystem.BaseGameItemId,
                _ when ammoId == Ammo45FMJItemSystem.ItemKey     => Ammo45FMJItemSystem.BaseGameItemId,
                _ when ammoId == Ammo919PSOItemSystem.ItemKey    => Ammo919PSOItemSystem.BaseGameItemId,
                _ when ammoId == Ammo55645FMJItemSystem.ItemKey  => Ammo55645FMJItemSystem.BaseGameItemId,
                _ when ammoId == Ammo5728SB193ItemSystem.ItemKey => Ammo5728SB193ItemSystem.BaseGameItemId,
                _ => "556round",
            };

            var prefab = Resources.Load<GameObject>(basePrefabId);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[CaliberPatch] Prefab '{basePrefabId}' not found for round '{ammoId}'.");
                return;
            }

            var go = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            var newItem = go.GetComponent<Item>();
            if (newItem == null)
            {
                UnityEngine.Object.Destroy(go);
                return;
            }

            // 配置为自定义子弹
            var request = new MedicalGrantRequest(ammoId, ammoId, 1, "RoundUnload", basePrefabId);

            if (ammoId == Ammo338UCWItemSystem.ItemKey)
                Ammo338UCWItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (ammoId == Ammo76251BPZItemSystem.ItemKey)
                Ammo76251BPZItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (ammoId == Ammo76239SPItemSystem.ItemKey)
                Ammo76239SPItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (ammoId == Ammo12g85ItemSystem.ItemKey)
                Ammo12g85ItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (ammoId == Ammo50CopperItemSystem.ItemKey)
                Ammo50CopperItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (ammoId == Ammo45FMJItemSystem.ItemKey)
                Ammo45FMJItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (ammoId == Ammo919PSOItemSystem.ItemKey)
                Ammo919PSOItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (ammoId == Ammo55645FMJItemSystem.ItemKey)
                Ammo55645FMJItemSystem.ConfigureSpawnedItem(newItem, request);
            else if (ammoId == Ammo5728SB193ItemSystem.ItemKey)
                Ammo5728SB193ItemSystem.ConfigureSpawnedItem(newItem, request);

            body.AutoPickUpItem(newItem);

            Plugin.Log.LogInfo($"[CaliberPatch] Spawned custom round '{ammoId}' from magazine unload.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CaliberPatch] Failed to spawn custom round '{ammoId}': {ex}");
        }
    }
}

// ===== GunScript.Update 拉栓退膛补丁 =====

/// <summary>
/// 拦截 GunScript.Update - 拉栓退膛时弹出正确的自定义子弹。
/// 原版逻辑在拉栓时调用 AmmoScript.AmmoTypeToItem(ammoType) 获取预制体名，
/// 对 Rifle 类型硬编码返回 "556round"，导致从自定义枪拉栓退弹得到原版556子弹。
///
/// 策略：在 Prefix 中检测拉栓转换（!lastRacked && racked）且膛内有活弹时，
/// 提前弹出正确的自定义子弹并设 roundInChamber=Empty，让原版逻辑跳过退弹段。
/// </summary>
[HarmonyPatch(typeof(GunScript), nameof(GunScript.Update))]
public static class GunRackEjectPatch
{
    [HarmonyPrefix]
    public static void Prefix(GunScript __instance)
    {
        // 仅处理拉栓转换：lastRacked=false, racked=true
        if (__instance.lastRacked || !__instance.racked)
            return;

        // 仅有活弹时需要处理（0=Loaded, 1=Fired弹壳, 2=Empty）
        if ((int)__instance.roundInChamber != 0)
            return;

        // 检查是否为自定义枪
        var item = __instance.GetComponent<Item>();
        if (item == null) return;

        var caliber = CaliberRegistry.GetGunCaliber(item.id);
        if (caliber == null) return;

        // 查找该口径对应的自定义弹药
        string? customAmmoId = null;
        foreach (var kvp in CaliberRegistry.AmmoCalibers)
        {
            if (kvp.Value == caliber)
            {
                customAmmoId = kvp.Key;
                break;
            }
        }
        if (customAmmoId == null) return;

        // 确定基础预制体
        var basePrefabId = customAmmoId switch
        {
            _ when customAmmoId == Ammo338UCWItemSystem.ItemKey   => Ammo338UCWItemSystem.BaseGameItemId,
            _ when customAmmoId == Ammo76251BPZItemSystem.ItemKey => Ammo76251BPZItemSystem.BaseGameItemId,
            _ when customAmmoId == Ammo76239SPItemSystem.ItemKey  => Ammo76239SPItemSystem.BaseGameItemId,
            _ when customAmmoId == Ammo12g85ItemSystem.ItemKey    => Ammo12g85ItemSystem.BaseGameItemId,
            _ when customAmmoId == Ammo50CopperItemSystem.ItemKey => Ammo50CopperItemSystem.BaseGameItemId,
            _ when customAmmoId == Ammo45FMJItemSystem.ItemKey    => Ammo45FMJItemSystem.BaseGameItemId,
            _ when customAmmoId == Ammo919PSOItemSystem.ItemKey   => Ammo919PSOItemSystem.BaseGameItemId,
            _ when customAmmoId == Ammo55645FMJItemSystem.ItemKey => Ammo55645FMJItemSystem.BaseGameItemId,
            _ when customAmmoId == Ammo5728SB193ItemSystem.ItemKey => Ammo5728SB193ItemSystem.BaseGameItemId,
            _ => "556round",
        };

        try
        {
            var prefab = Resources.Load<GameObject>(basePrefabId);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[CaliberPatch] Prefab '{basePrefabId}' not found for rack eject.");
                return;
            }

            var pos = __instance.transform.position;
            var rot = __instance.transform.rotation;
            var go = UnityEngine.Object.Instantiate(prefab, pos, rot);

            // 配置为自定义子弹
            var newItem = go.GetComponent<Item>();
            if (newItem != null)
            {
                var request = new MedicalGrantRequest(customAmmoId, customAmmoId, 1, "RackEject", basePrefabId);

                if (customAmmoId == Ammo338UCWItemSystem.ItemKey)
                    Ammo338UCWItemSystem.ConfigureSpawnedItem(newItem, request);
                else if (customAmmoId == Ammo76251BPZItemSystem.ItemKey)
                    Ammo76251BPZItemSystem.ConfigureSpawnedItem(newItem, request);
                else if (customAmmoId == Ammo76239SPItemSystem.ItemKey)
                    Ammo76239SPItemSystem.ConfigureSpawnedItem(newItem, request);
                else if (customAmmoId == Ammo12g85ItemSystem.ItemKey)
                    Ammo12g85ItemSystem.ConfigureSpawnedItem(newItem, request);
                else if (customAmmoId == Ammo50CopperItemSystem.ItemKey)
                    Ammo50CopperItemSystem.ConfigureSpawnedItem(newItem, request);
                else if (customAmmoId == Ammo45FMJItemSystem.ItemKey)
                    Ammo45FMJItemSystem.ConfigureSpawnedItem(newItem, request);
                else if (customAmmoId == Ammo919PSOItemSystem.ItemKey)
                    Ammo919PSOItemSystem.ConfigureSpawnedItem(newItem, request);
                else if (customAmmoId == Ammo55645FMJItemSystem.ItemKey)
                    Ammo55645FMJItemSystem.ConfigureSpawnedItem(newItem, request);
                else if (customAmmoId == Ammo5728SB193ItemSystem.ItemKey)
                    Ammo5728SB193ItemSystem.ConfigureSpawnedItem(newItem, request);
            }

            // 向上弹出（与原版一致：transform.up * 12）
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
                rb.velocity = (Vector2)(__instance.transform.up * 12f);

            Plugin.Log.LogInfo($"[CaliberPatch] Ejected custom round '{customAmmoId}' from chamber rack.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CaliberPatch] Failed to eject custom round '{customAmmoId}': {ex}");
        }

        // 设为 Empty(2) 让原版跳过退弹段
        __instance.roundInChamber = (GunScript.RoundInChamber)2;
    }
}
