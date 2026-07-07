using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 悬停描述辅助方法。
/// 不按 SHIFT 时隐藏效果详细描述，按 SHIFT 展开时显示全部效果。
/// </summary>
public static class HoverDescriptionHelper
{
    /// <summary>
    /// 当未按下 expanddesc 键时，从描述中移除效果部分（\n\n&lt;color 开头的内容），
    /// 仅保留 lore 文本和重量/价值等基础信息。
    /// </summary>
    public static void StripEffectsWhenNotExpanded(ref (string, string) result)
    {
        // 未按下 SHIFT → 移除效果详情
        if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
        {
            int effectsStart = result.Item2.IndexOf("\n\n<color");
            if (effectsStart > 0)
            {
                // 找到重量行（<color=#ffffff><sprite index=0...）的起始位置
                int weightStart = result.Item2.IndexOf("<color=#ffffff><sprite", effectsStart + 1);
                if (weightStart > effectsStart)
                {
                    result.Item2 = result.Item2.Substring(0, effectsStart)
                                 + "\n\n"
                                 + result.Item2.Substring(weightStart);
                }
            }
        }
    }
}
