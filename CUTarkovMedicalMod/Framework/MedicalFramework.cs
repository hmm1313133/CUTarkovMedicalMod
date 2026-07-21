using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace CUTarkovMedicalMod.Framework;

public enum MedicalFeatureMode
{
    Disabled = 0,
    StartingLoadoutOnly = 1,
    WorldLootOnly = 2,
    Both = 3
}

public enum MultiplayerCompatibilityMode
{
    AutoSafe = 0,
    PreferStartingLoadoutOnly = 1,
    ForceConfiguredMode = 2
}

public enum MedicalItemCategory
{
    Bandage,
    Splint,
    Painkiller,
    Hemostatic,
    Surgery,
    Support,
    Stim,
    Saline
}

public sealed class MedicalItemDefinition
{
    public MedicalItemDefinition(
        string key,
        string displayName,
        MedicalItemCategory category,
        int weight,
        int minCount,
        int maxCount,
        string? gameItemId = null,
        bool enabled = true)
    {
        Key = key;
        DisplayName = displayName;
        Category = category;
        Weight = weight;
        MinCount = minCount;
        MaxCount = maxCount;
        GameItemId = gameItemId;
        Enabled = enabled;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public MedicalItemCategory Category { get; }
    public int Weight { get; }
    public int MinCount { get; }
    public int MaxCount { get; }
    public string? GameItemId { get; }
    public bool Enabled { get; }

    public override string ToString()
    {
        var gameId = string.IsNullOrWhiteSpace(GameItemId) ? string.Empty : $", GameItemId={GameItemId}";
        return $"{DisplayName} ({Key}) x{MinCount}-{MaxCount}, Cat={Category}, W={Weight}{gameId}";
    }
}

public sealed class MedicalGrantRequest
{
    public MedicalGrantRequest(string itemKey, string displayName, int count, string source, string? gameItemId = null)
    {
        ItemKey = itemKey;
        DisplayName = displayName;
        Count = count;
        Source = source;
        GameItemId = gameItemId;
    }

    public string ItemKey { get; }
    public string DisplayName { get; }
    public int Count { get; }
    public string Source { get; }
    public string? GameItemId { get; }

    public string SpawnItemId => string.IsNullOrWhiteSpace(GameItemId) ? ItemKey : GameItemId!;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(GameItemId)
            ? $"{DisplayName} x{Count} [{Source}]"
            : $"{DisplayName} x{Count} [{Source}]#{GameItemId}";
    }
}

public sealed class MedicalPlan
{
    public MedicalPlan(
        MedicalFeatureMode effectiveMode,
        string contentSource,
        IReadOnlyList<MedicalGrantRequest> startingLoadout,
        IReadOnlyList<MedicalGrantRequest> worldLoot)
    {
        EffectiveMode = effectiveMode;
        ContentSource = contentSource;
        StartingLoadout = startingLoadout;
        WorldLoot = worldLoot;
    }

    public MedicalFeatureMode EffectiveMode { get; }
    public string ContentSource { get; }
    public IReadOnlyList<MedicalGrantRequest> StartingLoadout { get; }
    public IReadOnlyList<MedicalGrantRequest> WorldLoot { get; }
}

