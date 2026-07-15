using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CUCoreLib.Networking;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 医疗效果定期备份与跨端恢复。
///
/// EffectBackup 有两个职责：
/// 1. 定期备份效果（每3秒），用于换层（RegenerateWorld 非回档）时恢复
/// 2. 客户端 Body 变化时恢复效果
///
/// 回档时的效果恢复由 KrokMpStateBridge（主机端）+ EffectSyncNetwork（网络同步）处理，
/// EffectBackup 在回档时被清空（由 MultiPlayerStateSaveProvider.Restore 调用 ClearAllBackups）。
/// </summary>
public static class EffectBackup
{
    private static readonly Dictionary<string, string> _backups = new();
    private static float _lastSaveTime;
    private static Body? _lastClientBody;
    private static bool _hadClientBody;
    private static bool _firstTickLogged;
    private const float SaveInterval = 3f;

    public static void Tick()
    {
        if (!KrokMpHelper.IsMultiplayer) return;

        if (!_firstTickLogged)
        {
            _firstTickLogged = true;
            Plugin.Log.LogInfo($"[EffectBackup] Tick running. IsHost={KrokMpHelper.IsHost}, IsClient={KrokMpHelper.IsClient}");
        }

        KrokMpLiquidSyncPatch.TryRegisterCustomLiquids();

        if (!KrokMpHelper.IsHost && !KrokMpHelper.IsServer)
        {
            TryClientRestore();
        }

        if (Time.time - _lastSaveTime >= SaveInterval)
        {
            _lastSaveTime = Time.time;
            SaveEffects();
        }
    }

    private static void SaveEffects()
    {
        try
        {
            if (KrokMpHelper.IsHost || KrokMpHelper.IsServer)
            {
                var dict = GetBodyToPlayerDict();
                if (dict == null) return;
                var localBody = PlayerCamera.main?.body;
                foreach (var entry in dict)
                {
                    var body = entry.Key;
                    var plr = entry.Value;
                    if (body == null || plr == null) continue;
                    if (body == localBody) continue;
                    var pid = InvokeGetPersistentId(plr);
                    if (string.IsNullOrEmpty(pid)) continue;
                    var json = Eff.Ser(body);
                    if (string.IsNullOrEmpty(json) && _backups.TryGetValue(pid!, out var existing) && !string.IsNullOrEmpty(existing))
                        continue;
                    _backups[pid!] = json ?? "";
                }
            }
            else
            {
                var body = PlayerCamera.main?.body;
                if (body == null) return;
                var json = Eff.Ser(body);
                if (string.IsNullOrEmpty(json) && _backups.TryGetValue("local", out var existing) && !string.IsNullOrEmpty(existing))
                    return;
                _backups["local"] = json ?? "";
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectBackup] SaveEffects failed: {ex.Message}");
        }
    }

    private static void TryClientRestore()
    {
        var body = PlayerCamera.main?.body;
        if (body == null) return;

        if (_lastClientBody == body) return;

        var hadPreviousBody = _hadClientBody;
        _lastClientBody = body;
        _hadClientBody = true;

        Plugin.Log.LogInfo($"[EffectBackup] Client body changed (hadPrevious={hadPreviousBody}).");

        if (!hadPreviousBody) return;

        // 不再用 EffectBackup 备份恢复，也不主动请求主机。
        // 完全依赖主机的 Broadcast（立即 + 延迟3秒）来恢复效果+健康+技能。
        // 备份会在主机广播到达后被更新或清空。
    }

    private static IEnumerator DelayedRestore(Body body, string json)
    {
        for (int i = 0; i < 5; i++) yield return null;
        if (body == null) yield break;
        try
        {
            Eff.Res(json, body);
            Plugin.Log.LogInfo("[EffectBackup] Restored effects to client body.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectBackup] Client restore failed: {ex.Message}");
        }
    }

    public static string? TryGetBackup(string pid)
    {
        return _backups.TryGetValue(pid, out var json) ? json : null;
    }

    public static bool HasBackups => _backups.Count > 0;

    /// <summary>
    /// 清空所有备份（在存档加载时调用，防止回档前备份覆盖存档效果）。
    /// 同时重置客户端 Body 标志。
    /// </summary>
    public static void ClearAllBackups()
    {
        _backups.Clear();
        _hadClientBody = false;
        _lastClientBody = null;
        Plugin.Log.LogInfo("[EffectBackup] All backups cleared.");
    }

