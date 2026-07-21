using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using CUTarkovMedicalMod.Framework;
using CUTarkovMedicalMod.Integration;

namespace CUTarkovMedicalMod;

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInDependency("net.cucorelib")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string ModGuid = "com.yourname.cu.tarkovmedicalmod";
    public const string ModName = "Casualties: Unknown - Tarkov-Style Medical Mod";
    public const string ModVersion = "0.3.0";

    internal static ManualLogSource Log = null!;
    internal static CUCoreLibMode IntegrationMode = null!;

    private MedicalFramework _framework = null!;
    private MedicalDebugHotkeys _debugHotkeys = null!;
    private UpdateNotifier _updateNotifier = null!;
    private int _tickCounter;

    private void Awake()
    {
        Log = Logger;

        // 初始化管视遮罩系统（全屏黑色径向渐变叠加层）
        try { SkillEffectHelper.InitializeTunnelVision(); }
        catch (Exception ex) { Log.LogError($"InitializeTunnelVision threw: {ex}"); }

        try
        {
            _framework = new MedicalFramework(Config, Logger);
            _framework.Initialize();
        }
        catch (Exception ex) { Log.LogError($"MedicalFramework init threw: {ex}"); }

        try { _debugHotkeys = new MedicalDebugHotkeys(Logger); }
        catch (Exception ex) { Log.LogError($"MedicalDebugHotkeys init threw: {ex}"); }

        try
        {
            MedicalInjectionBridge.RegisterSink(new DefaultMedicalItemGrantSink());
            MedicalSpawnHooks.SetLog(Logger);
            MedicalWorldLootHooks.SetLog(Logger);
        }
        catch (Exception ex)
        {
            Log.LogError($"Setup threw: {ex}");
        }

        var harmony = new Harmony(ModGuid);
        try { harmony.PatchAll(); }
        catch (Exception ex) { Log.LogError($"PatchAll() threw: {ex}"); }

        // Initialize CUCoreLib integration mode.
        try
        {
            IntegrationMode = new CUCoreLibMode();
            IntegrationMode.Initialize(harmony);
        }
        catch (Exception ex) { Log.LogError($"IntegrationMode.Initialize() threw: {ex}"); }

        // 安装 KrokMP PlayerSavedState 桥接（仅 KrokMP 已安装时生效）
        try
        {
            KrokMpStateBridge.Install(harmony);
        }
        catch (Exception ex) { Log.LogError($"KrokMpStateBridge.Install() threw: {ex}"); }

        // 安装 KrokMP 健康同步保护补丁（防止主机同步覆盖客户端本地医疗效果）
        try
        {
            KrokMpHealthSyncPatch.Install(harmony);
        }
        catch (Exception ex) { Log.LogError($"KrokMpHealthSyncPatch.Install() threw: {ex}"); }

        // 安装 QoL 多人存档兼容补丁（仅 QoL Unknown 已安装时生效）
        try
        {
            QolMpSaveCompat.Install(harmony);
        }
        catch (Exception ex) { Log.LogError($"QolMpSaveCompat.Install() threw: {ex}"); }

        // 安装 KrokMP 液体同步补丁（捕获 PackData2 的 KeyNotFoundException）
        try
        {
            KrokMpLiquidSyncPatch.Install(harmony);
        }
        catch (Exception ex) { Log.LogError($"KrokMpLiquidSyncPatch.Install() threw: {ex}"); }

        // 安装效果网络同步（主机广播存档效果到客户端）
        try
        {
            EffectSyncNetwork.Install();
        }
        catch (Exception ex) { Log.LogError($"EffectSyncNetwork.Install() threw: {ex}"); }

        Log.LogInfo($"{ModName} loaded. Enabled={_framework.EffectiveMode != MedicalFeatureMode.Disabled}, KrokMP={_framework.KrokMpDetected}");
        Log.LogInfo($"Medical content source: {_framework.ContentSource}");
        Log.LogInfo($"Catalog item count: {_framework.Catalog.Count}");
        Log.LogInfo(_framework.DescribeCompatibility());

        // 创建更新提醒实例（由 Plugin 的 Update/OnGUI 驱动）
        _updateNotifier = new UpdateNotifier();
    }

    private void Update()
    {
        _updateNotifier?.Tick();
        _debugHotkeys?.Tick();

        _tickCounter++;
        if (_tickCounter < 300) return;
        _tickCounter = 0;

        MedicalSpawnHooks.TickGlobalGrantFallback();
    }

    private void OnGUI()
    {
        _updateNotifier?.OnGUI();
    }
}