public static class MedicalCatalog
{
    public static readonly IReadOnlyList<MedicalItemDefinition> DefaultItems = new[]
    {
        new MedicalItemDefinition("etg_c", I18n.Tr("etg_c.name"), MedicalItemCategory.Stim, 6, 1, 1, "syringe"),
        new MedicalItemDefinition("zagustin", I18n.Tr("zagustin.name"), MedicalItemCategory.Hemostatic, 8, 1, 1, "syringe"),
        new MedicalItemDefinition("cu_morphine", I18n.Tr("cu_morphine.name"), MedicalItemCategory.Painkiller, 10, 1, 1, "syringe"),
        new MedicalItemDefinition("sj12", I18n.Tr("sj12.name"), MedicalItemCategory.Stim, 7, 1, 1, "syringe"),
        new MedicalItemDefinition("mule", I18n.Tr("mule.name"), MedicalItemCategory.Stim, 6, 1, 1, "syringe"),
        new MedicalItemDefinition("propital", I18n.Tr("propital.name"), MedicalItemCategory.Stim, 7, 1, 1, "syringe"),
        new MedicalItemDefinition("sj1", I18n.Tr("sj1.name"), MedicalItemCategory.Stim, 6, 1, 1, "syringe"),
        new MedicalItemDefinition("sj6", I18n.Tr("sj6.name"), MedicalItemCategory.Stim, 6, 1, 1, "syringe"),
        new MedicalItemDefinition("pnb", I18n.Tr("pnb.name"), MedicalItemCategory.Stim, 7, 1, 1, "syringe"),
        new MedicalItemDefinition("obdolbos", I18n.Tr("obdolbos.name"), MedicalItemCategory.Stim, 5, 1, 1, "syringe"),
        new MedicalItemDefinition("sj9", I18n.Tr("sj9.name"), MedicalItemCategory.Stim, 6, 1, 1, "syringe"),
        new MedicalItemDefinition("blueblood", I18n.Tr("blueblood.name"), MedicalItemCategory.Stim, 7, 1, 1, "syringe"),
        new MedicalItemDefinition("xtg12", I18n.Tr("xtg12.name"), MedicalItemCategory.Stim, 6, 1, 1, "syringe"),
        new MedicalItemDefinition("mildronate", I18n.Tr("mildronate.name"), MedicalItemCategory.Stim, 6, 1, 1, "syringe"),
        new MedicalItemDefinition("2a2btg", I18n.Tr("2a2btg.name"), MedicalItemCategory.Stim, 6, 1, 1, "syringe"),
        new MedicalItemDefinition("obdolbos2", I18n.Tr("obdolbos2.name"), MedicalItemCategory.Stim, 5, 1, 1, "syringe"),
        new MedicalItemDefinition("ai2", I18n.Tr("ai2.name"), MedicalItemCategory.Support, 7, 1, 1, "syringe"),
        new MedicalItemDefinition("grizzlykit", I18n.Tr("grizzlykit.name"), MedicalItemCategory.Support, 5, 1, 1, "bruisekit"),
        new MedicalItemDefinition("afak", I18n.Tr("afak.name"), MedicalItemCategory.Support, 5, 1, 1, "bruisekit"),
        new MedicalItemDefinition("ifak", I18n.Tr("ifak.name"), MedicalItemCategory.Support, 5, 1, 1, "bruisekit"),
        new MedicalItemDefinition("salewa", I18n.Tr("salewa.name"), MedicalItemCategory.Support, 5, 1, 1, "bruisekit"),
        new MedicalItemDefinition("goldenstar", I18n.Tr("goldenstar.name"), MedicalItemCategory.Support, 8, 1, 1, "bruisekit"),
        new MedicalItemDefinition("vaseline", I18n.Tr("vaseline.name"), MedicalItemCategory.Support, 10, 1, 1, "bruisekit"),
        new MedicalItemDefinition("libatine", I18n.Tr("libatine.name"), MedicalItemCategory.Support, 7, 1, 1, "bruisekit"),
        new MedicalItemDefinition("ibuprofen", I18n.Tr("ibuprofen.name"), MedicalItemCategory.Support, 8, 1, 1, "bruisekit"),
        new MedicalItemDefinition("multitool", I18n.Tr("multitool.name"), MedicalItemCategory.Support, 5, 1, 1, "bruisekit"),
        new MedicalItemDefinition("cms", I18n.Tr("cms.name"), MedicalItemCategory.Support, 6, 1, 1, "bruisekit"),
        new MedicalItemDefinition("adhesivebandage", "Adhesive Bandage", MedicalItemCategory.Bandage, 28, 1, 2, "adhesivebandage"),
        new MedicalItemDefinition("bandage", "Bandage", MedicalItemCategory.Bandage, 35, 1, 2, "bandage"),
        new MedicalItemDefinition("sterilizedbandage", "Sterilized Bandage", MedicalItemCategory.Bandage, 24, 1, 2, "sterilizedbandage"),
        new MedicalItemDefinition("splint", "Splint", MedicalItemCategory.Splint, 30, 1, 1, "splint"),
        new MedicalItemDefinition("painkillers", "Painkillers", MedicalItemCategory.Painkiller, 25, 1, 1, "painkillers"),
        new MedicalItemDefinition("tourniquet", "Tourniquet", MedicalItemCategory.Hemostatic, 18, 1, 1, "tourniquet"),
        new MedicalItemDefinition("procoagulant", "Procoagulant", MedicalItemCategory.Hemostatic, 10, 1, 1, "procoagulant"),
        new MedicalItemDefinition("medkit", "Medkit", MedicalItemCategory.Support, 20, 1, 1, "medkit"),
        new MedicalItemDefinition("bruisekit", "Bruise Kit", MedicalItemCategory.Support, 14, 1, 1, "bruisekit"),
        new MedicalItemDefinition("antibiotics", "Antibiotics", MedicalItemCategory.Support, 9, 1, 1, "antibiotics"),
        new MedicalItemDefinition("ringersolution", "Ringer's Solution", MedicalItemCategory.Saline, 8, 1, 1, "ringersolution")
    };

}

