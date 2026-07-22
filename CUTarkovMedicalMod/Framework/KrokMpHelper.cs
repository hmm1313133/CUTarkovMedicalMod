using BepInEx.Bootstrap;
using CUCoreLib.Networking;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// KrokMP 多人模式 API 封装。
/// 通过 CUCoreLib 的 MultiplayerApi 检测主机/客户端状态，不再使用手动反射。
/// KrokMP 未安装时所有检查返回 false（视为单机模式）。
/// </summary>
public static class KrokMpHelper
{
    private const string KrokMpGuid = "KrokoshaCasualtiesMP";

    // 性能优化：IsKrokMpInstalled 启动后不变，缓存一次
    private static bool _isKrokMpInstalledCache;
    private static bool _isKrokMpInstalledChecked;

    // 性能优化：ShouldSpawnLoot 缓存 2 秒，避免每帧对每个 BuildingEntity 重复检查
    private static float _shouldSpawnLootCacheTime = -999f;
    private static bool _shouldSpawnLootCache = true;
    private const float ShouldSpawnLootCacheInterval = 2f;

    /// <summary>KrokMP 是否已安装（通过 BepInEx Chainloader 检测，任意时机可用）</summary>
    public static bool IsKrokMpInstalled
    {
        get
        {
            if (!_isKrokMpInstalledChecked)
            {
                _isKrokMpInstalledChecked = true;
                _isKrokMpInstalledCache = Chainloader.PluginInfos.ContainsKey(KrokMpGuid);
            }
            return _isKrokMpInstalledCache;
        }
    }

    /// <summary>多人模式是否正在运行（CUCoreLib MultiplayerApi.IsRunning）</summary>
    public static bool IsMultiplayer => MultiplayerApi.IsRunning;

    /// <summary>当前是否为主机（CUCoreLib MultiplayerApi.IsHost）</summary>
    public static bool IsHost => MultiplayerApi.IsHost;

    /// <summary>当前是否为服务器（CUCoreLib MultiplayerApi.IsServer，通常等同 IsHost）</summary>
    public static bool IsServer => MultiplayerApi.IsServer;

    /// <summary>当前是否为客户端（非主机，CUCoreLib MultiplayerApi.IsClient）</summary>
    public static bool IsClient => MultiplayerApi.IsClient;

    /// <summary>当前是否已连接到多人游戏（主机或客户端）</summary>
    public static bool IsConnected => MultiplayerApi.IsClient || MultiplayerApi.IsHost;

    /// <summary>
    /// 是否应该执行世界掉落生成。
    /// 单机模式：始终返回 true。
    /// 多人模式：仅主机返回 true（主机负责世界生成和物品掉落，KrokMP 自动同步给客户端）。
    /// </summary>
    public static bool ShouldSpawnLoot
    {
        get
        {
            // 性能优化：缓存 2 秒，避免每帧对每个 BuildingEntity 重复检查
            var now = Time.time;
            if (now - _shouldSpawnLootCacheTime < ShouldSpawnLootCacheInterval)
                return _shouldSpawnLootCache;
            _shouldSpawnLootCacheTime = now;

            if (!IsKrokMpInstalled) { _shouldSpawnLootCache = true; return true; }
            if (!IsMultiplayer) { _shouldSpawnLootCache = true; return true; }
            _shouldSpawnLootCache = IsHost || IsServer;
            return _shouldSpawnLootCache;
        }
    }

}
