using System;
using System.Collections.Generic;
using System.Reflection;
using CUTarkovMedicalMod.Framework;
using UnityEngine;

namespace CUTarkovMedicalMod.Integration;

/// <summary>
/// 医疗物品的基础预制体类型。
/// </summary>
public enum BasePrefabType
{
    /// <summary>注射器类（基于 syringe/waterbottle，含 WaterContainerItem）</summary>
    Syringe,
    /// <summary>急救包类（基于 bruisekit/bandage，不含 WaterContainerItem）</summary>
    Bruisekit,
}

/// <summary>
/// 单个医疗物品的注册元数据。
/// </summary>
public sealed class MedicalItemDef
{
    public string ItemId { get; }
    public BasePrefabType BasePrefab { get; }
    public float Capacity { get; }
    public string? LiquidId { get; }
    public Type ItemSystemType { get; }
    public float WorldSizeMultiplier { get; }

    private Sprite? _cachedIcon;
    private bool _iconResolved;

    public MedicalItemDef(string itemId, BasePrefabType basePrefab, float capacity, string? liquidId, Type itemSystemType, float worldSizeMultiplier = 2.5f)
    {
        ItemId = itemId;
        BasePrefab = basePrefab;
        Capacity = capacity;
        LiquidId = liquidId;
        ItemSystemType = itemSystemType;
        WorldSizeMultiplier = worldSizeMultiplier;
    }

    /// <summary>
    /// 通过反射调用 ItemSystem 的 TryLoadIcon 方法获取物品图标。
    /// 结果会被缓存。
    /// </summary>
    public Sprite? ResolveIcon()
    {
        if (_iconResolved) return _cachedIcon;
        _iconResolved = true;
        try
        {
            var method = ItemSystemType.GetMethod("TryLoadIcon",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            _cachedIcon = method?.Invoke(null, null) as Sprite;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[MedicalItemDef] Failed to resolve icon for '{ItemId}': {ex.Message}");
        }
        return _cachedIcon;
    }
}

/// <summary>
/// 所有医疗物品的中央定义注册表。
/// </summary>
public static class MedicalItemDefinitions
{
    private static readonly List<MedicalItemDef> _all = new();
    private static readonly Dictionary<string, MedicalItemDef> _byId = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<MedicalItemDef> All => _all;
    public static IReadOnlyDictionary<string, MedicalItemDef> ById => _byId;

    static MedicalItemDefinitions()
    {
        // === 注射器兴奋剂（16个，capacity=0，无液体）===
        // capacity=0: CUCoreLib ApplyCustomItemComponents 不会重算 condition（def.capacity > 0f 为 false）。
        // CUCoreLibMode 中通过 defaultContents=[{water,0}] 使 ChooseTemplateId 返回 "waterbottle"（正确模板），
        // 避免 capacity=0 导致返回 "bandage" 的 IndexOutOfRangeException。
        Add("etg_c", BasePrefabType.Syringe, 0, null, typeof(EtgCItemSystem));
        Add("zagustin", BasePrefabType.Syringe, 0, null, typeof(ZagustinItemSystem));
        Add("cu_morphine", BasePrefabType.Syringe, 0, null, typeof(MorphineItemSystem));
        Add("sj12", BasePrefabType.Syringe, 0, null, typeof(SJ12ItemSystem));
        Add("mule", BasePrefabType.Syringe, 0, null, typeof(MuleItemSystem));
        Add("propital", BasePrefabType.Syringe, 0, null, typeof(PropitalItemSystem));
        Add("sj6", BasePrefabType.Syringe, 0, null, typeof(SJ6ItemSystem));
        Add("sj1", BasePrefabType.Syringe, 0, null, typeof(Sj1ItemSystem));
        Add("pnb", BasePrefabType.Syringe, 0, null, typeof(PnbItemSystem));
        Add("obdolbos", BasePrefabType.Syringe, 0, null, typeof(ObdolbosItemSystem));
        Add("sj9", BasePrefabType.Syringe, 0, null, typeof(Sj9ItemSystem));
        Add("blueblood", BasePrefabType.Syringe, 0, null, typeof(BluebloodItemSystem));
        Add("xtg12", BasePrefabType.Syringe, 0, null, typeof(Xtg12ItemSystem));
        Add("mildronate", BasePrefabType.Syringe, 0, null, typeof(MildronateItemSystem));
        Add("2a2btg", BasePrefabType.Syringe, 0, null, typeof(TwoATwoBTGItemSystem));
        Add("obdolbos2", BasePrefabType.Syringe, 0, null, typeof(Obdolbos2ItemSystem));

        // === 多剂量注射器（1个，capacity=100，含液体）===
        Add("ai2", BasePrefabType.Syringe, 100, AI2ItemSystem.LiquidId, typeof(AI2ItemSystem));

        // === 液体药膏（4个，含液体）===
        Add("goldenstar", BasePrefabType.Bruisekit, 10, GoldenStarItemSystem.LiquidId, typeof(GoldenStarItemSystem));
        Add("vaseline", BasePrefabType.Bruisekit, 10, VaselineItemSystem.LiquidId, typeof(VaselineItemSystem));
        Add("ibuprofen", BasePrefabType.Bruisekit, 10, IbuprofenItemSystem.LiquidId, typeof(IbuprofenItemSystem));
        Add("libatine", BasePrefabType.Bruisekit, 2, LibatineItemSystem.LiquidId, typeof(LibatineItemSystem));

        // === 绷带急救包（4个，capacity=0，无液体）===
        // capacity=0 使 ChooseTemplateId 返回 "bandage"（与 bruisekit 行为一致）
        Add("grizzlykit", BasePrefabType.Bruisekit, 0, null, typeof(GrizzlyKitItemSystem), 3.25f);
        Add("afak", BasePrefabType.Bruisekit, 0, null, typeof(AfakKitItemSystem));
        Add("ifak", BasePrefabType.Bruisekit, 0, null, typeof(IfakKitItemSystem));
        Add("salewa", BasePrefabType.Bruisekit, 0, null, typeof(SalewaKitItemSystem), 1.25f);

        // === 手术工具（2个，capacity=0，无液体）===
        Add("multitool", BasePrefabType.Bruisekit, 0, null, typeof(MultiToolItemSystem));
        Add("cms", BasePrefabType.Bruisekit, 0, null, typeof(CmsKitItemSystem));
    }

    private static void Add(string itemId, BasePrefabType basePrefab, float capacity, string? liquidId, Type itemSystemType, float worldSizeMultiplier = 2.5f)
    {
        var def = new MedicalItemDef(itemId, basePrefab, capacity, liquidId, itemSystemType, worldSizeMultiplier);
        _all.Add(def);
        _byId[itemId] = def;
        // 注册到 i18n 注册表，语言切换时刷新 fullName/description
        ItemI18nRegistry.Register(itemId);
    }
}
