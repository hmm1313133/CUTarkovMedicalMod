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
    public const string ModName = "Casualties: Unknown - Tarkov-Style Medical Mod";
    public const string ModVersion = "0.1.0";

    internal static ManualLogSource Log = null!;

    private MedicalFramework _framework = null!;
    private MedicalDebugHotkeys _debugHotkeys = null!;
    private int _tickCounter;

    private void Awake()
    {
        Log = Logger;

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
        MedicalSpawnHooks.TickGlobalGrantFallback();
    }
}
