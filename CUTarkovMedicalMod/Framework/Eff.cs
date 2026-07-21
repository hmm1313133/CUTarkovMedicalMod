using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 医疗效果序列化/反序列化。
/// 被 CUCoreLibMode 的 MedicalEffectSaveProvider 调用，通过 CUCoreLib SaveCoordinator 持久化。
/// 多人模式下，Capture/Restore 接收 Body 参数，按各玩家身体独立保存/恢复效果。
/// </summary>
public static class Eff
{
    /// <summary>
    /// 序列化指定 Body 上的所有活跃医疗效果。
    /// 多人模式下使用传入的 body 参数，而非 PlayerCamera.main.body（本地玩家）。
    /// </summary>
    public static string Ser(Body? body = null)
    {
        // 优先使用传入的 body，回退到本地玩家 body（单机兼容）
        var b = body;
        if (b == null) try { b = PlayerCamera.main?.body; } catch { }
        if (b == null) return "";

        var l = new List<ESD>();
        foreach (var kv in Ctrl)
        {
            var c = b.gameObject.GetComponent(kv.Value); if (c == null || c is not MonoBehaviour mc) continue;
            if (!mc.enabled) continue;
            var d = new ESD { cn = kv.Value.FullName, r = RF(mc, "_remaining"), pt = RF(mc, "_phaseTimer"), br = RF(mc, "_buffRemaining"), dr = RF(mc, "_debuffRemaining"), el = RF(mc, "_elapsed"), sr = RF(mc, "_sideEffectRemaining") > 0f ? RF(mc, "_sideEffectRemaining") : RF(mc, "_sideEffectTimer"), it = RF(mc, "_initialTemp") };
            if (d.r <= 0f && d.pt <= 0f && d.br <= 0f) d.r = RF(mc, "_timer");
            var pf = kv.Value.GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance); if (pf != null) d.p = (int)(pf.GetValue(mc) ?? 0);
            // 控制器无 _phase 字段时，用 p 编码 _sideEffectStarted（SJ9/Blueblood）
            if (pf == null) { var ssf = kv.Value.GetField("_sideEffectStarted", BindingFlags.NonPublic | BindingFlags.Instance); if (ssf != null) d.p = (bool)(ssf.GetValue(mc) ?? false) ? 1 : 0; }
            // Obdolbos: 序列化 _outcome (enum->int) 和 _outcomeApplied (bool->int)
            d.o = RI(mc, "_outcome"); d.oa = RB(mc, "_outcomeApplied");
            // Obdolbos2: 序列化 _buffStarted, _sideEffectActive, _sideEffectCompleted, _injectionCount, _staminaCapBaseline
            d.bs = RB(mc, "_buffStarted"); d.sa = RB(mc, "_sideEffectActive"); d.sc = RB(mc, "_sideEffectCompleted");
            d.ic = RI(mc, "_injectionCount"); d.scb = RF(mc, "_staminaCapBaseline");
            if (d.r > 0f || d.pt > 0f || d.br > 0f || d.dr > 0f || d.sr > 0f) l.Add(d);
        }
        return l.Count == 0 ? "" : JsonConvert.SerializeObject(l);
    }

    /// <summary>
    /// 反序列化医疗效果并恢复到指定 Body。
    /// 多人模式下使用传入的 body 参数，而非 PlayerCamera.main.body（本地玩家）。
    /// </summary>
    public static void Res(string json, Body? body = null)
    {
        if (string.IsNullOrEmpty(json)) return;
        var l = JsonConvert.DeserializeObject<List<ESD>>(json); if (l == null || l.Count == 0) return;

        // 优先使用传入的 body，回退到本地玩家 body（单机兼容）
        var b = body;
        if (b == null) try { b = PlayerCamera.main?.body; } catch { }
        if (b == null) return;

        int n = 0;
        foreach (var d in l)
        {
            try
            {
                var t = Find(d.cn); if (t == null) continue;
                // 移除同类型旧控制器，防止残留的已禁用实例导致 GetComponent 返回错误引用
                var existing = b.GetComponents(t);
                foreach (var ex in existing)
                {
                    if (ex is MonoBehaviour mb) mb.enabled = false;
                    try { UnityEngine.Object.Destroy(ex); } catch { }
                }
                var mc = b.gameObject.AddComponent(t) as MonoBehaviour; if (mc == null) continue;
                WF(mc, "_remaining", d.r); WF(mc, "_timer", d.r); WF(mc, "_phaseTimer", d.pt); WF(mc, "_buffRemaining", d.br); WF(mc, "_debuffRemaining", d.dr); WF(mc, "_elapsed", d.el); WF(mc, "_sideEffectRemaining", d.sr); WF(mc, "_sideEffectTimer", d.sr); WF(mc, "_initialTemp", d.it);
                var pf = t.GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance); if (pf != null) pf.SetValue(mc, d.p);
                // 控制器无 _phase 字段时，从 p 恢复 _sideEffectStarted（SJ9/Blueblood）
                if (pf == null) { var ssf = t.GetField("_sideEffectStarted", BindingFlags.NonPublic | BindingFlags.Instance); if (ssf != null) ssf.SetValue(mc, d.p != 0); }
                // Obdolbos: 恢复 _outcome, _outcomeApplied
                WI(mc, "_outcome", d.o); WB(mc, "_outcomeApplied", d.oa);
                // Obdolbos2: 恢复 _buffStarted, _sideEffectActive, _sideEffectCompleted, _injectionCount, _staminaCapBaseline
                WB(mc, "_buffStarted", d.bs); WB(mc, "_sideEffectActive", d.sa); WB(mc, "_sideEffectCompleted", d.sc);
                WI(mc, "_injectionCount", d.ic); WF(mc, "_staminaCapBaseline", d.scb);
                var bf = t.GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance); if (bf != null) bf.SetValue(mc, b);
                var af = t.GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance); if (af != null) af.SetValue(mc, true);
                mc.enabled = true; n++;
            }
            catch { }
        }
    }

    /// <summary>
    /// 所有已注册的医疗效果控制器类型（公开访问，供 KrokMpHealthSyncPatch 检查活跃效果）。
    /// </summary>
    public static IReadOnlyDictionary<string, Type> ControllerTypes => Ctrl;

    static float RF(MonoBehaviour mc, string n) { var f = mc.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); return f != null ? (float)(f.GetValue(mc) ?? 0f) : 0f; }
    static void WF(MonoBehaviour mc, string n, float v) { var f = mc.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); if (f != null) f.SetValue(mc, v); }
    static int RI(MonoBehaviour mc, string n) { var f = mc.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); return f != null ? Convert.ToInt32(f.GetValue(mc) ?? 0) : 0; }
    static void WI(MonoBehaviour mc, string n, int v) { var f = mc.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); if (f != null) f.SetValue(mc, v); }
    static int RB(MonoBehaviour mc, string n) { var f = mc.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); return f != null && (bool)(f.GetValue(mc) ?? false) ? 1 : 0; }
    static void WB(MonoBehaviour mc, string n, int v) { var f = mc.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); if (f != null) f.SetValue(mc, v != 0); }
    static Type? Find(string n) { foreach (var kv in Ctrl) if (kv.Value.FullName == n) return kv.Value; return null; }
    static readonly Dictionary<string, Type> Ctrl = new() { ["etg_c"]=typeof(EtgStimEffectController),["zagustin"]=typeof(ZagustinEffectController),["cu_morphine"]=typeof(MorphineEffectController),["sj12"]=typeof(SJ12EffectController),["mule"]=typeof(MuleEffectController),["propital"]=typeof(PropitalEffectController),["sj6"]=typeof(SJ6EffectController),["sj1"]=typeof(Sj1EffectController),["pnb"]=typeof(PnbEffectController),["sj9"]=typeof(Sj9EffectController),["blueblood"]=typeof(BluebloodEffectController),["xtg12"]=typeof(Xtg12EffectController),["mildronate"]=typeof(MildronateEffectController),["2a2btg"]=typeof(TwoATwoBTGEffectController),["ibuprofen"]=typeof(IbuprofenEffectController),["obdolbos"]=typeof(ObdolbosEffectController),["obdolbos2"]=typeof(Obdolbos2EffectController),["libatine"]=typeof(LibatineEffectController),["goldenstar"]=typeof(GoldenStarEffectController), };
    [Serializable] class ESD { public string cn = ""; public float r, pt, br, dr, el, sr, it, scb; public int p, o, oa, bs, sa, sc, ic; }
}
