using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// QoL 兼容存档修复（无 CUCoreLib 时生效）。
///
/// 反编译确认的游戏存档加载流程 (SaveSystem.TryLoadGame):
///   line 295: gameObject = Object.Instantiate(Resources.Load(savedItem.id), pos, rot) as GameObject;
///   自定义物品 ID 不在 Resources 中 -> Resources.Load 返回 null -> Instantiate(null) -> NRE
///   游戏捕获异常并显示 "Error occured during creating item \"propital\"."
///
/// 修复方案（双重保障）：
/// 1. Prefix: 遍历 save.sv 的 items 数组，将自定义物品 ID 替换为基础预制体 ID
///    这样 Resources.Load("syringe") 成功，游戏创建出 syringe 物品
/// 2. Transpiler: 拦截 Resources.Load + Object.Instantiate（镜像 CUCoreLib）
/// 3. Postfix: 遍历玩家槽位/穿戴栏，找到基础预制体物品，用 ConfigureCustomItem 转换为自定义物品
///    同时恢复 condition 和弹药
/// </summary>
public static class QoLSaveFix
{
    private static bool? _qolCached;

    /// <summary>
    /// 多层检测 QoL Unknown mod 是否已加载并激活。
    /// 
    /// 问题：Chainloader.PluginInfos.ContainsKey 可能在以下情况误判：
    ///   - QoL 自身 ShouldBlockQoLLoads 逻辑阻止加载（Instance 为 null 但 key 存在）
    ///   - QoL Awake 异常导致未完全初始化
    ///   - 加载时序边缘情况
    /// 
    /// 解决方案（三层 fallback）：
    ///   1. Chainloader.PluginInfos + Instance != null（最准确：插件确实加载并实例化）
    ///   2. AppDomain 中存在 "QoL Unknown" 程序集（程序集已加载说明 BepInEx 至少完成了加载阶段）
    ///   3. 所有已加载程序集中存在已知 QoL 类型（最终兜底）
    /// </summary>
    public static bool HasQoL()
    {
        if (_qolCached.HasValue) return _qolCached.Value;

        _qolCached = DetectQoL();
        Plugin.Log.LogInfo($"[QoLSaveFix] HasQoL detection result: {_qolCached.Value}");
        return _qolCached.Value;
    }

    private static bool DetectQoL()
    {
        const string qolGuid = "org.bepinex.plugins.qol_unknown";

        // Layer 1: BepInEx Chainloader - 不仅检查 key 存在，还验证 Instance 已实例化
        try
        {
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(qolGuid, out var info)
                && info?.Instance != null)
                return true;
        }
        catch { }

        // Layer 2: AppDomain 中查找 QoL 程序集
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == "QoL Unknown")
                    return true;
            }
        }
        catch { }

        // Layer 3: 在所有已加载程序集中搜索已知的 QoL 类型
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (name != "QoL Unknown" && !name.StartsWith("QoL"))
                    continue;
                // SaveSystemPatcher / QoLModuleManager 是 QoL 的核心类
                if (asm.GetType("SaveSystemPatcher") != null
                    || asm.GetType("QoLModuleManager") != null
                    || asm.GetType("QoL_Unknown.SaveSystemPatcher") != null
                    || asm.GetType("QoL_Unknown.QoLModuleManager") != null)
                    return true;
            }
        }
        catch { }

        return false;
    }

    public static void Register(Harmony harmony)
    {
        var mSave = typeof(SaveSystem).GetMethod("SaveGame");
        var mLoad = typeof(SaveSystem).GetMethod("TryLoadGame");

        harmony.Patch(mSave, postfix: new HarmonyMethod(typeof(QoLSaveFix_Save), "Postfix"));

        // Critical: our Prefix must run AFTER QoL's Prefix.
        // QoL's SaveSystemPatcher.TryLoadGame_Prefix copies the named save file over save.sv.
        // If our Prefix runs first, QoL overwrites our modifications.
        var loadPrefix = new HarmonyMethod(typeof(QoLSaveFix_Load), "Prefix")
        {
            after = new[] { "org.bepinex.plugins.qol.savesystem" }
        };

        harmony.Patch(mLoad,
            prefix: loadPrefix,
            postfix: new HarmonyMethod(typeof(QoLSaveFix_Load), "Postfix"),
            transpiler: new HarmonyMethod(typeof(QoLSaveFix_Transpiler), "Transpiler"));

        Plugin.Log.LogInfo("[QoLSaveFix] Registered Save/Load + transpiler patches (Prefix after QoL).");
    }
}

