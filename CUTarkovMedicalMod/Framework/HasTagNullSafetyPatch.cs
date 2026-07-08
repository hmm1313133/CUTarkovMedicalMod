using System.Reflection;
using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 防止 ItemInfo.HasTag 在 actualTags 为 null 时崩溃。
///
/// 崩溃链：
///   某个 ItemInfo 的 actualTags 为 null（SetTags 未被调用或失败）
///   → HasTag() 调用 actualTags.Contains(tag) → ArgumentNullException
///   → 每帧从 HandleGunMenu / HandleBlockPlaceGhost / HandleVisuals 重复调用
///   → 帧率骤降 → 游戏崩溃
///
/// 此补丁在 HasTag 前检查 actualTags，为 null 时返回 false 并记录日志，
/// 便于定位是哪个 ItemInfo 缺少 SetTags 调用。
/// </summary>
[HarmonyPatch(typeof(ItemInfo), nameof(ItemInfo.HasTag))]
public static class HasTagNullSafetyPatch
{
    private static readonly FieldInfo? ActualTagsField =
        AccessTools.Field(typeof(ItemInfo), "actualTags");

    private static int _logCount = 0; // 限制日志频率

    [HarmonyPrefix]
    public static bool Prefix(ItemInfo __instance, ref bool __result, string tag)
    {
        var actualTags = ActualTagsField?.GetValue(__instance) as string[];
        if (actualTags == null)
        {
            // 限制日志频率（每 60 帧记录一次，避免日志爆炸）
            if (_logCount++ % 60 == 0)
            {
                Plugin.Log.LogWarning(
                    $"[HasTag] actualTags is null for ItemInfo '{__instance.fullName}' " +
                    $"(tags='{__instance.tags}', category='{__instance.category}'). Returning false. " +
                    $"This indicates SetTags() was not called on this ItemInfo.");
            }
            __result = false;
            return false; // 跳过原方法
        }
        return true; // 执行原方法
    }
}
