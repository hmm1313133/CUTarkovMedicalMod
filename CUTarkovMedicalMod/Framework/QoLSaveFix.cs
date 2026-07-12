using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// QoL 兼容修复。Plugin.cs 在 Awake 中调用 QoLSaveFix.Register(harmony)。
/// 仅当 QoL.Unknown.dll 存在时才注册补丁，无 QoL 时原版不变。
/// </summary>
public static class QoLSaveFix
{
    public static bool HasQoL()
    {
        return File.Exists(Path.Combine(Application.dataPath, "../BepInEx/plugins/QoL.Unknown.dll"));
    }

    public static void Register(HarmonyLib.Harmony harmony)
    {
        var mSave = typeof(SaveSystem).GetMethod("SaveGame");
        var mLoad = typeof(SaveSystem).GetMethod("TryLoadGame");
        harmony.Patch(mSave, postfix: new HarmonyLib.HarmonyMethod(typeof(QoLSaveFix_Save), "Postfix"));
        harmony.Patch(mLoad, prefix: new HarmonyLib.HarmonyMethod(typeof(QoLSaveFix_Load), "Prefix"));
        harmony.Patch(mLoad, postfix: new HarmonyLib.HarmonyMethod(typeof(QoLSaveFix_Load), "Postfix"));
        Plugin.Log.LogInfo("[QoLSaveFix] QoL detected, registered Save/Load patches.");
    }
}

public static class QoLSaveFix_Save
{
    static int _qid;
    static List<SD>? Collect()
    {
        Body? b = null; try { b = PlayerCamera.main?.body; } catch { } if (b == null) return null;
        var l = new List<SD>(); _qid = 0;
        if (b.slots != null) for (int i = 0; i < b.slots.Length; i++) if (b.HoldingItem(i)) SR(b.GetItem(i), l, i);
        if (b.GetAllWearables() != null) foreach (var w in b.GetAllWearables()) if (w != null && !string.IsNullOrEmpty(w.id)) SR(w, l, -1);
        return l;
    }
    static void SR(Item it, List<SD> l, int s, int ps = -1)
    {
        if (it == null || string.IsNullOrEmpty(it.id)) return;
        if (IsCustom(it.id))
        {
            int a = -1; var am = it.GetComponent<AmmoScript>(); if (am != null) a = am.rounds;
            l.Add(new SD { uid = _qid++, id = it.id, cd = it.condition, sl = s, ps = ps, am = a });
            var g = it.GetComponent<GunScript>(); if (g != null && g.hasMag && g.roundsInMag > 0) l.Add(new SD { uid = _qid++, id = "_gm", sl = -1, ps = s, am = g.roundsInMag, gm = true });
        }
        var c = it.GetComponent<Container>(); if (c == null) return;
        foreach (Transform t in it.transform) if (t.TryGetComponent<Item>(out var ch)) SR(ch, l, -1, s);
    }
    static bool IsCustom(string id) { return CIds.Contains(id); }
    static readonly HashSet<string> CIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "etg_c","zagustin","cu_morphine","sj12","mule","propital","sj6","sj1","pnb","sj9","obdolbos","obdolbos2",
        "blueblood","xtg12","mildronate","2a2btg","ai2","goldenstar","vaseline","libatine","ibuprofen",
        "grizzlykit","afakkit","ifakkit","salewa","multitool","cms",
        "axmc","dvl10","sks","akm","deagle","glock17","m4a1","p90","mp133","ump45","rpd","mp153","usp",
        "axmc_mag","dvl10_mag","akm_mag","deagle_mag","glock17_mag","m4a1_mag","p90_mag","ump45_mag","rpd_mag","usp_mag",
        "338ucw","76251bpz","76239sp","12g85","50copper","45fmj","919pso","55645fmj","5728sb193","redrebel","m2sword",
    };
    public static void Postfix()
    {
        try
        {
            var items = Collect();
            var eff = Eff.Ser();
            var p = Application.persistentDataPath + "\\save.sv";
            if (!File.Exists(p)) return;
            var r = JObject.Parse(UZ(File.ReadAllBytes(p)));
            if (items != null && items.Count > 0) r["_customItems"] = JToken.FromObject(items); else r.Remove("_customItems");
            if (!string.IsNullOrEmpty(eff)) r["_customEffects"] = JToken.Parse(eff); else r.Remove("_customEffects");
            File.WriteAllBytes(p, Z(r.ToString(Formatting.None)));
            Plugin.Log.LogInfo($"[QoLSave] Embedded {items?.Count ?? 0} items.");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[QoLSave] Embed: {ex.Message}"); }
    }
    static string UZ(byte[] c) { using var s = new MemoryStream(c); using var g = new GZipStream(s, CompressionMode.Decompress); using var r = new StreamReader(g, Encoding.ASCII); return r.ReadToEnd(); }
    static byte[] Z(string d) { using var m = new MemoryStream(); using (var g = new GZipStream(m, System.IO.Compression.CompressionLevel.Optimal)) using (var w = new StreamWriter(g, Encoding.ASCII)) { w.Write(d); } return m.ToArray(); }
    [Serializable] public class SD { public int uid, sl, ps = -1, am = -1; public string id = ""; public float cd; public bool gm; }
}