/// <summary>
/// 自定义物品 ID 集合和基础预制体映射。
/// </summary>
public static class QoLSaveFix_ItemMap
{
    internal static readonly HashSet<string> CustomIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "etg_c","zagustin","cu_morphine","sj12","mule","propital","sj6","sj1","pnb","sj9","obdolbos","obdolbos2",
        "blueblood","xtg12","mildronate","2a2btg","ai2","goldenstar","vaseline","libatine","ibuprofen",
        "grizzlykit","afakkit","ifakkit","salewa","multitool","cms",
        "axmc","dvl10","sks","akm","deagle","glock17","m4a1","p90","mp133","ump45","rpd","mp153","usp",
        "axmc_mag","dvl10_mag","akm_mag","deagle_mag","glock17_mag","m4a1_mag","p90_mag","ump45_mag","rpd_mag","usp_mag",
        "338ucw","76251bpz","76239sp","12g85","50copper","45fmj","919pso","55645fmj","5728sb193","redrebel","m2sword",
    };

    static readonly Dictionary<string, string> PrefabMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["axmc"]="rifle",["dvl10"]="rifle",["akm"]="rifle",["sks"]="rifle",["m4a1"]="rifle",
        ["p90"]="rifle",["ump45"]="rifle",["rpd"]="rifle",["mp133"]="shotgun",["mp153"]="shotgun",
        ["deagle"]="pistol",["glock17"]="pistol",["usp"]="pistol",
        ["12g85"]="12gauge",["redrebel"]="bruisekit",["m2sword"]="bruisekit",
    };

    public static string GetBasePrefab(string id)
    {
        if (PrefabMap.TryGetValue(id, out var x)) return x;
        if (id.Contains("mag")) return "riflemagazine";
        if (id.Contains("762")||id.Contains("338")||id.Contains("12g")||id.Contains("50c")||id.Contains("45f")||id.Contains("919")||id.Contains("556")||id.Contains("5728")) return "556round";
        return "syringe";
    }

    public static bool IsCustom(string id) => CustomIds.Contains(id);
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
        if (QoLSaveFix_ItemMap.IsCustom(it.id))
        {
            int a = -1; var am = it.GetComponent<AmmoScript>(); if (am != null) a = am.rounds;
            l.Add(new SD { uid = _qid++, id = it.id, cd = it.condition, sl = s, ps = ps, am = a });
            var g = it.GetComponent<GunScript>(); if (g != null && g.hasMag && g.roundsInMag > 0) l.Add(new SD { uid = _qid++, id = "_gm", sl = -1, ps = s, am = g.roundsInMag, gm = true });
        }
        var c = it.GetComponent<Container>(); if (c == null) return;
        foreach (Transform t in it.transform) if (t.TryGetComponent<Item>(out var ch)) SR(ch, l, -1, s);
    }

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
    // Map: original custom ID -> base prefab ID (set in Prefix, used in Postfix)
    static Dictionary<int, string> _slotCustomIds = new();  // slot index -> custom ID
    static Dictionary<string, string> _wearCustomIds = new(); // wearSlot -> custom ID
    static string _items = "", _eff = "";

    public static void Prefix()
    {
        Plugin.Log.LogInfo($"[QoLSave] Prefix called. loadedRun={SaveSystem.loadedRun}");
        _items = ""; _eff = "";
        _slotCustomIds.Clear();
        _wearCustomIds.Clear();
        if (!SaveSystem.loadedRun) return;
        var p = Application.persistentDataPath + "\\save.sv";
        if (!File.Exists(p)) { Plugin.Log.LogInfo("[QoLSave] Prefix: save.sv not found."); return; }
        try
        {
            var r = JObject.Parse(UZ(File.ReadAllBytes(p)));
            _items = r["_customItems"]?.ToString(Formatting.None) ?? "";
            _eff = r["_customEffects"]?.ToString(Formatting.None) ?? "";
            r.Remove("_customItems");
            r.Remove("_customEffects");

            // The save JSON has an "items" array of SavedItem objects with fields:
            //   id, condition, slot, wearSlot, favourited
            // Game's TryLoadGame line 295: Resources.Load(savedItem.id)
            // Replace custom item IDs with base prefab IDs so Resources.Load succeeds.
            var itemsToken = r["items"];
            Plugin.Log.LogInfo($"[QoLSave] Prefix: items token is {(itemsToken == null ? "null" : itemsToken.Type.ToString())}, count={((itemsToken as JArray)?.Count ?? -1)}");
            if (itemsToken is JArray itemsArr)
            {
                for (int i = 0; i < itemsArr.Count; i++)
                {
                    var itemObj = itemsArr[i] as JObject;
                    if (itemObj == null) continue;
                    var idTok = itemObj["id"];
                    if (idTok == null || idTok.Type != JTokenType.String) continue;
                    var id = idTok.Value<string>() ?? "";
                    if (id.Length > 0 && QoLSaveFix_ItemMap.IsCustom(id))
                    {
                        var baseId = QoLSaveFix_ItemMap.GetBasePrefab(id);
                        itemObj["id"] = baseId;

                        // Record mapping for Postfix restoration
                        var slotTok = itemObj["slot"];
                        var wearTok = itemObj["wearSlot"];
                        int slotVal = slotTok?.Value<int>() ?? -1;
                        string? wearVal = wearTok?.Value<string>();

                        if (slotVal >= 0)
                            _slotCustomIds[slotVal] = id;
                        else if (!string.IsNullOrEmpty(wearVal))
                            _wearCustomIds[wearVal!] = id;

                        Plugin.Log.LogInfo($"[QoLSave] Prefix: items[{i}] id '{id}' -> '{baseId}' (slot={slotVal} wear={wearVal})");
                    }
                }
            }

            File.WriteAllBytes(p, Z(r.ToString(Formatting.None)));
            Plugin.Log.LogInfo($"[QoLSave] Prefix: items={!string.IsNullOrEmpty(_items)} eff={!string.IsNullOrEmpty(_eff)} customSlots={_slotCustomIds.Count} customWears={_wearCustomIds.Count}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[QoLSave] Prefix: {ex.Message}"); }
    }

    public static void Postfix()
    {
        // Phase 1: Convert base prefab items loaded by game back to custom items
        if (_slotCustomIds.Count > 0 || _wearCustomIds.Count > 0)
        {
            Body? b = null; try { b = PlayerCamera.main?.body; } catch { }
            if (b != null)
            {
                int converted = 0;

                // Convert slot items
                foreach (var kv in _slotCustomIds)
                {
                    int slot = kv.Key;
                    string customId = kv.Value;
                    try
                    {
                        if (b.HoldingItem(slot))
                        {
                            var item = b.GetItem(slot);
                            if (item != null)
                            {
                                var baseId = QoLSaveFix_ItemMap.GetBasePrefab(customId);
                                if (string.Equals(item.id, baseId, StringComparison.OrdinalIgnoreCase))
                                {
                                    ConvertToCustom(item, customId);
                                    converted++;
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[QoLSave] Convert slot {slot}: {ex.Message}"); }
                }

                // Convert wearable items
                foreach (var kv in _wearCustomIds)
                {
                    string wearSlot = kv.Key;
                    string customId = kv.Value;
                    try
                    {
                        var wearable = b.GetWearableBySlotID(wearSlot);
                        if (wearable != null)
                        {
                            var baseId = QoLSaveFix_ItemMap.GetBasePrefab(customId);
                            if (string.Equals(wearable.id, baseId, StringComparison.OrdinalIgnoreCase))
                            {
                                ConvertToCustom(wearable, customId);
                                converted++;
                            }
                        }
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[QoLSave] Convert wear {wearSlot}: {ex.Message}"); }
                }

                Plugin.Log.LogInfo($"[QoLSave] Converted {converted} items to custom.");
            }
        }

        // Phase 2: Restore condition and ammo from _customItems data
        if (!string.IsNullOrEmpty(_items))
        {
            Body? b = null; try { b = PlayerCamera.main?.body; } catch { }
            if (b != null)
            {
                try
                {
                    var all = JsonConvert.DeserializeObject<List<QoLSaveFix_Save.SD>>(_items);
                    if (all != null && all.Count > 0)
                    {
                        int n = 0;
                        foreach (var s in all)
                        {
                            if (s.gm)
                            {
                                var g = b.GetItem(s.ps)?.GetComponent<GunScript>();
                                if (g != null) { g.hasMag = true; g.roundsInMag = s.am; n++; }
                                continue;
                            }

                            // Find the item by custom ID in slots or containers
                            Item? found = FindItemById(b, s.id);
                            if (found != null)
                            {
                                found.condition = s.cd;
                                if (s.am >= 0) { var a = found.GetComponent<AmmoScript>(); if (a != null) a.rounds = s.am; }
                                n++;
                            }
                        }
                        Plugin.Log.LogInfo($"[QoLSave] Restored state for {n}/{all.Count} items.");
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[QoLSave] Items restore: {ex.Message}"); }
            }
        }

        // Phase 3: Restore effects
        if (!string.IsNullOrEmpty(_eff)) { try { Eff.Res(_eff); } catch { } Plugin.Log.LogInfo("[QoLSave] Effects restored."); }

        _items = ""; _eff = "";
        _slotCustomIds.Clear();
        _wearCustomIds.Clear();
    }

    static void ConvertToCustom(Item item, string customId)
    {
        item.id = customId;
        try { var m = typeof(ConsoleSpawnPatch).GetMethod("ConfigureCustomItem", BindingFlags.NonPublic | BindingFlags.Static); if (m != null) m.Invoke(null, new object[] { item, new MedicalGrantRequest(customId, customId, 1, "QoL") }); } catch { }
    }

    static Item? FindItemById(Body b, string id)
    {
        // Search slots
        if (b.slots != null)
            for (int i = 0; i < b.slots.Length; i++)
                if (b.HoldingItem(i))
                {
                    var item = b.GetItem(i);
                    var found = SearchItem(item, id);
                    if (found != null) return found;
                }
        // Search wearables
        if (b.GetAllWearables() != null)
            foreach (var w in b.GetAllWearables())
                if (w != null)
                {
                    var found = SearchItem(w, id);
                    if (found != null) return found;
                }
        return null;
    }

    static Item? SearchItem(Item? item, string id)
    {
        if (item == null) return null;
        if (string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase)) return item;
        var c = item.GetComponent<Container>();
        if (c == null) return null;
        foreach (Transform t in c.transform)
            if (t.TryGetComponent<Item>(out var ch))
            {
                var found = SearchItem(ch, id);
                if (found != null) return found;
            }
        return null;
    }

    static string UZ(byte[] c) { using var s = new MemoryStream(c); using var g = new GZipStream(s, CompressionMode.Decompress); using var r = new StreamReader(g, Encoding.ASCII); return r.ReadToEnd(); }
    static byte[] Z(string d) { using var m = new MemoryStream(); using (var g = new GZipStream(m, System.IO.Compression.CompressionLevel.Optimal)) using (var w = new StreamWriter(g, Encoding.ASCII)) { w.Write(d); } return m.ToArray(); }
}

/// <summary>
/// Transpiler - 镜像 CUCoreLib CustomItemSerializationPatches。
/// 拦截 TryLoadGame 中的 Resources.Load 和 Object.Instantiate 调用。
/// 作为安全网：即使 Prefix 的 ID 替换有遗漏，transpiler 也能拦截 Resources.Load。
/// </summary>
public static class QoLSaveFix_Transpiler
{
    static readonly MethodInfo ResourcesLoadMethod = typeof(Resources)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(Resources.Load)
                    && !m.IsGenericMethod
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(string));

    static readonly MethodInfo LoadSavedItemResourceMethod =
        AccessTools.Method(typeof(QoLSaveFix_Transpiler), nameof(LoadSavedItemResource));

    static readonly MethodInfo ObjectInstantiateMethod = typeof(UnityEngine.Object)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m => m.Name == nameof(UnityEngine.Object.Instantiate)
                    && !m.IsGenericMethod
                    && m.GetParameters().Length == 3
                    && m.GetParameters()[0].ParameterType == typeof(UnityEngine.Object)
                    && m.GetParameters()[1].ParameterType == typeof(Vector3)
                    && m.GetParameters()[2].ParameterType == typeof(Quaternion));

    static readonly MethodInfo InstantiateSavedItemMethod =
        AccessTools.Method(typeof(QoLSaveFix_Transpiler), nameof(InstantiateSavedItem));

    public static UnityEngine.Object LoadSavedItemResource(string id)
    {
        var vanilla = Resources.Load(id);
        if (vanilla != null) return vanilla;

        // Custom item ID - fall back to base prefab
        if (!string.IsNullOrEmpty(id) && QoLSaveFix_ItemMap.IsCustom(id))
        {
            var baseId = QoLSaveFix_ItemMap.GetBasePrefab(id);
            var fallback = Resources.Load(baseId);
            Plugin.Log.LogInfo($"[QoLSave] Transpiler Resources.Load fallback: '{id}' -> '{baseId}' (ok={fallback != null})");
            return fallback!;
        }

        return null!;
    }

    public static UnityEngine.Object InstantiateSavedItem(UnityEngine.Object original, Vector3 position, Quaternion rotation)
    {
        if (original == null) return null!;
        var clone = UnityEngine.Object.Instantiate(original, position, rotation);
        if (clone is GameObject go) go.SetActive(true);
        return clone;
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var inst in instructions)
        {
            if (inst.Calls(ResourcesLoadMethod))
            {
                yield return new CodeInstruction(OpCodes.Call, LoadSavedItemResourceMethod);
                continue;
            }
            if (inst.Calls(ObjectInstantiateMethod))
            {
                yield return new CodeInstruction(OpCodes.Call, InstantiateSavedItemMethod);
                continue;
            }
            yield return inst;
        }
    }
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
