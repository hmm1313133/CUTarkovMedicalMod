using System;
using System.Collections.Generic;
using CUCoreLib.Saving;
using CUTarkovMedicalMod.Framework;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CUTarkovMedicalMod.Integration;

/// <summary>
/// 多人玩家状态全局存档提供者。
///
/// QoL 的主机存档只保存 PlayerCamera.main.body（主机玩家），客户端数据完全丢失。
/// 本提供者在主机存档时捕获所有非主机在线玩家的：
/// - 医疗效果（Eff.Ser）
/// - 健康状态（brainHealth/heartRate/bloodOxygen/temperature/radiationSickness）
/// - 技能（STR/RES/INT）
/// - 位置（仅同层恢复）
///
/// 存档加载后数据暂存在 PendingRestore，等待客户端重连时通过 Body.Start Postfix 恢复。
/// </summary>
public sealed class MultiPlayerStateSaveProvider : ICustomSaveProvider
{
    public int GetVersion() => 1;

    public JToken Capture()
    {
        if (!KrokMpHelper.IsKrokMpInstalled || !KrokMpHelper.IsMultiplayer)
            return null!;
        if (!KrokMpHelper.IsHost && !KrokMpHelper.IsServer)
            return null!;

        try
        {
            var dict = GetBodyToPlayerDict();
            if (dict == null || dict.Count == 0) return null!;

            var playersArray = new JArray();
            var localBody = PlayerCamera.main?.body;

            foreach (var entry in dict)
            {
                var body = entry.Key;
                var plr = entry.Value;
                if (body == null || plr == null) continue;
                if (body == localBody) continue; // 跳过主机

                var pid = InvokeGetPersistentId(plr);
                if (string.IsNullOrEmpty(pid)) continue;

                var inventory = CaptureInventory(body);
                var playerData = new JObject
                {
                    ["pid"] = pid,
                    ["biomeDepth"] = WorldGeneration.world?.biomeDepth ?? 0,
                    ["effects"] = CaptureEffects(body),
                    ["health"] = CaptureHealth(body),
                    ["skills"] = CaptureSkills(body),
                    ["position"] = CapturePosition(body),
                    ["inventory"] = inventory,
                };
                playersArray.Add(playerData);

                var invItems = inventory as JArray;
                Plugin.Log.LogInfo($"[MultiPlayerStateSaveProvider] Captured player {pid}: effects={(playerData["effects"] as JArray)?.Count ?? 0} active, health={body.brainHealth:F2}/{body.heartRate:F1}/{body.bloodOxygen:F2}, skills={body.skills?.STR}/{body.skills?.RES}/{body.skills?.INT}, inv={invItems?.Count ?? 0} items.");
            }

            if (playersArray.Count == 0) return null!;
            Plugin.Log.LogInfo($"[MultiPlayerStateSaveProvider] Captured {playersArray.Count} players for save.");
            return playersArray;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[MultiPlayerStateSaveProvider] Capture failed: {ex.Message}");
            return null!;
        }
    }

    public void Restore(JToken payload, int version, SaveRestoreContext context)
    {
        if (payload == null) return;
        try
        {
            var arr = payload as JArray;
            if (arr == null || arr.Count == 0) return;

            // 清空 EffectBackup 的所有备份，防止回档前的备份覆盖存档中的效果。
            // 回档后应该恢复存档中的效果状态，而不是回档前最近3秒的备份。
            EffectBackup.ClearAllBackups();
            Plugin.Log.LogInfo("[MultiPlayerStateSaveProvider] Cleared EffectBackup on save restore.");

            // 标记存档已加载，供 KrokMpStateBridge 区分回档和新游戏
            KrokMpStateBridge.MarkPendingDataLoaded();

            PendingPlayerData.Clear();
            foreach (var token in arr)
            {
                var obj = token as JObject;
                if (obj == null) continue;
                var pid = obj["pid"]?.ToString();
                if (!string.IsNullOrEmpty(pid) && pid != null)
                    PendingPlayerData[pid] = obj;
            }
            Plugin.Log.LogInfo($"[MultiPlayerStateSaveProvider] Loaded {PendingPlayerData.Count} player entries for restore-on-reconnect.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[MultiPlayerStateSaveProvider] Restore failed: {ex.Message}");
        }
    }

