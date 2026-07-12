using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using CUTarkovMedicalMod.Framework;
using UnityEngine;

namespace CUTarkovMedicalMod;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string ModGuid = "com.yourname.cu.tarkovmedicalmod";
    public const string ModName = "Casualties: Unknown - Tarkov-Style Medical Mod";
    public const string ModVersion = "0.3.0";

    internal static ManualLogSource Log = null!;

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
            var harmony = new Harmony(ModGuid);
            MedicalInjectionBridge.RegisterSink(new DefaultMedicalItemGrantSink());
            MedicalSpawnHooks.SetLog(Logger);
            MedicalWorldLootHooks.SetLog(Logger);
            harmony.PatchAll();
            Log.LogInfo("Harmony PatchAll succeeded.");
        }
        catch (Exception ex)
        {
            Log.LogError($"PatchAll() threw: {ex}");
        }

        // 单独应用新增补丁，确保即使失败也不影响原有补丁（如 buff 图标）
        try
        {
            var harmony2 = new Harmony(ModGuid + ".Extra");
            harmony2.CreateClassProcessor(typeof(SaveSystemTryLoadPatch)).Patch();
            harmony2.CreateClassProcessor(typeof(FloorTransitionReconfigurePatch)).Patch();
            Log.LogInfo("Extra patches (SaveSystem, FloorTransition) applied successfully.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Extra patches failed: {ex}");
        }

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

        // 每帧检查是否需要执行楼层切换后的物品重新配置（轻量级，仅检查布尔标志）
        CustomItemReconfigurator.Tick(Log);

        _tickCounter++;
        if (_tickCounter < 300) return;
        _tickCounter = 0;

        // 确保所有自定义物品都注册到 GlobalItems（不只是部分）
        CustomItemReconfigurator.EnsureAllCustomItemsRegistered();
        MedicalSpawnHooks.TickGlobalGrantFallback();
    }

    private void OnGUI()
    {
        _updateNotifier?.OnGUI();
    }
}