    /// <summary>
    /// 更新本地备份（客户端从主机收到存档效果后调用，防止 EffectBackup 用旧备份覆盖）。
    /// </summary>
    public static void UpdateLocalBackup(string json)
    {
        _backups["local"] = json;
        _hadClientBody = true;
        _lastClientBody = PlayerCamera.main?.body;
    }

    /// <summary>
    /// 清除指定玩家的备份（新游戏时调用，防止旧效果残留）。
    /// </summary>
    public static void ClearBackupForPid(string pid)
    {
        _backups.Remove(pid);
    }

    // ===== 反射辅助 =====

    internal static Type? _netPlayerType;
    internal static FieldInfo? _dictField;

    internal static IDictionary<Body, object>? GetBodyToPlayerDict()
    {
        try
        {
            _netPlayerType ??= AccessTools.TypeByName("KrokoshaCasualtiesMP.NetPlayer");
            if (_netPlayerType == null) return null;
            _dictField ??= AccessTools.Field(_netPlayerType, "BodyToPlayerDict");
            if (_dictField == null) return null;

            var dict = _dictField.GetValue(null);
            if (dict is IDictionary<Body, object> typed) return typed;

            if (dict is System.Collections.IDictionary raw)
            {
                var result = new Dictionary<Body, object>();
                foreach (System.Collections.DictionaryEntry e in raw)
                {
                    if (e.Key is Body b && e.Value != null)
                        result[b] = e.Value;
                }
                return result;
            }
            return null;
        }
        catch { return null; }
    }

    internal static string? InvokeGetPersistentId(object plr)
    {
        var m = AccessTools.Method(plr.GetType(), "GetPersistentId");
        return m?.Invoke(plr, null) as string;
    }
}

/// <summary>
/// 通过 CUCoreLib MultiplayerApi 在主机和客户端之间同步医疗效果、健康状态和技能。
///
/// 回档时主机端从存档恢复到客户端远程 Body，但客户端本地 Body 不包含这些数据。
/// 主机通过 Broadcast 发送完整状态（效果+健康+技能）到客户端。
/// 客户端 Body 变化时主动向主机请求状态（解决回档时客户端重连晚于广播的问题）。
/// </summary>
public static class EffectSyncNetwork
{
    private const string Channel = "cutarkov.effects.sync";
    private const string RequestChannel = "cutarkov.effects.request";
    private static bool _installed;
    private static float _lastRequestTime;

