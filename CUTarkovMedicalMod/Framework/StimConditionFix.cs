using System.Collections.Generic;
using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 修复 WaterContainerItem.UpdateCondition 覆盖 condition 导致耐久显示异常的问题。
/// 模组针剂固定 condition = 1f (100%)。
/// </summary>
[HarmonyPatch(typeof(WaterContainerItem), nameof(WaterContainerItem.UpdateCondition))]
public static class StimConditionFix
{
    private static readonly HashSet<string> ModStimKeys = new()
    {
        MorphineItemSystem.ItemKey, Sj1ItemSystem.ItemKey, PropitalItemSystem.ItemKey,
        SJ6ItemSystem.ItemKey, Sj9ItemSystem.ItemKey, EtgCItemSystem.EtgItemKey,
        MildronateItemSystem.ItemKey, PnbItemSystem.ItemKey, ObdolbosItemSystem.ItemKey,
        Obdolbos2ItemSystem.ItemKey, SJ12ItemSystem.ItemKey, BluebloodItemSystem.ItemKey,
        MuleItemSystem.ItemKey, ZagustinItemSystem.ItemKey, Xtg12ItemSystem.ItemKey,
        TwoATwoBTGItemSystem.ItemKey,
    };

    [HarmonyPostfix]
    public static void Postfix(WaterContainerItem __instance)
    {
        var item = __instance.GetComponent<Item>();
        if (item != null && ModStimKeys.Contains(item.id))
            item.condition = 1f;
    }
}
