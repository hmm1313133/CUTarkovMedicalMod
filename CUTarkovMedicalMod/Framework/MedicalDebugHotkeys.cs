using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

public sealed class MedicalDebugHotkeys
{
    private readonly ManualLogSource _log;
    private bool _tickObserved;

    public MedicalDebugHotkeys(ManualLogSource log)
    {
        _log = log;
    }

    public void Tick()
    {
        if (!_tickObserved)
        {
            _tickObserved = true;
            _log.LogInfo("[Debug] Medical hotkey polling is active.");
        }

        if (Input.GetKeyDown(KeyCode.F6) || Input.GetKeyDown(KeyCode.Keypad6))
        {
            GiveEtgToCurrentPlayer();
        }

        if (Input.GetKeyDown(KeyCode.F7) || Input.GetKeyDown(KeyCode.Keypad7))
        {
            DumpRuntimeState();
        }
    }

    private void GiveEtgToCurrentPlayer()
    {
        var world = WorldGeneration.world;
        var body = world?.body;
        if (body == null)
        {
            _log.LogWarning("[Debug] F6 ignored: WorldGeneration/body not ready.");
            return;
        }

        MedicalSpawnHooks.ForceDebugGrantCurrentRun();
        var request = new MedicalGrantRequest(
            EtgCItemSystem.EtgItemKey,
            EtgCItemSystem.EtgDisplayName,
            1,
            "DebugHotkey",
            EtgCItemSystem.EtgBaseGameItemId);

        var ok = MedicalInjectionBridge.TryGrantStartingLoadout(body, new List<MedicalGrantRequest> { request }, _log);
        _log.LogInfo(ok
            ? "[Debug] F6: ETG-c grant request succeeded."
            : "[Debug] F6: ETG-c grant request failed.");
    }

    private void DumpRuntimeState()
    {
        var world = WorldGeneration.world;
        var body = world?.body;
        var mode = MedicalFrameworkApi.EffectiveMode;
        _log.LogInfo($"[Debug] F7: initialized={MedicalFrameworkApi.IsInitialized}, mode={mode}, krok={MedicalFrameworkApi.IsKrokMpDetected}, world={(world != null)}, body={(body != null)}");

        if (body == null)
        {
            return;
        }

        var handItem = body.HoldingItem(body.handSlot) ? body.GetItem(body.handSlot) : null;
        var handId = handItem != null ? handItem.id : "<none>";
        _log.LogInfo($"[Debug] F7: handSlot={body.handSlot}, handItem={handId}");
    }
}