    public static void Install()
    {
        if (_installed) return;
        if (!KrokMpHelper.IsKrokMpInstalled) return;

        try
        {
            // 客户端注册 handler：接收主机广播的状态
            MultiplayerApi.RegisterClientHandler(Channel, OnReceiveState);

            _installed = true;
            Plugin.Log.LogInfo("[EffectSyncNetwork] Installed client handler for state sync.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectSyncNetwork] Install failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 主机端：广播完整状态（效果+健康+技能）到所有客户端。
    /// 在 KrokMpStateBridge.RestoreEffectsForPlayer 恢复存档后调用。
    /// </summary>
    public static void BroadcastEffects(string pid, string effectsJson)
    {
        if (!_installed) return;

        try
        {
            var body = FindBodyByPid(pid);
            var payload = new JObject { ["pid"] = pid, ["effects"] = effectsJson ?? "" };

            if (body != null)
            {
                payload["health"] = new JObject
                {
                    ["brainHealth"] = body.brainHealth,
                    ["heartRate"] = body.heartRate,
                    ["bloodOxygen"] = body.bloodOxygen,
                    ["temperature"] = body.temperature,
                    ["radiationSickness"] = body.radiationSickness,
                };
                if (body.skills != null)
                {
                    payload["skills"] = new JObject
                    {
                        ["STR"] = (int)body.skills.STR,
                        ["RES"] = (int)body.skills.RES,
                        ["INT"] = (int)body.skills.INT,
                    };
                }
            }

            // 立即广播一次
            MultiplayerApi.Broadcast(Channel, payload, includeHost: false, reliable: true);
            Plugin.Log.LogInfo($"[EffectSyncNetwork] Broadcast state for player {pid} (effects len={effectsJson?.Length ?? 0}).");

            // 延迟3秒再广播一次，确保客户端已重连
            CoroutineRunner.Run(DelayedBroadcast(payload, pid));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectSyncNetwork] BroadcastEffects failed: {ex.Message}");
        }
    }

    private static IEnumerator DelayedBroadcast(JObject payload, string pid)
    {
        yield return new WaitForSeconds(3f);
        try
        {
            MultiplayerApi.Broadcast(Channel, payload, includeHost: false, reliable: true);
            Plugin.Log.LogInfo($"[EffectSyncNetwork] Delayed broadcast state for player {pid} (3s after restore).");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectSyncNetwork] DelayedBroadcast failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 客户端：Body 变化时主动向主机请求完整状态。
    /// 解决回档时客户端重连晚于主机广播的问题。
    /// </summary>
    public static void ClientRequestState()
    {
        if (!_installed) return;
        if (KrokMpHelper.IsHost || KrokMpHelper.IsServer) return;

        // 限制请求频率（每5秒最多一次）
        if (Time.time - _lastRequestTime < 5f) return;
        _lastRequestTime = Time.time;

        try
        {
            // RequestServer 发送 "request" 类型消息，主机通过 RegisterServerHandler 处理并返回数据
            // 客户端在 onResponse 回调中接收返回的数据
            MultiplayerApi.RequestServer(RequestChannel, null, OnResponseState, reliable: true);
            Plugin.Log.LogInfo("[EffectSyncNetwork] Requested state from host.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectSyncNetwork] ClientRequestState failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 客户端：接收主机响应的状态数据。
    /// </summary>
    private static void OnResponseState(JToken response)
    {
        try
        {
            if (response == null || (response is JArray arr && arr.Count == 0))
            {
                Plugin.Log.LogInfo("[EffectSyncNetwork] Host response was null/empty (new game). Clearing local backups.");
                EffectBackup.ClearAllBackups();
                return;
            }

            // 响应格式与 OnReceiveState 相同，复用处理逻辑
            OnReceiveState(response);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectSyncNetwork] OnResponseState failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 主机端：响应客户端的状态请求，发送所有在线玩家的状态。
    /// </summary>
    private static JToken OnClientRequestState(JToken _)
    {
        try
        {
            // 返回所有非主机玩家的状态，客户端会匹配自己的 pid
            var dict = EffectBackup.GetBodyToPlayerDict();
            if (dict == null || dict.Count == 0) return new JArray();

            var localBody = PlayerCamera.main?.body;
            var playersArray = new JArray();

            foreach (var entry in dict)
            {
                var body = entry.Key;
                var plr = entry.Value;
                if (body == null || plr == null) continue;
                if (body == localBody) continue;

                var pid = EffectBackup.InvokeGetPersistentId(plr);
                if (string.IsNullOrEmpty(pid)) continue;

                var effectsJson = Eff.Ser(body);
                var playerData = new JObject
                {
                    ["pid"] = pid!,
                    ["effects"] = effectsJson ?? "",
                    ["health"] = new JObject
                    {
                        ["brainHealth"] = body.brainHealth,
                        ["heartRate"] = body.heartRate,
                        ["bloodOxygen"] = body.bloodOxygen,
                        ["temperature"] = body.temperature,
                        ["radiationSickness"] = body.radiationSickness,
                    },
                };
                if (body.skills != null)
                {
                    playerData["skills"] = new JObject
                    {
                        ["STR"] = (int)body.skills.STR,
                        ["RES"] = (int)body.skills.RES,
                        ["INT"] = (int)body.skills.INT,
                    };
                }
                playersArray.Add(playerData);
            }

            Plugin.Log.LogInfo($"[EffectSyncNetwork] Responded to client state request ({playersArray.Count} players).");
            return playersArray;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectSyncNetwork] OnClientRequestState failed: {ex.Message}");
            return null!;
        }
    }

    /// <summary>
    /// 客户端：接收主机发送的状态，恢复到本地 Body。
    /// </summary>
    private static string? _lastRestoredEffectsJson;

    private static void OnReceiveState(JToken payload)
    {
        try
        {
            var body = PlayerCamera.main?.body;
            if (body == null) return;

            // payload 可能是单个玩家状态（Broadcast）或数组（请求响应）
            JToken? myData = null;
            var myPid = GetLocalPlayerPid();

            if (payload is JArray arr)
            {
                // 请求响应：数组中找自己的 pid
                foreach (var item in arr)
                {
                    if (item["pid"]?.ToString() == myPid)
                    {
                        myData = item;
                        break;
                    }
                }
            }
            else if (payload is JObject obj)
            {
                // Broadcast：单个玩家状态
                var dataPid = obj["pid"]?.ToString();
                if (dataPid == myPid || string.IsNullOrEmpty(myPid))
                    myData = obj;
            }

            if (myData == null) return;

            var effectsJson = myData["effects"]?.ToString();
            var health = myData["health"] as JObject;
            var skills = myData["skills"] as JObject;

            // 跳过重复的效果恢复（延迟广播会发送相同数据，不需要重复处理）
            if (effectsJson == _lastRestoredEffectsJson && health == null && skills == null)
            {
                Plugin.Log.LogInfo("[EffectSyncNetwork] Skipping duplicate state (same effects JSON).");
                return;
            }
            _lastRestoredEffectsJson = effectsJson;

            Plugin.Log.LogInfo($"[EffectSyncNetwork] Received state: effects len={effectsJson?.Length ?? 0}, hasHealth={health != null}, hasSkills={skills != null}.");

            // 延迟恢复确保 Body 完全初始化
            CoroutineRunner.Run(DelayedRestoreFromSave(body, effectsJson, health, skills));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectSyncNetwork] OnReceiveState failed: {ex.Message}");
        }
    }

    private static IEnumerator DelayedRestoreFromSave(Body body, string? effectsJson, JObject? health, JObject? skills)
    {
        for (int i = 0; i < 5; i++) yield return null;
        if (body == null) yield break;
        try
        {
            // 恢复效果
            if (!string.IsNullOrEmpty(effectsJson))
            {
                // 不调用 ClearAllBuffs() - Eff.Res 会销毁旧控制器并创建新的，
                // 新控制器的 Update() 会自动调用 ShowBuff 添加 buff 条目。
                // ClearAllBuffs 会导致几帧内状态栏为空（延迟广播重复触发时更明显）。
                Eff.Res(effectsJson, body);
                Plugin.Log.LogInfo($"[EffectSyncNetwork] Restored effects from host (len={effectsJson.Length}).");
                EffectBackup.UpdateLocalBackup(effectsJson);
            }

            // 恢复健康状态
            if (health != null)
            {
                body.brainHealth = health["brainHealth"]?.Value<float>() ?? body.brainHealth;
                body.heartRate = health["heartRate"]?.Value<float>() ?? body.heartRate;
                body.bloodOxygen = health["bloodOxygen"]?.Value<float>() ?? body.bloodOxygen;
                body.temperature = health["temperature"]?.Value<float>() ?? body.temperature;
                body.radiationSickness = health["radiationSickness"]?.Value<float>() ?? body.radiationSickness;
                Plugin.Log.LogInfo($"[EffectSyncNetwork] Restored health from host: brain={body.brainHealth:F1}, heart={body.heartRate:F1}, o2={body.bloodOxygen:F1}.");
            }

            // 恢复技能
            if (skills != null && body.skills != null)
            {
                body.skills.STR = skills["STR"]?.Value<int>() ?? body.skills.STR;
                body.skills.RES = skills["RES"]?.Value<int>() ?? body.skills.RES;
                body.skills.INT = skills["INT"]?.Value<int>() ?? body.skills.INT;
                Plugin.Log.LogInfo($"[EffectSyncNetwork] Restored skills from host: STR={body.skills.STR}, RES={body.skills.RES}, INT={body.skills.INT}.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[EffectSyncNetwork] DelayedRestore failed: {ex.Message}");
        }
    }

    private static Body? FindBodyByPid(string pid)
    {
        var dict = EffectBackup.GetBodyToPlayerDict();
        if (dict == null) return null;
        foreach (var entry in dict)
        {
            var p = EffectBackup.InvokeGetPersistentId(entry.Value);
            if (p == pid) return entry.Key;
        }
        return null;
    }

    private static string? GetLocalPlayerPid()
    {
        try
        {
            var body = PlayerCamera.main?.body;
            if (body == null) return null;
            var dict = EffectBackup.GetBodyToPlayerDict();
            if (dict == null) return null;
            if (dict.TryGetValue(body, out var plr))
                return EffectBackup.InvokeGetPersistentId(plr);
        }
        catch { }
        return null;
    }
}
