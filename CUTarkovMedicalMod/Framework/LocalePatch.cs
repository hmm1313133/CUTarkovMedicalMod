using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 拦截 Locale.GetString —— 为自定义弹药注入正确的名称和描述到游戏语言系统。
///
/// 原版逻辑：
///   Locale.GetItem(id) → GetString(id, 0) → Language.main[id]
///   键格式："556round" (无后缀) → 名称, "556rounddsc" → 描述
/// 但我们的 locale JSON 用 "76251bpz.name" / "76251bpz.desc" 格式。
///
/// 此 Postfix 在原版查找失败时（返回原始 key），回退到我们的 I18n 系统。
/// 同时也在 SetUpRecipes 后直接注入条目到 Language.main 以优化首次查找。
/// </summary>
[HarmonyPatch(typeof(Locale), nameof(Locale.GetString))]
public static class LocalePatch
{
    // 自定义弹药 ID → I18n name key 的映射
    private static readonly Dictionary<string, string> AmmoNameKeys = new()
    {
        { Ammo338UCWItemSystem.ItemKey, $"{Ammo338UCWItemSystem.ItemKey}.name" },
        { Ammo76251BPZItemSystem.ItemKey, $"{Ammo76251BPZItemSystem.ItemKey}.name" },
        { Ammo76239SPItemSystem.ItemKey, $"{Ammo76239SPItemSystem.ItemKey}.name" },
        { Ammo12g85ItemSystem.ItemKey, $"{Ammo12g85ItemSystem.ItemKey}.name" },
        { Ammo50CopperItemSystem.ItemKey, $"{Ammo50CopperItemSystem.ItemKey}.name" },
        { Ammo45FMJItemSystem.ItemKey, $"{Ammo45FMJItemSystem.ItemKey}.name" },
        { Ammo919PSOItemSystem.ItemKey, $"{Ammo919PSOItemSystem.ItemKey}.name" },
        { Ammo55645FMJItemSystem.ItemKey, $"{Ammo55645FMJItemSystem.ItemKey}.name" },
        { Ammo5728SB193ItemSystem.ItemKey, $"{Ammo5728SB193ItemSystem.ItemKey}.name" },
    };

    // 自定义弹药 ID → I18n desc key 的映射（游戏用 "iddsc" 格式）
    private static readonly Dictionary<string, string> AmmoDescKeys = new()
    {
        { $"{Ammo338UCWItemSystem.ItemKey}dsc", $"{Ammo338UCWItemSystem.ItemKey}.desc" },
        { $"{Ammo76251BPZItemSystem.ItemKey}dsc", $"{Ammo76251BPZItemSystem.ItemKey}.desc" },
        { $"{Ammo76239SPItemSystem.ItemKey}dsc", $"{Ammo76239SPItemSystem.ItemKey}.desc" },
        { $"{Ammo12g85ItemSystem.ItemKey}dsc", $"{Ammo12g85ItemSystem.ItemKey}.desc" },
        { $"{Ammo50CopperItemSystem.ItemKey}dsc", $"{Ammo50CopperItemSystem.ItemKey}.desc" },
        { $"{Ammo45FMJItemSystem.ItemKey}dsc", $"{Ammo45FMJItemSystem.ItemKey}.desc" },
        { $"{Ammo919PSOItemSystem.ItemKey}dsc", $"{Ammo919PSOItemSystem.ItemKey}.desc" },
        { $"{Ammo55645FMJItemSystem.ItemKey}dsc", $"{Ammo55645FMJItemSystem.ItemKey}.desc" },
        { $"{Ammo5728SB193ItemSystem.ItemKey}dsc", $"{Ammo5728SB193ItemSystem.ItemKey}.desc" },
    };

    // 是否已注入到游戏 Language 字典
    private static bool _injected = false;

    // 注意：原方法参数名是 str 和 type，Harmony 要求 Postfix 参数名必须精确匹配！
    // 之前用了 key/category 导致 PatchAll() 整体崩溃
    [HarmonyPostfix]
    public static void Postfix(string str, int type, ref string __result)
    {
        // 原版 GetString 在找不到 key 时返回原始 key 字符串
        // 如果返回值等于 key，说明原版没找到，检查我们的 I18n
        if (__result == str)
        {
            // 仅处理 type=0 (items/main) 和 type=3 (other)
            if (type == 0)
            {
                // 检查是否是自定义弹药名称键（如 "76251bpz"）
                if (AmmoNameKeys.TryGetValue(str, out var i18nKey))
                {
                    var translated = I18n.Tr(i18nKey);
                    if (translated != i18nKey) // I18n.Tr 找到了翻译
                    {
                        __result = translated;
                        return;
                    }
                }

                // 检查是否是自定义弹药描述键（如 "76251bpzdsc"）
                if (AmmoDescKeys.TryGetValue(str, out var descKey))
                {
                    var translated = I18n.Tr(descKey);
                    if (translated != descKey)
                    {
                        __result = translated;
                        return;
                    }
                }
            }
            else if (type == 3)
            {
                // 液体结果名用 Locale.GetOther → type=3
                // biochem 等液体名称可能也在这里查找
                // 暂不处理，biochem 是原版已有的液体
            }
        }
    }

    /// <summary>
    /// 将自定义弹药名称和描述直接注入到游戏的 Language.main 字典中。
    /// 在 SetUpRecipes 后调用，确保配方 UI 能直接找到条目（不需要每次走 Postfix 回退）。
    /// </summary>
    public static void InjectCustomEntries()
    {
        if (_injected) return;

        try
        {
            var lang = Locale.currentLang;
            if (lang == null || lang.main == null)
            {
                Plugin.Log.LogWarning("[LocalePatch] Language not loaded yet, skipping injection.");
                return;
            }

            // 注入弹药名称
            foreach (var kvp in AmmoNameKeys)
            {
                var translated = I18n.Tr(kvp.Value);
                if (translated != kvp.Value && !lang.main.ContainsKey(kvp.Key))
                {
                    lang.main[kvp.Key] = translated;
                }
            }

            // 注入弹药描述
            foreach (var kvp in AmmoDescKeys)
            {
                var translated = I18n.Tr(kvp.Value);
                if (translated != kvp.Value && !lang.main.ContainsKey(kvp.Key))
                {
                    lang.main[kvp.Key] = translated;
                }
            }

            _injected = true;
            Plugin.Log.LogInfo("[LocalePatch] Injected custom ammo locale entries into Language.main.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[LocalePatch] Failed to inject locale entries: {ex}");
        }
    }
}
