using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 管理临时免疫力降低效果。
/// 游戏 Body.HandlePeriodicChecks 每帧调用，但 immunity 重算在定时器条件块内（约每 0.5 秒一次）：
///   immunity = 100 + (hunger-70)*0.75 + (thirst-60)*0.3 + (energy-60)*0.2
///              + (temp-37)*8 + (bloodVol-100)*0.2 - max(0,dirty-50)
///              - sickness*0.8 - rad*0.5
///   if (antibioticImmunityTime > 0) immunity += 70
///   immunity = Clamp(immunity, 0, 200)
///
/// 直接修改 body.immunity 会被下一次重算覆盖（蓝血"立即回升"问题的根因）。
/// 使用 Postfix 会在非重算帧重复减幅，导致免疫力在 0 和正确值之间跳动。
/// 本管理器通过 Transpiler 在 IL 的 stfld immunity（Clamp 后的最终写入）前插入减法，
/// 确保只在 immunity 实际被重算时应用一次减幅。
/// </summary>
public static class ImmunityReductionManager
{
    private struct ReductionEntry
    {
        public float Amount;
        public float ExpiryTime; // Time.time 到期时刻；-1 = 永不过期
    }

    private static readonly Dictionary<Body, List<ReductionEntry>> _reductions = new();

    /// <summary>
    /// 添加免疫力降低。
    /// <param name="body">目标 Body</param>
    /// <param name="amount">降低量（百分比单位，如 10 表示 -10 免疫力）</param>
    /// <param name="duration">持续秒数；&lt;=0 表示永不过期</param>
    /// </summary>
    public static void AddReduction(Body body, float amount, float duration = -1f)
    {
        if (body == null || amount <= 0f) return;

        if (!_reductions.ContainsKey(body))
            _reductions[body] = new List<ReductionEntry>();

        _reductions[body].Add(new ReductionEntry
        {
            Amount = amount,
            ExpiryTime = duration > 0f ? Time.time + duration : -1f
        });

        Plugin.Log.LogInfo($"[ImmunityReduction] Added -{amount} immunity for {(duration > 0f ? duration + "s" : "permanent")} on {body.name}.");
    }

    /// <summary>
    /// 获取当前总免疫力降低量（自动清除过期条目）。
    /// </summary>
    public static float GetTotalReduction(Body body)
    {
        if (body == null) return 0f;
        if (!_reductions.TryGetValue(body, out var list) || list.Count == 0)
            return 0f;

        float total = 0f;
        var now = Time.time;
        list.RemoveAll(e => e.ExpiryTime > 0f && e.ExpiryTime <= now);
        foreach (var entry in list)
            total += entry.Amount;
        return total;
    }

    /// <summary>
    /// 清除指定 Body 的所有免疫力降低。
    /// </summary>
    public static void ClearReductions(Body body)
    {
        if (body != null && _reductions.ContainsKey(body))
        {
            _reductions[body].Clear();
            Plugin.Log.LogInfo($"[ImmunityReduction] Cleared all reductions on {body.name}.");
        }
    }
}

/// <summary>
/// 管理多来源抵抗力加成（如 xTG-12 +70、力百汀 +80、布洛芬 +50）。
/// 规则：相同增益同时使用时，取效果更强的那个，并重置时间。
/// 如果效果更强的那个先结束，将剩余时间改回剩下的那个针剂的数值。
///
/// 实现：按 amount 降序排列所有活跃条目，取最高的作为当前生效加成。
/// 游戏原生的 antibioticImmunityTime +70 也被纳入管理（xTG-12 直接设 antibioticImmunityTime）。
/// 自定义药品通过 AddBonus 注册，不直接设 antibioticImmunityTime。
/// </summary>
public static class ImmunityBonusManager
{
    private struct BonusEntry
    {
        public float Amount;      // 加成值（如 50、80）
        public float ExpiryTime;  // Time.time 到期时刻
        public string Source;     // 来源标识（如 "ibuprofen"、"libatine"）
    }

    private static readonly Dictionary<Body, List<BonusEntry>> _bonuses = new();

    /// <summary>
    /// 添加抵抗力加成。
    /// 如果同来源的加成已存在，刷新其时间和数值。
    /// </summary>
    public static void AddBonus(Body body, float amount, float duration, string source)
    {
        if (body == null || amount <= 0f || duration <= 0f) return;

        if (!_bonuses.ContainsKey(body))
            _bonuses[body] = new List<BonusEntry>();

        var list = _bonuses[body];
        // 移除同来源的旧条目
        list.RemoveAll(e => e.Source == source);

        list.Add(new BonusEntry
        {
            Amount = amount,
            ExpiryTime = Time.time + duration,
            Source = source
        });

        Plugin.Log.LogInfo($"[ImmunityBonus] Added +{amount} immunity for {duration}s from '{source}' on {body.name}. Active bonuses: {list.Count}.");
    }

