using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using HarmonyLib;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 针剂 buff 指示器。
/// 使用游戏原生的 MoodleManager.AddMoodle 显示 buff 图标。
///
/// 修复说明：
/// 1. 白色方框 → 补全 SJ6 图标注入；intensity 从 1 开始避免 backgroundIcons[0] 为空。
/// 2. 多针剂只显示一个 → 每个 buff 使用唯一的 intensity 值（基于顺序），避免 AddMoodle 去重。
/// 3. 存档切换图标残留 → 在 MoodleManager.Awake 时清除 _activeBuffs。
/// </summary>
public static class StimBuffIndicator
{
    private static readonly Dictionary<string, BuffEntry> _activeBuffs = new();
    private static MethodInfo? _addMoodleMethod;
    private static bool _iconsInjected;
    private static int _maxValidIntensity = 2; // 至少保证 3 个背景图标可用

    // 性能优化：缓冲区复用（节流已移除，确保每帧都能显示 buff）
    private static readonly List<string> _expiredKeysBuffer = new();
    private static readonly StringBuilder _descBuilder = new();
    private static readonly object[] _addMoodleArgs = new object[6];
    private static FieldInfo? _moodlesField;
    private static FieldInfo? _img2Field;
    private static string? _cachedUnrecName;
    private static string? _cachedUnrecDesc;

    public static void ShowBuff(
        string key, string displayName, Sprite? icon,
        float remainingSeconds, float totalSeconds, Color tintColor,
        IReadOnlyList<string>? positiveDescs = null,
        IReadOnlyList<string>? negativeDescs = null)
    {
        if (!_activeBuffs.TryGetValue(key, out var entry))
        {
            entry = new BuffEntry
            {
                Key = key,
                DisplayName = displayName,
                Icon = icon,
                TintColor = tintColor,
                PositiveEffects = positiveDescs ?? Array.Empty<string>(),
                NegativeEffects = negativeDescs ?? Array.Empty<string>()
            };
            _activeBuffs[key] = entry;
        }
        entry.Remaining = remainingSeconds;
        entry.Total = totalSeconds;
        entry.IsActive = true;
        entry.PositiveEffects = positiveDescs ?? entry.PositiveEffects;
        entry.NegativeEffects = negativeDescs ?? entry.NegativeEffects;
    }

    public static void HideBuff(string key) => _activeBuffs.Remove(key);

    /// <summary>
    /// 显示一次性效果（触发后显示10秒，标记"○一次性"）。
    /// </summary>
    public static void ShowOneTimeEffect(string key, string effect, bool isNegative = false)
    {
        // 若 BuffEntry 尚不存在（ShowBuff 还未调用），自动创建一个临时条目
        if (!_activeBuffs.TryGetValue(key, out var entry))
        {
            entry = new BuffEntry
            {
                Key = key,
                DisplayName = key,
                IsActive = true,
                Remaining = 10f,
                Total = 10f
            };
            _activeBuffs[key] = entry;
        }
        entry.OneTimeEffects.Add(new OneTimeEffectEntry
        {
            Text = $"{I18n.Tr("fw.onetime_prefix")}{(isNegative ? I18n.Tr("fw.onetime_warn") : "")} {effect}",
            IsNegative = isNegative,
            ExpireTime = Time.time + 10f
        });
    }

    /// <summary>
    /// 清除所有活跃 buff（在新存档/游戏重置时调用）。
    /// </summary>
    public static void ClearAllBuffs()
    {
        _activeBuffs.Clear();
        _iconsInjected = false; // 允许重新注入（以防 MoodleManager 被重建）
    }