public sealed class MedicalModConfig
{
    public MedicalModConfig(ConfigFile config)
    {
        EnableMod = config.Bind(
            "General",
            "EnableMod",
            true,
            "Master switch for the medical framework.");

        FeatureMode = config.Bind(
            "General",
            "FeatureMode",
            MedicalFeatureMode.Both,
            "Which features are active: starting loadout, world loot, both, or disabled.");

        CompatibilityMode = config.Bind(
            "Compatibility",
            "CompatibilityMode",
            MultiplayerCompatibilityMode.AutoSafe,
            "How the framework behaves when KrokMP is detected.");

        UseExternalContentFile = config.Bind(
            "Content",
            "UseExternalContentFile",
            true,
            "Load medical item definitions from the JSON content file in BepInEx/config.");

        ExternalContentFilePath = config.Bind(
            "Content",
            "ExternalContentFilePath",
            MedicalContentStore.DefaultContentFilePath,
            "Path to the medical content JSON file.");

        AutoCreateContentFile = config.Bind(
            "Content",
            "AutoCreateContentFile",
            true,
            "Create a default content JSON file if none exists.");

        StartingLoadoutMinItems = config.Bind(
            "StartingLoadout",
            "MinItems",
            0,
            "Minimum number of medical items granted at run start. Set to 0 with MaxItems=0 to disable all starting items.");

        StartingLoadoutMaxItems = config.Bind(
            "StartingLoadout",
            "MaxItems",
            0,
            "Maximum number of medical items granted at run start. Set to 0 to disable all starting items.");

        WorldLootMinItems = config.Bind(
            "WorldLoot",
            "MinItems",
            1,
            "Minimum number of medical items added to world loot.");

        WorldLootMaxItems = config.Bind(
            "WorldLoot",
            "MaxItems",
            4,
            "Maximum number of medical items added to world loot.");

        AllowDuplicateItems = config.Bind(
            "Distribution",
            "AllowDuplicateItems",
            true,
            "Allow the same medical item to appear more than once in a generated plan.");

        Seed = config.Bind(
            "Distribution",
            "Seed",
            0,
            "Optional deterministic seed. Use 0 for a non-deterministic runtime seed.");

        LogGeneratedPlans = config.Bind(
            "Debug",
            "LogGeneratedPlans",
            true,
            "Log the generated loadout and loot plans on startup.");
    }

