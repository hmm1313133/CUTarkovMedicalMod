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

        if (Input.GetKeyDown(KeyCode.F7) || Input.GetKeyDown(KeyCode.Keypad7))
        {
            DumpRuntimeState();
        }
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

