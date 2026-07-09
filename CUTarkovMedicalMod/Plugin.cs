using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using CUTarkovMedicalMod.Framework;

namespace CUTarkovMedicalMod;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string ModGuid = "com.yourname.cu.tarkovmedicalmod";
    public const string ModName = "Casualties: Unknown - Tarkov-Style Medical&Weapon Mod";
    public const string ModVersion = "0.1.5.0";

    internal static ManualLogSource Log = null!;

    private MedicalFramework _framework = null!;
    private MedicalDebugHotkeys _debugHotkeys = null!;
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

            // PatchAll() 的属性发现机制无法识别 PlayerCamera.HandleVariables（private 方法），
            // 需要手动注册 ScopeZoom 补丁
            try
            {
                var hvMethod = AccessTools.Method(typeof(PlayerCamera), "HandleVariables");
                if (hvMethod != null)
                {
                    var postfix = new HarmonyMethod(typeof(ScopeZoomPatch), nameof(ScopeZoomPatch.PostfixHandleVariables));
                    harmony.Patch(hvMethod, postfix: postfix);
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[ScopeZoom] Manual patch failed: {ex}");
            }
        }
        catch (Exception ex)
        {
            Log.LogError($"PatchAll() threw: {ex}");
        }

        Log.LogInfo($"{ModName} loaded. Enabled={_framework.EffectiveMode != MedicalFeatureMode.Disabled}, KrokMP={_framework.KrokMpDetected}");
        Log.LogInfo($"Medical content source: {_framework.ContentSource}");
        Log.LogInfo($"Catalog item count: {_framework.Catalog.Count}");
        Log.LogInfo(_framework.DescribeCompatibility());
    }

    private void Update()
    {
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
}