    // ===== 暂存数据，供 KrokMpStateBridge.Body_Start_Postfix 读取 =====

    internal static readonly Dictionary<string, JObject> PendingPlayerData = new();

    internal static bool TryConsumePending(string pid, out JObject data)
    {
        return PendingPlayerData.TryGetValue(pid, out data!) && PendingPlayerData.Remove(pid);
    }

    // ===== 序列化辅助 =====

    private static JToken CaptureEffects(Body body)
    {
        var json = Eff.Ser(body);
        return string.IsNullOrEmpty(json) ? new JObject() : JToken.Parse(json);
    }

    private static JToken CaptureHealth(Body body)
    {
        return new JObject
        {
            ["brainHealth"] = body.brainHealth,
            ["heartRate"] = body.heartRate,
            ["bloodOxygen"] = body.bloodOxygen,
            ["temperature"] = body.temperature,
            ["radiationSickness"] = body.radiationSickness,
        };
    }

    private static JToken CaptureSkills(Body body)
    {
        var skills = body.skills;
        if (skills == null) return new JObject();
        return new JObject
        {
            ["STR"] = (int)skills.STR,
            ["RES"] = (int)skills.RES,
            ["INT"] = (int)skills.INT,
        };
    }

    private static JToken CapturePosition(Body body)
    {
        var pos = body.transform.position;
        return new JObject { ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z };
    }

    // ===== 背包捕获 =====

    private static JToken CaptureInventory(Body body)
    {
        var items = new JArray();
        for (var i = 0; i < body.slots.Length; i++)
        {
            if (!body.HoldingItem(i)) continue;
            var item = body.GetItem(i);
            if (item == null) continue;

            items.Add(CaptureItemData(item, i, false));

            // 检查 Container 子物品：遍历 Container transform 子级找 Item
            var container = item.GetComponent<Container>();
            if (container != null)
            {
                foreach (Transform child in container.transform)
                {
                    var childItem = child.GetComponent<Item>();
                    if (childItem != null && childItem != item)
                        items.Add(CaptureItemData(childItem, i, true));
                }
            }
        }
        return items;
    }

    private static JObject CaptureItemData(Item item, int slot, bool inContainer)
    {
        var data = new JObject
        {
            ["id"] = item.id,
            ["condition"] = item.condition,
            ["favourited"] = item.favourited,
            ["slot"] = slot,
            ["inContainer"] = inContainer,
        };

        // 弹匣子弹数（独立弹匣物品）
        var ammo = item.GetComponent<AmmoScript>();
        if (ammo != null && ammo.itemType == AmmoScript.AmmoItemType.Magazine)
            data["ammoRounds"] = ammo.rounds;

        // 枪械弹匣状态（弹匣装入枪后被销毁，状态存在 GunScript 上）
        var gun = item.GetComponent<GunScript>();
        if (gun != null)
        {
            data["gunHasMag"] = gun.hasMag;
            data["gunRoundsInMag"] = gun.roundsInMag;
        }

        return data;
    }

    // ===== 反射辅助 =====

    private static IDictionary<Body, object>? GetBodyToPlayerDict()
    {
        var netPlayerType = AccessTools.TypeByName("KrokoshaCasualtiesMP.NetPlayer");
        if (netPlayerType == null) return null;

        var dictField = AccessTools.Field(netPlayerType, "BodyToPlayerDict");
        if (dictField == null) return null;

        var dict = dictField.GetValue(null);
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

    private static string? InvokeGetPersistentId(object plr)
    {
        var m = AccessTools.Method(plr.GetType(), "GetPersistentId");
        return m?.Invoke(plr, null) as string;
    }
}