    /// <summary>
    /// 获取当前最高抵抗力加成（自动清除过期条目）。
    /// 规则：取 amount 最高的活跃条目。
    /// </summary>
    public static float GetTopBonus(Body body)
    {
        if (body == null) return 0f;
        if (!_bonuses.TryGetValue(body, out var list) || list.Count == 0)
            return 0f;

        var now = Time.time;
        list.RemoveAll(e => e.ExpiryTime <= now);

        if (list.Count == 0) return 0f;

        float best = 0f;
        foreach (var entry in list)
            if (entry.Amount > best)
                best = entry.Amount;
        return best;
    }

    /// <summary>
    /// 清除指定来源的加成。
    /// </summary>
    public static void ClearBonus(Body body, string source)
    {
        if (body != null && _bonuses.TryGetValue(body, out var list))
        {
            list.RemoveAll(e => e.Source == source);
        }
    }
}

/// <summary>
/// Transpiler：在 HandlePeriodicChecks 的 IL 中：
/// 1) 在第 2 次 stfld immunity（antibiotic +70 后）前插入 ImmunityBonusManager.GetTopBonus
/// 2) 在第 3 次 stfld immunity（Clamp 后）前插入 ImmunityReductionManager.GetTotalReduction
/// </summary>
[HarmonyPatch(typeof(Body), "HandlePeriodicChecks")]
public static class ImmunityReductionPatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var immunityField = AccessTools.Field(typeof(Body), nameof(Body.immunity));
        var reductionMethod = AccessTools.Method(
            typeof(ImmunityReductionManager),
            nameof(ImmunityReductionManager.GetTotalReduction));
        var bonusMethod = AccessTools.Method(
            typeof(ImmunityBonusManager),
            nameof(ImmunityBonusManager.GetTopBonus));
        var maxMethod = AccessTools.Method(
            typeof(Mathf), nameof(Mathf.Max),
            new[] { typeof(float), typeof(float) });

        // HandlePeriodicChecks 中有 3 次 stfld immunity：
        //   1) 初始计算后 ← 在这前面插入 bonus 加成（无条件执行）
        //   2) antibioticImmunityTime > 0 时 +70 后（条件块内，可能不执行）
        //   3) Mathf.Clamp(0, 200) 后 ← 在这前面插入 reduction 减幅
        return new CodeMatcher(instructions)
            // 第 1 次 stfld immunity（基础计算后）— 前面插入 bonus
            .MatchForward(false, new CodeMatch(OpCodes.Stfld, immunityField))
            .ThrowIfInvalid("Could not find first stfld immunity in Body.HandlePeriodicChecks")
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, bonusMethod),
                new CodeInstruction(OpCodes.Add))
            .Advance(1)
            // 第 2 次 stfld immunity（+70 后，条件块内）— 跳过
            .MatchForward(false, new CodeMatch(OpCodes.Stfld, immunityField))
            .Advance(1)
            // 第 3 次 stfld immunity（Clamp 后）— 前面插入 reduction
            .MatchForward(false, new CodeMatch(OpCodes.Stfld, immunityField))
            .ThrowIfInvalid("Could not find third stfld immunity in Body.HandlePeriodicChecks")
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, reductionMethod),
                new CodeInstruction(OpCodes.Sub),
                new CodeInstruction(OpCodes.Ldc_R4, 0f),
                new CodeInstruction(OpCodes.Call, maxMethod))
            .InstructionEnumeration();
    }
}

/// <summary>
/// 管理多来源耐力恢复加成（如 SJ6 +120%、Obdolbos +80%、布洛芬 +20%）。
/// 规则：相同增益同时使用时，取效果更强的那个，并重置时间。
/// 如果效果更强的那个先结束，将剩余时间改回剩下的那个针剂的数值。
///
/// 各药品的 EffectController 每帧调用 GetTopBonus 获取当前最高加成比例，
/// 然后自行计算 extraRecovery = staminaStrength.Evaluate(...) * deltaTime * bonus。
/// </summary>
public static class StaminaBonusManager
{
    private struct StaminaEntry
    {
        public float Bonus;     // 加成比例（如 0.2 = +20%, 0.8 = +80%, 1.2 = +120%）
        public float ExpiryTime;
        public string Source;
    }

    private static readonly Dictionary<Body, List<StaminaEntry>> _entries = new();

    /// <summary>
    /// 添加耐力恢复加成。同来源自动刷新。
    /// </summary>
    public static void AddBonus(Body body, float bonus, float duration, string source)
    {
        if (body == null || bonus <= 0f || duration <= 0f) return;

        if (!_entries.ContainsKey(body))
            _entries[body] = new List<StaminaEntry>();

        var list = _entries[body];
        list.RemoveAll(e => e.Source == source);

        list.Add(new StaminaEntry
        {
            Bonus = bonus,
            ExpiryTime = Time.time + duration,
            Source = source
        });

        Plugin.Log.LogInfo($"[StaminaBonus] Added +{bonus*100}% stamina recovery for {duration}s from '{source}'. Active: {list.Count}.");
    }

