using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// AXMC 瞄准镜视野扩展补丁入口。
/// 逻辑实现在 <see cref="SkillEffectHelper.UpdateScopeZoom"/> 中。
/// 补丁挂载在 HandleVariables 之后（zoomTime 递减之后、HandleCameraPosition 之前），
/// 这样同一帧内 HandleCameraPosition 即可使用更新后的 zoomTime。
/// </summary>
public static class ScopeZoomPatch
{
    [HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.HandleVariables))]
    [HarmonyPostfix]
    public static void PostfixHandleVariables(PlayerCamera __instance)
    {
        SkillEffectHelper.UpdateScopeZoom(__instance);
    }
}