    public ConfigEntry<bool> EnableMod { get; }
    public ConfigEntry<MedicalFeatureMode> FeatureMode { get; }
    public ConfigEntry<MultiplayerCompatibilityMode> CompatibilityMode { get; }
    public ConfigEntry<bool> UseExternalContentFile { get; }
    public ConfigEntry<string> ExternalContentFilePath { get; }
    public ConfigEntry<bool> AutoCreateContentFile { get; }
    public ConfigEntry<int> StartingLoadoutMinItems { get; }
    public ConfigEntry<int> StartingLoadoutMaxItems { get; }
    public ConfigEntry<int> WorldLootMinItems { get; }
    public ConfigEntry<int> WorldLootMaxItems { get; }
    public ConfigEntry<bool> AllowDuplicateItems { get; }
    public ConfigEntry<int> Seed { get; }
    public ConfigEntry<bool> LogGeneratedPlans { get; }
}

public sealed class MedicalFramework
{
    private readonly ManualLogSource _log;
    private readonly MedicalModConfig _config;
    private readonly bool _krokMpDetected;
    private readonly MedicalFeatureMode _effectiveMode;
    private readonly Random _random;
    private readonly string _contentSource;
    private readonly IReadOnlyList<MedicalItemDefinition> _catalog;

    public MedicalFramework(ConfigFile config, ManualLogSource log)
    {
        _log = log;
        _config = new MedicalModConfig(config);
        _krokMpDetected = DetectKrokMp();

        try
        {
            var catalog = LoadCatalog();
            _catalog = catalog;
        }
        catch (Exception ex)
        {
            _log.LogError($"LoadCatalog threw: {ex}. Falling back to default catalog.");
            _catalog = MedicalCatalog.DefaultItems;
        }
        _contentSource = ResolveContentSource();
        _effectiveMode = ResolveEffectiveMode();
        _random = CreateRandom();
    }

    public bool KrokMpDetected => _krokMpDetected;
    public MedicalFeatureMode EffectiveMode => _effectiveMode;
    public string ContentSource => _contentSource;
    public IReadOnlyList<MedicalItemDefinition> Catalog => _catalog;

    public void Initialize()
    {
        MedicalFrameworkApi.Register(this);

        _log.LogInfo($"Medical framework initialized. KrokMP detected={KrokMpDetected}, effective mode={_effectiveMode}.");
        _log.LogInfo($"Catalog source: {_contentSource}");
        _log.LogInfo($"Catalog items available: {_catalog.Count}.");
        _log.LogInfo($"Compatibility summary: {DescribeCompatibility()}");

        if (_config.LogGeneratedPlans.Value)
        {
            var plan = BuildPlan();
            _log.LogInfo($"Starting loadout plan: {FormatPlan(plan.StartingLoadout)}");
            _log.LogInfo($"World loot plan: {FormatPlan(plan.WorldLoot)}");
        }
    }

    public MedicalPlan BuildPlan()
    {
        var startingLoadout = BuildStartingLoadout();
        var worldLoot = BuildWorldLoot();
        return new MedicalPlan(_effectiveMode, _contentSource, startingLoadout, worldLoot);
    }