    /// <summary>
    /// 获取当前最高耐力恢复加成比例（自动清除过期条目）。
    /// </summary>
    public static float GetTopBonus(Body body)
    {
        if (body == null) return 0f;
        if (!_entries.TryGetValue(body, out var list) || list.Count == 0)
            return 0f;

        var now = Time.time;
        list.RemoveAll(e => e.ExpiryTime <= now);

        if (list.Count == 0) return 0f;

        float best = 0f;
        foreach (var entry in list)
            if (entry.Bonus > best)
                best = entry.Bonus;
        return best;
    }

    /// <summary>
    /// 判断指定来源是否为当前最高耐力恢复加成的持有者。
    /// 用于确保多来源同时存在时，只有最强效果的那个控制器每帧追加恢复量，
    /// 避免多个控制器重复追加导致恢复量翻倍。
    /// </summary>
    public static bool IsTopSource(Body body, string source)
    {
        if (body == null || string.IsNullOrEmpty(source)) return false;
        if (!_entries.TryGetValue(body, out var list) || list.Count == 0)
            return false;

        var now = Time.time;
        list.RemoveAll(e => e.ExpiryTime <= now);

        if (list.Count == 0) return false;

        float best = 0f;
        string? bestSource = null;
        foreach (var entry in list)
        {
            if (entry.Bonus > best)
            {
                best = entry.Bonus;
                bestSource = entry.Source;
            }
        }
        return bestSource == source;
    }

    /// <summary>
    /// 清除指定来源的加成。
    /// </summary>
    public static void ClearBonus(Body body, string source)
    {
        if (body != null && _entries.TryGetValue(body, out var list))
            list.RemoveAll(e => e.Source == source);
    }
}

/// <summary>
/// 管理多来源耐力上限加成（如 SJ6 +20%、米屈肼 +10%）。
/// 规则与 ImmunityBonusManager 相同：相同增益同时使用时，取效果更强的那个，并重置时间。
/// 如果效果更强的那个先结束，将剩余时间改回剩下的那个针剂的数值。
///
/// 各药品的 EffectController 每帧调用 IsTopSource 判断自己是否为当前最强来源，
/// 只有最强来源的控制器才执行耐力上限 clamp，避免多个控制器互相干扰。
/// </summary>
public static class StaminaCapBonusManager
{
    private struct CapEntry
    {
        public float Bonus;     // 上限增加比例（如 0.20 = +20%, 0.10 = +10%）
        public float ExpiryTime;
        public string Source;
    }

    private static readonly Dictionary<Body, List<CapEntry>> _entries = new();

    /// <summary>
    /// 添加耐力上限加成。同来源自动刷新。
    /// </summary>
    public static void AddBonus(Body body, float bonus, float duration, string source)
    {
        if (body == null || bonus <= 0f || duration <= 0f) return;

        if (!_entries.ContainsKey(body))
            _entries[body] = new List<CapEntry>();

        var list = _entries[body];
        list.RemoveAll(e => e.Source == source);

        list.Add(new CapEntry
        {
            Bonus = bonus,
            ExpiryTime = Time.time + duration,
            Source = source
        });

        Plugin.Log.LogInfo($"[StaminaCapBonus] Added +{bonus*100}% stamina cap for {duration}s from '{source}'. Active: {list.Count}.");
    }

    /// <summary>
    /// 获取当前最高耐力上限加成比例（自动清除过期条目）。
    /// </summary>
    public static float GetTopBonus(Body body)
    {
        if (body == null) return 0f;
        if (!_entries.TryGetValue(body, out var list) || list.Count == 0)
            return 0f;

        var now = Time.time;
        list.RemoveAll(e => e.ExpiryTime <= now);

        if (list.Count == 0) return 0f;

        float best = 0f;
        foreach (var entry in list)
            if (entry.Bonus > best)
                best = entry.Bonus;
        return best;
    }

    /// <summary>
    /// 判断指定来源是否为当前最高耐力上限加成的持有者。
    /// </summary>
    public static bool IsTopSource(Body body, string source)
    {
        if (body == null || string.IsNullOrEmpty(source)) return false;
        if (!_entries.TryGetValue(body, out var list) || list.Count == 0)
            return false;

        var now = Time.time;
        list.RemoveAll(e => e.ExpiryTime <= now);

        if (list.Count == 0) return false;

        float best = 0f;
        string? bestSource = null;
        foreach (var entry in list)
        {
            if (entry.Bonus > best)
            {
                best = entry.Bonus;
                bestSource = entry.Source;
            }
        }
        return bestSource == source;
    }

    /// <summary>
    /// 清除指定来源的加成。
    /// </summary>
    public static void ClearBonus(Body body, string source)
    {
        if (body != null && _entries.TryGetValue(body, out var list))
            list.RemoveAll(e => e.Source == source);
    }
}
