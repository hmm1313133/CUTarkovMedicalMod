using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using CUTarkovMedicalMod.Framework;
using CUTarkovMedicalMod.Integration;
using UnityEngine;

namespace CUTarkovMedicalMod;

[BepInPlugin(ModGuid, ModName, ModVersion)]
[BepInDependency("net.cucorelib", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("org.bepinex.plugins.qol_unknown", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string ModGuid = "com.yourname.cu.tarkovmedicalmod";
    public const string ModName = "Casualties: Unknown - Tarkov-Style Medical Mod";
    public const string ModVersion = "0.2.6";

    internal static ManualLogSource Log = null!;
    internal static IIntegrationMode IntegrationMode = null!;

    private MedicalFramework _framework = null!;
    private MedicalDebugHotkeys _debugHotkeys = null!;
    private UpdateNotifier _updateNotifier = null!;
    private int _tickCounter;

    private void Awake()
    {
        Log = Logger;

        // 初始化管视遮罩系统（全屏黑色径向渐变叠加层）
        SkillEffectHelper.InitializeTunnelVision();

        _framework = new MedicalFramework(Config, Logger);
        _framework.Initialize();
        _debugHotkeys = new MedicalDebugHotkeys(Logger);

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

        // Initialize integration mode now. QoL loads before us (verified in logs),
        // so Chainloader.PluginInfos will contain QoL's GUID.
        try
        {
            IntegrationMode = IntegrationModeFactory.Create();
            IntegrationMode.Initialize(harmony);
        }
        catch (Exception ex) { Log.LogError($"IntegrationMode.Initialize() threw: {ex}"); }

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

        EtgCItemSystem.EnsureRegisteredInItemTable();
        ZagustinItemSystem.EnsureRegisteredInItemTable();
        PropitalItemSystem.EnsureRegisteredInItemTable();
        SJ6ItemSystem.EnsureRegisteredInItemTable();
        Sj1ItemSystem.EnsureRegisteredInItemTable();
        PnbItemSystem.EnsureRegisteredInItemTable();
        ObdolbosItemSystem.EnsureRegisteredInItemTable();
        Sj9ItemSystem.EnsureRegisteredInItemTable();
        BluebloodItemSystem.EnsureRegisteredInItemTable();
        MedicalSpawnHooks.TickGlobalGrantFallback();
    }

    private void OnGUI()
    {
        _updateNotifier?.OnGUI();
    }
}