    public IReadOnlyList<MedicalGrantRequest> BuildStartingLoadout()
    {
        if (!IsFeatureEnabledForStartingLoadout())
        {
            return Array.Empty<MedicalGrantRequest>();
        }

        var min = Math.Max(0, _config.StartingLoadoutMinItems.Value);
        var max = Math.Max(min, _config.StartingLoadoutMaxItems.Value);

        // MaxItems=0 表示不生成任何起始物品
        if (max <= 0)
        {
            _log.LogInfo("StartingLoadout MaxItems=0, skipping all starting items.");
            return Array.Empty<MedicalGrantRequest>();
        }

        var randomGrants = BuildRandomGrantList(min, max, "StartingLoadout");

        // Grizzly急救包 固定发放 1 个
        var grizzly = new MedicalGrantRequest(
            GrizzlyKitItemSystem.ItemKey,
            GrizzlyKitItemSystem.DisplayName,
            1, "StartingLoadout",
            GrizzlyKitItemSystem.BaseGameItemId);

        // AFAK 急救包 固定发放 1 个
        var afak = new MedicalGrantRequest(
            AfakKitItemSystem.ItemKey,
            AfakKitItemSystem.DisplayName,
            1, "StartingLoadout",
            AfakKitItemSystem.BaseGameItemId);

        // IFAK 急救包 固定发放 1 个
        var ifak = new MedicalGrantRequest(
            IfakKitItemSystem.ItemKey,
            IfakKitItemSystem.DisplayName,
            1, "StartingLoadout",
            IfakKitItemSystem.BaseGameItemId);

        // Salewa 急救包 固定发放 1 个
        var salewa = new MedicalGrantRequest(
            SalewaKitItemSystem.ItemKey,
            SalewaKitItemSystem.DisplayName,
            1, "StartingLoadout",
            SalewaKitItemSystem.BaseGameItemId);

        // AI-2 急救组合 固定发放 1 个
        var ai2 = new MedicalGrantRequest(
            AI2ItemSystem.ItemKey,
            AI2ItemSystem.DisplayName,
            1, "StartingLoadout",
            AI2ItemSystem.BaseGameItemId);

        var result = new List<MedicalGrantRequest>(randomGrants.Count + 5);
        result.Add(grizzly);
        result.Add(afak);
        result.Add(ifak);
        result.Add(salewa);
        result.Add(ai2);
        result.AddRange(randomGrants);
        return result;
    }

    public IReadOnlyList<MedicalGrantRequest> BuildWorldLoot()
    {
        if (!IsFeatureEnabledForWorldLoot())
        {
            return Array.Empty<MedicalGrantRequest>();
        }

        var min = Math.Max(0, _config.WorldLootMinItems.Value);
        var max = Math.Max(min, _config.WorldLootMaxItems.Value);
        return BuildRandomGrantList(min, max, "WorldLoot");
    }

    public string DescribeCompatibility()
    {
        if (KrokMpDetected)
        {
            return "KrokMP detected; world loot spawns only on host, items sync to clients via KrokMP.";
        }

        return "KrokMP not detected; full configured feature set is available.";
    }

    private IReadOnlyList<MedicalItemDefinition> LoadCatalog()
    {
        if (!_config.UseExternalContentFile.Value)
        {
            return MedicalCatalog.DefaultItems;
        }

        var contentPath = _config.ExternalContentFilePath.Value;
        if (!_config.AutoCreateContentFile.Value)
        {
            return MedicalContentStore.LoadOrCreateDefault(contentPath, MedicalCatalog.DefaultItems, _log) is { } file
                ? MedicalContentStore.ToDefinitions(file)
                : MedicalCatalog.DefaultItems;
        }

        var content = MedicalContentStore.LoadOrCreateDefault(contentPath, MedicalCatalog.DefaultItems, _log);
        var catalog = MedicalContentStore.ToDefinitions(content);
        return catalog.Count > 0 ? catalog : MedicalCatalog.DefaultItems;
    }

    private string ResolveContentSource()
    {
        if (!_config.UseExternalContentFile.Value)
        {
            return "Built-in default catalog";
        }

        return $"JSON file: {_config.ExternalContentFilePath.Value}";
    }

    private bool IsFeatureEnabledForStartingLoadout()
    {
        if (!_config.EnableMod.Value)
        {
            return false;
        }

        if (_effectiveMode == MedicalFeatureMode.Disabled)
        {
            return false;
        }

        return _effectiveMode == MedicalFeatureMode.StartingLoadoutOnly || _effectiveMode == MedicalFeatureMode.Both;
    }

    private bool IsFeatureEnabledForWorldLoot()
    {
        if (!_config.EnableMod.Value)
        {
            return false;
        }

        if (_effectiveMode == MedicalFeatureMode.Disabled)
        {
            return false;
        }

        return _effectiveMode == MedicalFeatureMode.WorldLootOnly || _effectiveMode == MedicalFeatureMode.Both;
    }

