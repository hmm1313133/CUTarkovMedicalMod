using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 针剂 buff 指示器。
/// 使用游戏原生的 MoodleManager.AddMoodle 显示 buff 图标。
/// AddMoodle 会从 MoodleManager.icons 字典中查找 icon 参数对应的 Sprite 作为前景图标。
/// </summary>
public static class StimBuffIndicator
{
    private static readonly Dictionary<string, BuffEntry> _activeBuffs = new();
    private static MethodInfo? _addMoodleMethod;
    private static bool _iconsInjected;

    public static void ShowBuff(string key, string displayName, Sprite? icon, float remainingSeconds, float totalSeconds, Color tintColor)
    {
        if (!_activeBuffs.TryGetValue(key, out var entry))
        {
            entry = new BuffEntry { Key = key, DisplayName = displayName, Icon = icon, TintColor = tintColor };
            _activeBuffs[key] = entry;
        }
        entry.Remaining = remainingSeconds;
        entry.Total = totalSeconds;
        entry.IsActive = true;
    }

    public static void HideBuff(string key) => _activeBuffs.Remove(key);

    /// <summary>
    /// 将针剂图标注入 MoodleManager.icons 字典。
/// </summary>
    private static void EnsureIconsInjected()
    {
        if (_iconsInjected) return;

        try
        {
            var manager = MoodleManager.main;
            if (manager == null) return;

            var iconsField = typeof(MoodleManager).GetField("icons",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (iconsField == null) return;

            var icons = iconsField.GetValue(manager) as IDictionary;
            if (icons == null) return;

            // 注入 ETG-c 图标
            InjectIcon(icons, EtgCItemSystem.EtgItemKey, "etg_c");
            // 注入 Zagustin 图标
            InjectIcon(icons, ZagustinItemSystem.ItemKey, "zagustin");
            // 注入 Morphine 图标
            InjectIcon(icons, MorphineItemSystem.ItemKey, "morphine");

            _iconsInjected = true;
        }
        catch { }
    }

    private static void InjectIcon(IDictionary icons, string key, string iconName)
    {
        if (icons.Contains(key)) return;

        // 获取对应的 Sprite
        Sprite? sprite = null;
        if (key == EtgCItemSystem.EtgItemKey)
        {
            var method = typeof(EtgCItemSystem).GetMethod("TryLoadIcon",
                BindingFlags.Static | BindingFlags.NonPublic);
            sprite = method?.Invoke(null, null) as Sprite;
        }
        else if (key == ZagustinItemSystem.ItemKey)
        {
            var method = typeof(ZagustinItemSystem).GetMethod("TryLoadIcon",
                BindingFlags.Static | BindingFlags.NonPublic);
            sprite = method?.Invoke(null, null) as Sprite;
        }
        else if (key == MorphineItemSystem.ItemKey)
        {
            var method = typeof(MorphineItemSystem).GetMethod("TryLoadIcon",
                BindingFlags.Static | BindingFlags.NonPublic);
            sprite = method?.Invoke(null, null) as Sprite;
        }

        if (sprite != null)
        {
            icons[key] = sprite;
            Plugin.Log.LogInfo($"[StimBuff] Injected icon '{key}' into MoodleManager.icons");
        }
        else
        {
            // 图标加载失败，用游戏已有的图标作为 fallback
            // "hunger" 是游戏自带的 moodle 图标
            if (icons.Contains("hunger"))
            {
                icons[key] = icons["hunger"];
                Plugin.Log.LogWarning($"[StimBuff] Using 'hunger' icon as fallback for '{key}'");
            }
        }
    }

    internal static void AddBuffs()
    {
        try
        {
            if (_activeBuffs.Count == 0) return;

            var manager = MoodleManager.main;
            if (manager == null) return;

            // 确保图标已注入
            EnsureIconsInjected();

            if (_addMoodleMethod == null)
            {
                _addMoodleMethod = typeof(MoodleManager).GetMethod("AddMoodle",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (_addMoodleMethod == null) return;
            }

            foreach (var buff in _activeBuffs.Values)
            {
                if (!buff.IsActive || buff.Remaining <= 0f) continue;

                var mins = Mathf.Max(0, Mathf.FloorToInt(buff.Remaining / 60f));
                var secs = Mathf.Max(0, Mathf.FloorToInt(buff.Remaining % 60f));
                var name = $"{buff.DisplayName} ({mins}:{secs:D2})";
                var desc = $"针剂效果持续中，剩余 {Mathf.CeilToInt(buff.Remaining)} 秒";

                _addMoodleMethod.Invoke(manager, new object[]
                {
                    0,         // intensity (backgroundIcons 索引)
                    buff.Key,  // icon key (从 icons 字典查找前景图标)
                    name,      // tooltip 名称
                    desc,      // tooltip 描述
                    false,     // not critical
                    false      // not chippedOnly
                });

                // AddMoodle 调用 SetNativeSize()，导致图标按原始尺寸显示（32x32 太大）。
                // 找到新创建的 moodle，缩小前景图标 img2 以匹配背景圆形边框。
                var moodlesField = typeof(MoodleManager).GetField("moodles",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (moodlesField?.GetValue(manager) is Transform moodles)
                {
                    // 最后一个子对象就是新创建的 moodle
                    var moodleGo = moodles.GetChild(moodles.childCount - 1);
                    if (moodleGo != null)
                    {
                        // Moodle.img2 是子对象 "MoodleInside" 的 Image
                        // 背景图标 (img) 通常是 64x64，前景图标应该比背景小
                        var moodleComp = moodleGo.GetComponent<Moodle>();
                        if (moodleComp != null)
                        {
                            var img2Field = typeof(Moodle).GetField("img2",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (img2Field?.GetValue(moodleComp) is UnityEngine.UI.Image img2)
                            {
                                // 缩小前景图标到背景的 ~70% 大小
                                var rt = img2.rectTransform;
                                rt.sizeDelta = new Vector2(24, 24); // 缩小到背景的 ~37%
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[StimBuff] AddBuffs failed: {ex.Message}");
        }
    }

    internal sealed class BuffEntry
    {
        public string Key = "";
        public string DisplayName = "";
        public Sprite? Icon;
        public Color TintColor = Color.white;
        public float Remaining;
        public float Total;
        public bool IsActive;
    }
}

/// <summary>
/// 拦截 MoodleManager.UpdateMoodles（public 方法）。
/// </summary>
[HarmonyPatch(typeof(MoodleManager), nameof(MoodleManager.UpdateMoodles))]
public static class StimBuffPatch_UpdateMoodles
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        StimBuffIndicator.AddBuffs();
    }
}