    /// <summary>
    /// 将针剂图标注入 MoodleManager.icons 字典。
    /// 同时探测 backgroundIcons 有效长度，避免使用无效索引。
    /// </summary>
    private static void EnsureIconsInjected()
    {
        if (_iconsInjected) return;

        try
        {
            var manager = MoodleManager.main;
            if (manager == null) return;

            // 探测 backgroundIcons 数组长度
            var bgField = typeof(MoodleManager).GetField("backgroundIcons",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (bgField?.GetValue(manager) is Sprite[] bgIcons && bgIcons.Length > 0)
            {
                _maxValidIntensity = bgIcons.Length - 1;
                Plugin.Log.LogInfo($"[StimBuff] backgroundIcons length = {bgIcons.Length}, maxValidIntensity = {_maxValidIntensity}");
            }

            var iconsField = typeof(MoodleManager).GetField("icons",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (iconsField == null) return;

            var icons = iconsField.GetValue(manager) as IDictionary;
            if (icons == null) return;

            // 注入所有针剂图标（原始 key 和 stim_ 前缀 key）
            InjectIcon(icons, EtgCItemSystem.EtgItemKey);
            InjectIcon(icons, ZagustinItemSystem.ItemKey);
            InjectIcon(icons, MorphineItemSystem.ItemKey);
            InjectIcon(icons, SJ12ItemSystem.ItemKey);
            InjectIcon(icons, MuleItemSystem.ItemKey);
            InjectIcon(icons, PropitalItemSystem.ItemKey);
            InjectIcon(icons, SJ6ItemSystem.ItemKey);
            InjectIcon(icons, PnbItemSystem.ItemKey);
            InjectIcon(icons, Sj1ItemSystem.ItemKey);
            InjectIcon(icons, ObdolbosItemSystem.ItemKey);
            InjectIcon(icons, Sj9ItemSystem.ItemKey);
            InjectIcon(icons, BluebloodItemSystem.ItemKey);
            InjectIcon(icons, Xtg12ItemSystem.ItemKey);
            InjectIcon(icons, MildronateItemSystem.ItemKey);
            InjectIcon(icons, TwoATwoBTGItemSystem.ItemKey);
            InjectIcon(icons, Obdolbos2ItemSystem.ItemKey);
            InjectIcon(icons, GrizzlyKitItemSystem.ItemKey);
            InjectIcon(icons, AfakKitItemSystem.ItemKey);
            InjectIcon(icons, IfakKitItemSystem.ItemKey);
            InjectIcon(icons, SalewaKitItemSystem.ItemKey);
            InjectIcon(icons, AI2ItemSystem.ItemKey);
            InjectIcon(icons, GoldenStarItemSystem.ItemKey);
            InjectIcon(icons, VaselineItemSystem.ItemKey);
            InjectIcon(icons, LibatineItemSystem.ItemKey);
            InjectIcon(icons, IbuprofenItemSystem.ItemKey);
            InjectIcon(icons, MultiToolItemSystem.ItemKey);
            InjectIcon(icons, CmsKitItemSystem.ItemKey);

            // 为去重用的 stim_ 前缀 key 也注入相同图标
            InjectIconAlias(icons, EtgCItemSystem.EtgItemKey);
            InjectIconAlias(icons, ZagustinItemSystem.ItemKey);
            InjectIconAlias(icons, MorphineItemSystem.ItemKey);
            InjectIconAlias(icons, SJ12ItemSystem.ItemKey);
            InjectIconAlias(icons, MuleItemSystem.ItemKey);
            InjectIconAlias(icons, PropitalItemSystem.ItemKey);
            InjectIconAlias(icons, SJ6ItemSystem.ItemKey);
            InjectIconAlias(icons, PnbItemSystem.ItemKey);
            InjectIconAlias(icons, Sj1ItemSystem.ItemKey);
            InjectIconAlias(icons, ObdolbosItemSystem.ItemKey);
            InjectIconAlias(icons, Sj9ItemSystem.ItemKey);
            InjectIconAlias(icons, BluebloodItemSystem.ItemKey);
            InjectIconAlias(icons, Xtg12ItemSystem.ItemKey);
            InjectIconAlias(icons, MildronateItemSystem.ItemKey);
            InjectIconAlias(icons, TwoATwoBTGItemSystem.ItemKey);
            InjectIconAlias(icons, Obdolbos2ItemSystem.ItemKey);
            InjectIconAlias(icons, GrizzlyKitItemSystem.ItemKey);
            InjectIconAlias(icons, AfakKitItemSystem.ItemKey);
            InjectIconAlias(icons, IfakKitItemSystem.ItemKey);
            InjectIconAlias(icons, SalewaKitItemSystem.ItemKey);
            InjectIconAlias(icons, AI2ItemSystem.ItemKey);
            InjectIconAlias(icons, GoldenStarItemSystem.ItemKey);
            InjectIconAlias(icons, VaselineItemSystem.ItemKey);
            InjectIconAlias(icons, LibatineItemSystem.ItemKey);
            InjectIconAlias(icons, IbuprofenItemSystem.ItemKey);
            InjectIconAlias(icons, MultiToolItemSystem.ItemKey);
            InjectIconAlias(icons, CmsKitItemSystem.ItemKey);

            _iconsInjected = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[StimBuff] EnsureIconsInjected failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 通过反射调用对应 ItemSystem 的 private static TryLoadIcon() 加载 Sprite。
    /// </summary>
    private static void InjectIcon(IDictionary icons, string key)
    {
        if (icons.Contains(key)) return;

        Sprite? sprite = null;

        try
        {
            Type? systemType = key switch
            {
                _ when key == EtgCItemSystem.EtgItemKey => typeof(EtgCItemSystem),
                _ when key == ZagustinItemSystem.ItemKey => typeof(ZagustinItemSystem),
                _ when key == MorphineItemSystem.ItemKey => typeof(MorphineItemSystem),
                _ when key == SJ12ItemSystem.ItemKey => typeof(SJ12ItemSystem),
                _ when key == MuleItemSystem.ItemKey => typeof(MuleItemSystem),
                _ when key == PropitalItemSystem.ItemKey => typeof(PropitalItemSystem),
                _ when key == SJ6ItemSystem.ItemKey => typeof(SJ6ItemSystem),
                _ when key == PnbItemSystem.ItemKey => typeof(PnbItemSystem),
                _ when key == Sj1ItemSystem.ItemKey => typeof(Sj1ItemSystem),
                _ when key == ObdolbosItemSystem.ItemKey => typeof(ObdolbosItemSystem),
                _ when key == Sj9ItemSystem.ItemKey => typeof(Sj9ItemSystem),
                _ when key == BluebloodItemSystem.ItemKey => typeof(BluebloodItemSystem),
                _ when key == Xtg12ItemSystem.ItemKey => typeof(Xtg12ItemSystem),
                _ when key == MildronateItemSystem.ItemKey => typeof(MildronateItemSystem),
                _ when key == TwoATwoBTGItemSystem.ItemKey => typeof(TwoATwoBTGItemSystem),
                _ when key == Obdolbos2ItemSystem.ItemKey => typeof(Obdolbos2ItemSystem),
                _ when key == GrizzlyKitItemSystem.ItemKey => typeof(GrizzlyKitItemSystem),
                _ when key == AfakKitItemSystem.ItemKey => typeof(AfakKitItemSystem),
                _ when key == IfakKitItemSystem.ItemKey => typeof(IfakKitItemSystem),
                _ when key == SalewaKitItemSystem.ItemKey => typeof(SalewaKitItemSystem),
                _ when key == AI2ItemSystem.ItemKey => typeof(AI2ItemSystem),
                _ when key == GoldenStarItemSystem.ItemKey => typeof(GoldenStarItemSystem),
                _ when key == VaselineItemSystem.ItemKey => typeof(VaselineItemSystem),
                _ when key == LibatineItemSystem.ItemKey => typeof(LibatineItemSystem),
                _ when key == IbuprofenItemSystem.ItemKey => typeof(IbuprofenItemSystem),
                _ when key == MultiToolItemSystem.ItemKey => typeof(MultiToolItemSystem),
                _ when key == CmsKitItemSystem.ItemKey => typeof(CmsKitItemSystem),
                _ => null
            };

            if (systemType != null)
            {
                var method = systemType.GetMethod("TryLoadIcon",
                    BindingFlags.Static | BindingFlags.NonPublic);
                sprite = method?.Invoke(null, null) as Sprite;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[StimBuff] InjectIcon('{key}') reflection failed: {ex.Message}");
        }

        if (sprite != null)
        {
            icons[key] = sprite;
            Plugin.Log.LogInfo($"[StimBuff] Injected icon '{key}' into MoodleManager.icons");
        }
        else
        {
            // 图标加载失败，用游戏已有的 hunger 图标作为 fallback
            if (icons.Contains("hunger"))
            {
                icons[key] = icons["hunger"]!;
                Plugin.Log.LogWarning($"[StimBuff] Using 'hunger' icon as fallback for '{key}'");
            }
            else
            {
                Plugin.Log.LogWarning($"[StimBuff] Cannot inject icon for '{key}': no sprite and no hunger fallback");
            }
        }
    }

    internal static void AddBuffs()
    {

        try
        {
            // 清理已过期/失效的 buff（for 循环替代 LINQ，复用缓冲区）
            _expiredKeysBuffer.Clear();
            foreach (var kv in _activeBuffs)
            {
                if (!kv.Value.IsActive || kv.Value.Remaining <= 0f)
                    _expiredKeysBuffer.Add(kv.Key);
            }
            foreach (var key in _expiredKeysBuffer)
            {
                _activeBuffs.Remove(key);
                Plugin.Log.LogInfo($"[StimBuff] Auto-cleaned expired buff '{key}'");
            }

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

            // 多人模式过滤：仅显示本地 body 上有活跃效果控制器的 buff。
            Body? localBody = null;
            if (KrokMpHelper.IsMultiplayer)
            {
                try { localBody = PlayerCamera.main?.body; } catch { }
            }

            foreach (var buff in _activeBuffs.Values)
            {
                if (!buff.IsActive || buff.Remaining <= 0f) continue;

                // 多人模式：检查本地 body 是否有该控制器的活跃实例
                if (localBody != null)
                {
                    Type? ctrlType = null;
                    if (Eff.ControllerTypes.TryGetValue(buff.Key, out var exactType))
                        ctrlType = exactType;
                    else
                    {
                        foreach (var kv in Eff.ControllerTypes)
                        {
                            if (buff.Key.StartsWith(kv.Key + "_"))
                            {
                                ctrlType = kv.Value;
                                break;
                            }
                        }
                    }

                    if (ctrlType != null)
                    {
                        var ctrls = localBody.GetComponents(ctrlType);
                        bool anyEnabled = false;
                        foreach (var c in ctrls)
                        {
                            if (c is MonoBehaviour mc && mc.enabled) { anyEnabled = true; break; }
                        }
                        if (!anyEnabled)
                            continue;
                    }
                }

                // Remaining 由 ShowBuff 每帧更新，此处无需递减

                var mins = Mathf.Max(0, Mathf.FloorToInt(buff.Remaining / 60f));
                var secs = Mathf.Max(0, Mathf.FloorToInt(buff.Remaining % 60f));

                // 检查玩家智力是否足以识别此物品
                bool recognizable = true;
                try
                {
                    if (Item.GlobalItems.TryGetValue(buff.Key, out var info) && info.rec != null)
                        recognizable = info.rec.recognizable;
                }
                catch { }

                string name;
                string desc;

                if (!recognizable)
                {
                    // 缓存 I18n 翻译（语言不变时无需每帧查字典）
                    _cachedUnrecName ??= I18n.Tr("fw.unrecognized_name");
                    _cachedUnrecDesc ??= I18n.Tr("fw.unrecognized_desc");
                    name = $"{_cachedUnrecName} ({mins}:{secs:D2})";
                    desc = _cachedUnrecDesc;
                }
                else
                {
                    name = $"{buff.DisplayName} ({mins}:{secs:D2})";

                    // 清理过期的一次性效果
                    buff.OneTimeEffects.RemoveAll(ot => Time.time >= ot.ExpireTime);

                    // 用 StringBuilder 替代 new List<string> + string.Join
                    _descBuilder.Clear();
                    foreach (var e in buff.PositiveEffects)
                        _descBuilder.Append("<color=#4fc3f7>+ ").Append(e).Append("</color>\n");
                    foreach (var e in buff.NegativeEffects)
                        _descBuilder.Append("<color=#ff6666>- ").Append(e).Append("</color>\n");
                    foreach (var ot in buff.OneTimeEffects)
                        _descBuilder.Append("<color=#aaaaaa>").Append(ot.Text).Append("</color>\n");
                    desc = _descBuilder.Length > 0
                        ? _descBuilder.ToString(0, _descBuilder.Length - 1) // 去掉末尾 \n
                        : I18n.TrFmt("fw.buff_default", Mathf.CeilToInt(buff.Remaining));
                }

                var sharedIntensity = Mathf.Clamp(1, 0, _maxValidIntensity);

                // 复用 object[] 数组避免每帧 GC 分配
                _addMoodleArgs[0] = sharedIntensity;
                _addMoodleArgs[1] = $"stim_{buff.Key}";
                _addMoodleArgs[2] = name;
                _addMoodleArgs[3] = desc;
                _addMoodleArgs[4] = false;
                _addMoodleArgs[5] = false;
                _addMoodleMethod.Invoke(manager, _addMoodleArgs);

                // 缩放前景图标
                ScaleMoodleIcon(manager);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[StimBuff] AddBuffs failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 为 stim_ 前缀的 moodle key 复制对应图标。
    /// moodleKey = "stim_{originalKey}" 用于去重，但需要相同的 sprite。
    /// </summary>
    private static void InjectIconAlias(IDictionary icons, string originalKey)
    {
        var aliasKey = $"stim_{originalKey}";
        if (icons.Contains(aliasKey)) return;

        if (icons.Contains(originalKey))
        {
            icons[aliasKey] = icons[originalKey]!;
            Plugin.Log.LogInfo($"[StimBuff] Injected alias icon '{aliasKey}' (copied from '{originalKey}')");
        }
    }

    /// <summary>
    /// 找到最新创建的 Moodle GameObject，缩放前景图标。
    /// </summary>
    private static void ScaleMoodleIcon(MoodleManager manager)
    {
        try
        {
            _moodlesField ??= typeof(MoodleManager).GetField("moodles",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (_moodlesField?.GetValue(manager) is not Transform moodles) return;
            if (moodles.childCount == 0) return;

            var moodleGo = moodles.GetChild(moodles.childCount - 1);
            if (moodleGo == null) return;

            var moodleComp = moodleGo.GetComponent<Moodle>();
            if (moodleComp == null) return;

            _img2Field ??= typeof(Moodle).GetField("img2",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (_img2Field?.GetValue(moodleComp) is UnityEngine.UI.Image img2)
                img2.rectTransform.sizeDelta = new Vector2(24, 24);
        }
        catch { }
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
        public IReadOnlyList<string> PositiveEffects = Array.Empty<string>();
        public IReadOnlyList<string> NegativeEffects = Array.Empty<string>();
        public List<OneTimeEffectEntry> OneTimeEffects = new();
    }

    internal sealed class OneTimeEffectEntry
    {
        public string Text = "";
        public bool IsNegative;
        public float ExpireTime;
    }
}

/// <summary>
/// 拦截 MoodleManager.UpdateMoodles，在原生 moodle 之后追加针剂 buff 图标。
/// </summary>
[HarmonyPatch(typeof(MoodleManager), nameof(MoodleManager.UpdateMoodles))]
public static class StimBuffPatch_UpdateMoodles
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        StimBuffIndicator.AddBuffs();
        EffectBackup.Tick();
    }
}

/// <summary>
/// 在 MoodleManager 初始化（新游戏/场景加载）时清除旧 buff 状态。
/// </summary>
[HarmonyPatch(typeof(MoodleManager), nameof(MoodleManager.Awake))]
public static class StimBuffPatch_MoodleManagerAwake
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        StimBuffIndicator.ClearAllBuffs();
        Plugin.Log.LogInfo("[StimBuff] MoodleManager.Awake → cleared all buffs.");

        // 场景加载后 URP Volume 可能失效，强制管视效果刷新
        if (TunnelVisionOverlay.Instance != null)
            TunnelVisionOverlay.Instance.RefreshVolume();
    }
}