    private MedicalFeatureMode ResolveEffectiveMode()
    {
        var configured = _config.FeatureMode.Value;
        if (configured == MedicalFeatureMode.Disabled)
        {
            return MedicalFeatureMode.Disabled;
        }

        // KrokMP 多人模式下不再降级功能模式。
        // 世界掉落生成由各生成系统的 KrokMpHelper.ShouldSpawnLoot 检查保证仅主机执行，
        // 客户端通过 KrokMP 的物品同步机制接收主机生成的物品。
        // 兼容模式配置保留向后兼容，但不再抑制世界掉落。
        return configured;
    }

    private Random CreateRandom()
    {
        var configuredSeed = _config.Seed.Value;
        if (configuredSeed != 0)
        {
            return new Random(configuredSeed);
        }

        return new Random(Environment.TickCount ^ GetHashCode());
    }

    private IReadOnlyList<MedicalGrantRequest> BuildRandomGrantList(int minItems, int maxItems, string source)
    {
        var usableCatalog = _catalog.Where(x => x.Enabled).ToList();
        if (usableCatalog.Count == 0 || maxItems <= 0)
        {
            return Array.Empty<MedicalGrantRequest>();
        }

        var count = minItems == maxItems ? minItems : _random.Next(minItems, maxItems + 1);
        var remaining = new List<MedicalItemDefinition>(usableCatalog);
        var results = new List<MedicalGrantRequest>(count);

        for (var i = 0; i < count && remaining.Count > 0; i++)
        {
            var chosen = PickWeighted(remaining);
            var grantCount = chosen.MinCount == chosen.MaxCount
                ? chosen.MinCount
                : _random.Next(chosen.MinCount, chosen.MaxCount + 1);

            results.Add(new MedicalGrantRequest(chosen.Key, chosen.DisplayName, grantCount, source, chosen.GameItemId));

            if (!_config.AllowDuplicateItems.Value)
            {
                remaining.Remove(chosen);
            }
        }

        return results;
    }

    private MedicalItemDefinition PickWeighted(IReadOnlyList<MedicalItemDefinition> items)
    {
        var totalWeight = 0;
        for (var i = 0; i < items.Count; i++)
        {
            totalWeight += Math.Max(1, items[i].Weight);
        }

        var roll = _random.Next(1, totalWeight + 1);
        var accumulator = 0;
        for (var i = 0; i < items.Count; i++)
        {
            accumulator += Math.Max(1, items[i].Weight);
            if (roll <= accumulator)
            {
                return items[i];
            }
        }

        return items[items.Count - 1];
    }

    private bool DetectKrokMp()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var i = 0; i < assemblies.Length; i++)
        {
            var name = assemblies[i].GetName().Name ?? string.Empty;
            if (name.IndexOf("KrokoshaCasualtiesMP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("KrokMP", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatPlan(IReadOnlyList<MedicalGrantRequest> plan)
    {
        if (plan.Count == 0)
        {
            return "<empty>";
        }

        return string.Join(", ", plan.Select(x => x.ToString()));
    }
}

public static class MedicalFrameworkApi
{
    private static MedicalFramework? _instance;

    internal static void Register(MedicalFramework framework)
    {
        _instance = framework;
    }

    public static bool IsInitialized => _instance != null;

    public static bool IsKrokMpDetected => _instance?.KrokMpDetected ?? false;

    public static MedicalFeatureMode EffectiveMode => _instance?.EffectiveMode ?? MedicalFeatureMode.Disabled;

    public static string ContentSource => _instance?.ContentSource ?? "<uninitialized>";

    public static IReadOnlyList<MedicalItemDefinition> Catalog => _instance?.Catalog ?? Array.Empty<MedicalItemDefinition>();

    public static IReadOnlyList<MedicalGrantRequest> BuildStartingLoadout()
    {
        return _instance?.BuildStartingLoadout() ?? Array.Empty<MedicalGrantRequest>();
    }

    public static IReadOnlyList<MedicalGrantRequest> BuildWorldLoot()
    {
        return _instance?.BuildWorldLoot() ?? Array.Empty<MedicalGrantRequest>();
    }
}
