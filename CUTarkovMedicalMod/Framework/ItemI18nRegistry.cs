using System;
using System.Collections.Generic;
using System.Reflection;
using CUCoreLib.Registries;

namespace CUTarkovMedicalMod.Framework;

/// <summary>
/// 中央物品 i18n 注册表。
/// 跟踪所有模组物品 ID，在语言切换时刷新 ItemInfo.fullName/description。
/// 物品注册时 fullName/description 被一次性写入（当时语言），切换语言后不会自动更新。
/// 此类通过 I18n.EnsureLoaded 检测到语言变化时调用 RefreshAll() 修复此问题。
/// </summary>
public static class ItemI18nRegistry
{
    // 物品 ID → (nameKey, descKey)
    private static readonly Dictionary<string, string> _nameKeys = new();
    private static readonly Dictionary<string, string> _descKeys = new();

    // 缓存的 ItemInfo 引用（注册时捕获）
    private static readonly Dictionary<string, object> _itemInfos = new();

    // 反射缓存：ItemRegistry.RegisteredItems（internal static）
    private static FieldInfo? _registeredItemsField;
    private static bool _registeredItemsFieldChecked;

    /// <summary>
    /// 注册一个模组物品的 i18n 键。应在物品注册后调用。
    /// </summary>
    public static void Register(string itemId, string nameKey, string descKey)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        _nameKeys[itemId] = nameKey;
        _descKeys[itemId] = descKey;
    }

    /// <summary>
    /// 便捷方法：键模式为 "{itemId}.name" / "{itemId}.desc"
    /// </summary>
    public static void Register(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        _nameKeys[itemId] = $"{itemId}.name";
        _descKeys[itemId] = $"{itemId}.desc";
    }

    /// <summary>
    /// 注册时捕获 ItemInfo 引用，避免后续反射查找。
    /// </summary>
    public static void CaptureItemInfo(string itemId, object itemInfo)
    {
        if (!string.IsNullOrEmpty(itemId) && itemInfo != null)
            _itemInfos[itemId] = itemInfo;
    }

    /// <summary>
    /// 语言切换后刷新所有已注册物品的 fullName/description。
    /// </summary>
    public static void RefreshAll()
    {
        if (_nameKeys.Count == 0) return;

        // 1. 刷新已捕获的 ItemInfo 引用
        foreach (var kv in _nameKeys)
        {
            var itemId = kv.Key;
            var nameKey = kv.Value;
            var descKey = _descKeys[itemId];

            if (_itemInfos.TryGetValue(itemId, out var infoObj))
            {
                UpdateItemInfo(infoObj, nameKey, descKey);
            }
        }

        // 2. 刷新 Item.GlobalItems（游戏原生字典，public）
        try
        {
            var globalItems = Item.GlobalItems;
            if (globalItems != null)
            {
                foreach (var kv in _nameKeys)
                {
                    var itemId = kv.Key;
                    var nameKey = kv.Value;
                    var descKey = _descKeys[itemId];
                    if (globalItems.TryGetValue(itemId, out var info) && info != null)
                    {
                        info.fullName = I18n.Tr(nameKey);
                        info.description = I18n.Tr(descKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[ItemI18nRegistry] Failed to refresh Item.GlobalItems: {ex.Message}");
        }

        // 3. 刷新 ItemRegistry.RegisteredItems（internal，用反射）
        try
        {
            var registeredItems = GetRegisteredItems();
            if (registeredItems != null)
            {
                foreach (var kv in _nameKeys)
                {
                    var itemId = kv.Key;
                    var nameKey = kv.Value;
                    var descKey = _descKeys[itemId];
                    if (registeredItems.TryGetValue(itemId, out var infoObj) && infoObj != null)
                    {
                        UpdateItemInfo(infoObj, nameKey, descKey);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log?.LogWarning($"[ItemI18nRegistry] Failed to refresh ItemRegistry.RegisteredItems: {ex.Message}");
        }
    }

    private static void UpdateItemInfo(object infoObj, string nameKey, string descKey)
    {
        try
        {
            var info = (ItemInfo)infoObj;
            info.fullName = I18n.Tr(nameKey);
            info.description = I18n.Tr(descKey);
        }
        catch { }
    }

    private static Dictionary<string, object>? GetRegisteredItems()
    {
        if (!_registeredItemsFieldChecked)
        {
            _registeredItemsFieldChecked = true;
            _registeredItemsField = typeof(ItemRegistry).GetField("RegisteredItems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        }
        return _registeredItemsField?.GetValue(null) as Dictionary<string, object>;
    }
}
