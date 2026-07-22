using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

    // 性能优化：缓存 Item 组件引用，避免每帧 GetComponent
    private static readonly ConditionalWeakTable<WaterContainerItem, Item> _itemCache = new();

    [HarmonyPostfix]
    public static void Postfix(WaterContainerItem __instance)
    {
        Item item;
        if (_itemCache.TryGetValue(__instance, out var cached))
        {
            item = cached;
        }
        else
        {
            item = __instance.GetComponent<Item>();
            if (item != null)
                _itemCache.Add(__instance, item);
        }

        if (item != null && ModStimKeys.Contains(item.id))
            item.condition = 1f;
    }
}
