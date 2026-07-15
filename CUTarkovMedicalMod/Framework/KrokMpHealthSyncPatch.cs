using System;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 补丁 KrokMP 的 CharacterHealthStateSyncPacket.Apply 方法。
///
/// 问题：KrokMP 主机每隔约 1 秒将所有身体状态（temperature/hunger/thirst/sicknessAmount/
/// weightOffset/brainHealth/heartRate 等）同步到客户端，覆盖客户端本地医疗效果控制器的修改。
/// 导致客户端使用针剂后效果每秒被重置。
///
/// 修复：当 Apply 的目标 body 是本地玩家身体且存在活跃的医疗效果控制器时，跳过同步。
/// 客户端是自己身体的权威，医疗效果在本地独立运行，不应被主机状态覆盖。
/// </summary>
public static class KrokMpHealthSyncPatch
{
    private static bool _installed;
    private static Type? _netBodyType;

    /// <summary>
    /// 安装补丁。仅在 KrokMP 已安装时生效。
    /// </summary>
    public static void Install(Harmony harmony)
    {
        if (_installed) return;

        var packetType = AccessTools.TypeByName("KrokoshaCasualtiesMP.CharacterHealthStateSyncPacket");
        if (packetType == null)
        {
            Plugin.Log.LogInfo("[KrokMpHealthSyncPatch] CharacterHealthStateSyncPacket type not found, skipping.");
            return;
        }

        _netBodyType = AccessTools.TypeByName("KrokoshaCasualtiesMP.NetBody");

        // 补丁 Apply(Body body) -- 直接接收 Body 参数的方法
        var applyBodyMethod = AccessTools.Method(packetType, "Apply", new[] { typeof(Body) });
        if (applyBodyMethod != null)
        {
            harmony.Patch(applyBodyMethod,
                prefix: new HarmonyMethod(typeof(KrokMpHealthSyncPatch), nameof(Apply_Body_Prefix)));
            Plugin.Log.LogInfo("[KrokMpHealthSyncPatch] Patched CharacterHealthStateSyncPacket.Apply(Body).");
        }
        else
        {
            Plugin.Log.LogWarning("[KrokMpHealthSyncPatch] Apply(Body) method not found.");
        }

        // 补丁 Apply(NetBody npc) -- 接收 NetBody 参数的方法（客户端接收同步包时可能调用此重载）
        if (_netBodyType != null)
        {
            var applyNetBodyMethod = AccessTools.Method(packetType, "Apply", new[] { _netBodyType });
            if (applyNetBodyMethod != null)
            {
                harmony.Patch(applyNetBodyMethod,
                    prefix: new HarmonyMethod(typeof(KrokMpHealthSyncPatch), nameof(Apply_NetBody_Prefix)));
                Plugin.Log.LogInfo("[KrokMpHealthSyncPatch] Patched CharacterHealthStateSyncPacket.Apply(NetBody).");
            }
        }

        _installed = true;
    }

    /// <summary>
    /// Apply(Body body) 的 Prefix：当 body 是本地玩家身体且有活跃医疗效果时跳过同步。
    /// 使用 __0 按索引获取参数，避免参数名不匹配导致注入失败。
    /// </summary>
    private static bool Apply_Body_Prefix(object __0)
    {
        if (!KrokMpHelper.IsMultiplayer) return true;
        if (__0 is not Body body) return true;

        var localBody = PlayerCamera.main?.body;
        if (body != localBody) return true;

        if (!HasActiveMedicalEffects(body)) return true;

        Plugin.Log.LogDebug("[KrokMpHealthSyncPatch] Blocked Apply(Body) sync for local body with active medical effects.");
        return false;
    }

    /// <summary>
    /// Apply(NetBody npc) 的 Prefix：当 NetBody 对应的 body 是本地玩家身体且有活跃医疗效果时跳过同步。
    /// 使用 __0 按索引获取参数，避免参数名不匹配导致注入失败。
    /// </summary>
    private static bool Apply_NetBody_Prefix(object __0)
    {
        if (!KrokMpHelper.IsMultiplayer) return true;
        if (__0 == null) return true;

        // NetBody 是 MonoBehaviour/Component，从其 GameObject 获取 Body 组件
        Body? body = null;
        try
        {
            if (__0 is Component comp)
                body = comp.GetComponent<Body>();
        }
        catch { }

        if (body == null) return true;

        var localBody = PlayerCamera.main?.body;
        if (body != localBody) return true;

        if (!HasActiveMedicalEffects(body)) return true;

        Plugin.Log.LogDebug("[KrokMpHealthSyncPatch] Blocked Apply(NetBody) sync for local body with active medical effects.");
        return false;
    }

    /// <summary>
    /// 检查 body 上是否有活跃（enabled）的医疗效果控制器。
    /// 使用 Eff.ControllerTypes 中注册的所有效果控制器类型。
    /// </summary>
    private static bool HasActiveMedicalEffects(Body body)
    {
        if (body == null) return false;

        foreach (var kv in Eff.ControllerTypes)
        {
            var comp = body.GetComponent(kv.Value);
            if (comp is MonoBehaviour mb && mb.enabled)
                return true;
        }
        return false;
    }
}