public static class QoLSaveFix_Load
{
    static string _items = "", _eff = "";
    public static void Prefix()
    {
        _items = ""; _eff = "";
        var p = Application.persistentDataPath + "\\save.sv";
        if (!File.Exists(p)) return;
        try
        {
            var r = JObject.Parse(UZ(File.ReadAllBytes(p)));
            _items = r["_customItems"]?.ToString(Formatting.None) ?? "";
            _eff = r["_customEffects"]?.ToString(Formatting.None) ?? "";
            if (!string.IsNullOrEmpty(_items) || !string.IsNullOrEmpty(_eff)) { r.Remove("_customItems"); r.Remove("_customEffects"); File.WriteAllBytes(p, Z(r.ToString(Formatting.None))); }
            Plugin.Log.LogInfo($"[QoLSave] Extracted: items={!_items.StartsWith("")} eff={!_eff.StartsWith("")}");
        }
        catch { }
    }
    public static void Postfix()
    {
        if (string.IsNullOrEmpty(_items) && string.IsNullOrEmpty(_eff)) return;
        Body? b = null; try { b = PlayerCamera.main?.body; } catch { } if (b == null) return;
        if (!string.IsNullOrEmpty(_items))
        {
            try
            {
                var all = JsonConvert.DeserializeObject<List<QoLSaveFix_Save.SD>>(_items);
                if (all != null && all.Count > 0)
                {
                    int n = 0;
                    foreach (var s in all.FindAll(s => s.ps < 0 && !s.gm)) if (SpawnTop(b, s)) n++;
                    foreach (var s in all.FindAll(s => s.ps >= 0 || s.gm))
                    {
                        if (s.gm) { var g = b.GetItem(s.ps)?.GetComponent<GunScript>(); if (g != null) { g.hasMag = true; g.roundsInMag = s.am; n++; } continue; }
                        if (s.ps >= 0 && b.HoldingItem(s.ps)) { var p = b.GetItem(s.ps)?.GetComponent<Container>(); if (p != null) { var i = MakeItem(b, s); if (i != null) { p.LoadItem(i); n++; } } }
                    }
                    Plugin.Log.LogInfo($"[QoLSave] Restored {n}/{all.Count} items.");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[QoLSave] Items: {ex.Message}"); }
        }
        if (!string.IsNullOrEmpty(_eff)) { try { Eff.Res(_eff); } catch { } }
        _items = ""; _eff = "";
    }
    static bool SpawnTop(Body b, QoLSaveFix_Save.SD s) { var i = MakeItem(b, s); if (i == null) return false; if (s.sl >= 0) { if (b.HoldingItem(s.sl)) b.GetItem(s.sl).GetComponent<Container>().LoadItem(i); else b.PickUpItem(i, s.sl, true); } return true; }
    static Item? MakeItem(Body b, QoLSaveFix_Save.SD s)
    {
        var bid = BasePf(s.id); var pf = Resources.Load<GameObject>(bid); if (pf == null) return null;
        var go = UnityEngine.Object.Instantiate(pf, b.transform.position, Quaternion.identity);
        var it = go.GetComponent<Item>(); if (it == null) { UnityEngine.Object.Destroy(go); return null; }
        it.id = s.id;
        try { var m = typeof(ConsoleSpawnPatch).GetMethod("ConfigureCustomItem", BindingFlags.NonPublic | BindingFlags.Static); if (m != null) m.Invoke(null, new object[] { it, new MedicalGrantRequest(s.id, s.id, 1, "QoL") }); } catch { }
        it.condition = s.cd;
        if (s.am >= 0) { var a = it.GetComponent<AmmoScript>(); if (a != null) a.rounds = s.am; }
        return it;
    }
    static string BasePf(string id) { if (Prefabs.TryGetValue(id, out var x)) return x; if (id.Contains("mag")) return "riflemagazine"; if (id.Contains("762")||id.Contains("338")||id.Contains("12g")||id.Contains("50c")||id.Contains("45f")||id.Contains("919")||id.Contains("556")||id.Contains("5728")) return "556round"; return "syringe"; }
    static readonly Dictionary<string, string> Prefabs = new(StringComparer.OrdinalIgnoreCase) { ["axmc"]="rifle",["dvl10"]="rifle",["akm"]="rifle",["sks"]="rifle",["m4a1"]="rifle",["p90"]="rifle",["ump45"]="rifle",["rpd"]="rifle",["mp133"]="shotgun",["mp153"]="shotgun",["deagle"]="pistol",["glock17"]="pistol",["usp"]="pistol",["12g85"]="12gauge",["redrebel"]="bruisekit",["m2sword"]="bruisekit" };
    static string UZ(byte[] c) { using var s = new MemoryStream(c); using var g = new GZipStream(s, CompressionMode.Decompress); using var r = new StreamReader(g, Encoding.ASCII); return r.ReadToEnd(); }
    static byte[] Z(string d) { using var m = new MemoryStream(); using (var g = new GZipStream(m, System.IO.Compression.CompressionLevel.Optimal)) using (var w = new StreamWriter(g, Encoding.ASCII)) { w.Write(d); } return m.ToArray(); }
}

public static class Eff
{
    public static string Ser()
    {
        Body? b = null; try { b = PlayerCamera.main?.body; } catch { } if (b == null) return "";
        var l = new List<ESD>();
        foreach (var kv in Ctrl)
        {
            var c = b.gameObject.GetComponent(kv.Value); if (c == null || c is not MonoBehaviour mc) continue;
            if (!mc.enabled) continue;
            var d = new ESD { cn = kv.Value.FullName, r = RF(mc, "_remaining"), pt = RF(mc, "_phaseTimer"), br = RF(mc, "_buffRemaining"), dr = RF(mc, "_debuffRemaining"), el = RF(mc, "_elapsed"), sr = RF(mc, "_sideEffectRemaining"), it = RF(mc, "_initialTemp") };
            if (d.r <= 0f && d.pt <= 0f && d.br <= 0f) d.r = RF(mc, "_timer");
            var pf = kv.Value.GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance); if (pf != null) d.p = (int)(pf.GetValue(mc) ?? 0);
            if (d.r > 0f || d.pt > 0f || d.br > 0f) l.Add(d);
        }
        return l.Count == 0 ? "" : JsonConvert.SerializeObject(l);
    }
    public static void Res(string json)
    {
        var l = JsonConvert.DeserializeObject<List<ESD>>(json); if (l == null || l.Count == 0) return;
        Body? b = null; try { b = PlayerCamera.main?.body; } catch { } if (b == null) return; int n = 0;
        foreach (var d in l)
        {
            try
            {
                var t = Find(d.cn); if (t == null) continue;
                var mc = b.gameObject.AddComponent(t) as MonoBehaviour; if (mc == null) continue;
                WF(mc, "_remaining", d.r); WF(mc, "_timer", d.r); WF(mc, "_phaseTimer", d.pt); WF(mc, "_buffRemaining", d.br); WF(mc, "_debuffRemaining", d.dr); WF(mc, "_elapsed", d.el); WF(mc, "_sideEffectRemaining", d.sr); WF(mc, "_initialTemp", d.it);
                var pf = t.GetField("_phase", BindingFlags.NonPublic | BindingFlags.Instance); if (pf != null) pf.SetValue(mc, d.p);
                var bf = t.GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance); if (bf != null) bf.SetValue(mc, b);
                var af = t.GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance); if (af != null) af.SetValue(mc, true);
                if (t == typeof(MuleEffectController) && d.br > 0f) { var mf = typeof(MuleEffectController).GetField("ActiveInstance", BindingFlags.NonPublic | BindingFlags.Static); if (mf != null) mf.SetValue(null, mc); }
                mc.enabled = true; n++;
            }
            catch { }
        }
    }
    static float RF(MonoBehaviour mc, string n) { var f = mc.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); return f != null ? (float)(f.GetValue(mc) ?? 0f) : 0f; }
    static void WF(MonoBehaviour mc, string n, float v) { var f = mc.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance); if (f != null) f.SetValue(mc, v); }
    static Type? Find(string n) { foreach (var kv in Ctrl) if (kv.Value.FullName == n) return kv.Value; return null; }
    static readonly Dictionary<string, Type> Ctrl = new() { ["etg_c"]=typeof(EtgStimEffectController),["zagustin"]=typeof(ZagustinEffectController),["cu_morphine"]=typeof(MorphineEffectController),["sj12"]=typeof(SJ12EffectController),["mule"]=typeof(MuleEffectController),["propital"]=typeof(PropitalEffectController),["sj6"]=typeof(SJ6EffectController),["sj1"]=typeof(Sj1EffectController),["pnb"]=typeof(PnbEffectController),["sj9"]=typeof(Sj9EffectController),["blueblood"]=typeof(BluebloodEffectController),["xtg12"]=typeof(Xtg12EffectController),["mildronate"]=typeof(MildronateEffectController),["2a2btg"]=typeof(TwoATwoBTGEffectController), };
    [Serializable] class ESD { public string cn = ""; public float r, pt, br, dr, el, sr, it; public int p; }
}
