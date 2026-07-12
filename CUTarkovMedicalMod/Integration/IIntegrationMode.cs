using HarmonyLib;

namespace CUTarkovMedicalMod.Integration;

/// <summary>
/// 集成模式接口。封装 CUCoreLib 和非 CUCoreLib 两种模式的差异。
/// Plugin.Awake 中根据 CUCoreLib 是否安装选择对应实现。
/// </summary>
public interface IIntegrationMode
{
    /// <summary>
    /// 在 harmony.PatchAll() 之后调用，负责注册存档相关补丁或提供者。
    /// </summary>
    void Initialize(Harmony harmony);

    /// <summary>
    /// 在 EtgStimRegistryPatch.Postfix 中所有 EnsureRegisteredInItemTable 调用之后触发。
    /// CUCoreLib 模式下将已注册到 GlobalItems 的物品同步注册到 CUCoreLib ItemRegistry。
    /// </summary>
    void OnItemsSetup();
}
