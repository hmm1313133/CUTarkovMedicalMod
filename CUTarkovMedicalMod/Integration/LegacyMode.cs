using CUTarkovMedicalMod.Framework;
using HarmonyLib;

namespace CUTarkovMedicalMod.Integration;

/// <summary>
/// 非 CUCoreLib 模式（遗留模式）。
/// 保持现有行为：当 QoL 安装时注册 QoLSaveFix 补丁（物品+效果持久化）。
/// </summary>
public sealed class LegacyMode : IIntegrationMode
{
    public void Initialize(Harmony harmony)
    {
        var hasQoL = QoLSaveFix.HasQoL();
        Plugin.Log.LogInfo($"[LegacyMode] Initialize. HasQoL={hasQoL}");
        if (hasQoL)
            QoLSaveFix.Register(harmony);
    }

    public void OnItemsSetup()
    {
        // 遗留模式无需额外操作，EnsureRegisteredInItemTable 已将物品注册到 GlobalItems。
    }
}
