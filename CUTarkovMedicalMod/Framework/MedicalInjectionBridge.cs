using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;

namespace CUTarkovMedicalMod.Framework;

public interface IMedicalItemGrantSink
{
    bool TryGrantStartingLoadout(object bodyInstance, IReadOnlyList<MedicalGrantRequest> plan, ManualLogSource log);
    bool TryInjectWorldLoot(object worldGenerationInstance, IReadOnlyList<MedicalGrantRequest> plan, ManualLogSource log);
}

public static class MedicalInjectionBridge
{
    private static readonly object Lock = new();
    private static IMedicalItemGrantSink? _sink;

    public static void RegisterSink(IMedicalItemGrantSink sink)
    {
        lock (Lock) { _sink = sink; }
    }

    public static bool TryGrantStartingLoadout(object? bodyInstance, IReadOnlyList<MedicalGrantRequest> plan, ManualLogSource log)
    {
        if (bodyInstance == null || plan.Count == 0) return false;
        var sink = _sink;
        return sink != null && sink.TryGrantStartingLoadout(bodyInstance, plan, log);
    }

    public static bool TryInjectWorldLoot(object? worldGenerationInstance, IReadOnlyList<MedicalGrantRequest> plan, ManualLogSource log)
    {
        if (worldGenerationInstance == null || plan.Count == 0) return false;
        var sink = _sink;
        return sink != null && sink.TryInjectWorldLoot(worldGenerationInstance, plan, log);
    }
}

public sealed class DefaultMedicalItemGrantSink : IMedicalItemGrantSink
{
    public bool TryGrantStartingLoadout(object bodyInstance, IReadOnlyList<MedicalGrantRequest> plan, ManualLogSource log)
    {
        if (bodyInstance is not Body body) return false;

        var bodyType = body.GetType();
        var autoPickUp = AccessTools.Method(bodyType, "AutoPickUpItem", new[] { typeof(Item) });
        var firstEmptySlot = AccessTools.Method(bodyType, "FirstEmptySlot", Type.EmptyTypes);
        var pickUpItemCandidates = ResolvePickUpItemCandidates(bodyType);

        // 三根针剂（ETG-c / Zagustin / Morphine）每局必发
        var mutablePlan = new List<MedicalGrantRequest>(plan);
        if (!mutablePlan.Any(EtgCItemSystem.IsEtgRequest))
        {
            mutablePlan.Insert(0, new MedicalGrantRequest(
                EtgCItemSystem.EtgItemKey, EtgCItemSystem.EtgDisplayName,
                1, "GuaranteedEtg", EtgCItemSystem.EtgBaseGameItemId));
        }
        if (!mutablePlan.Any(ZagustinItemSystem.IsZagustinRequest))
        {
            mutablePlan.Insert(1, new MedicalGrantRequest(
                ZagustinItemSystem.ItemKey, ZagustinItemSystem.DisplayName,
                1, "GuaranteedZagustin", ZagustinItemSystem.BaseGameItemId));
        }
        if (!mutablePlan.Any(MorphineItemSystem.IsMorphineRequest))
        {
            mutablePlan.Insert(2, new MedicalGrantRequest(
                MorphineItemSystem.ItemKey, MorphineItemSystem.DisplayName,
                1, "GuaranteedMorphine", MorphineItemSystem.BaseGameItemId));
        }
        if (!mutablePlan.Any(SJ12ItemSystem.IsSJ12Request))
        {
            mutablePlan.Insert(3, new MedicalGrantRequest(
                SJ12ItemSystem.ItemKey, SJ12ItemSystem.DisplayName,
                1, "GuaranteedSJ12", SJ12ItemSystem.BaseGameItemId));
        }
        if (!mutablePlan.Any(MuleItemSystem.IsMuleRequest))
        {
            mutablePlan.Insert(4, new MedicalGrantRequest(
                MuleItemSystem.ItemKey, MuleItemSystem.DisplayName,
                1, "GuaranteedMule", MuleItemSystem.BaseGameItemId));
        }

        // 针剂装入 medkit 发放，其它物品直接发放，避免开局库存被针剂挤占
        var injectorRequests = new List<MedicalGrantRequest>();
        var otherRequests = new List<MedicalGrantRequest>();
        foreach (var request in mutablePlan)
        {
            if (IsInjectorRequest(request))
                injectorRequests.Add(request);
            else
                otherRequests.Add(request);
        }

        var any = false;

        if (injectorRequests.Count > 0)
        {
            any |= GrantMedkitWithInjectors(
                body, injectorRequests, autoPickUp, firstEmptySlot, pickUpItemCandidates, log);
        }

        foreach (var request in otherRequests)
        {
            any |= GrantSingleItem(body, request, autoPickUp, firstEmptySlot, pickUpItemCandidates, log);
        }

        return any;
    }

    /// <summary>
    /// 判断是否为自定义针剂（装入 medkit 的对象）。
    /// </summary>
    private static bool IsInjectorRequest(MedicalGrantRequest request)
        => EtgCItemSystem.IsEtgRequest(request)
           || ZagustinItemSystem.IsZagustinRequest(request)
           || MorphineItemSystem.IsMorphineRequest(request)
           || SJ12ItemSystem.IsSJ12Request(request)
           || MuleItemSystem.IsMuleRequest(request);

    /// <summary>
    /// 发放一个 medkit，并将所有针剂装入其中（通过原生 Container.LoadItem）。
    /// medkit 创建失败、或某根针剂装不入容器时，回退为直接发放该针剂。
    /// </summary>
    private static bool GrantMedkitWithInjectors(
        Body body, IReadOnlyList<MedicalGrantRequest> injectorRequests,
        MethodInfo? autoPickUp, MethodInfo? firstEmptySlot,
        IReadOnlyList<MethodInfo> pickUpItemCandidates, ManualLogSource log)
    {
        var medkitRequest = new MedicalGrantRequest("medkit", "Medkit", 1, "StartingMedkit", "medkit");
        var medkit = CreateMedicalItem(medkitRequest);
        if (medkit == null)
        {
            log.LogWarning("Could not create starting medkit; falling back to direct injector grants.");
            var any = false;
            foreach (var request in injectorRequests)
                any |= GrantSingleItem(body, request, autoPickUp, firstEmptySlot, pickUpItemCandidates, log);
            return any;
        }

        medkit.transform.position = body.transform.position;

        var container = GetItemContainer(medkit);
        log.LogInfo($"Starting medkit spawned, container present = {container != null}.");

        foreach (var request in injectorRequests)
        {
            var spawnCount = Math.Max(1, request.Count);
            for (var i = 0; i < spawnCount; i++)
            {
                var injector = CreateMedicalItem(request);
                if (injector == null)
                {
                    log.LogWarning($"Could not create injector '{request.SpawnItemId}'.");
                    continue;
                }

                // 先配置（修改 id/耐久/图标/标记），使 Container.CanHoldItem 能读到自定义 tags
                ConfigureCustomItem(injector, request);

                if (container != null && TryLoadItemIntoContainer(container, injector))
                {
                    log.LogInfo($"Loaded injector '{request.SpawnItemId}' into medkit ({i + 1}/{spawnCount}).");
                    continue;
                }

                // 回退：直接发放该针剂
                log.LogInfo($"Could not load '{request.SpawnItemId}' into medkit; granting directly ({i + 1}/{spawnCount}).");
                TryPlaceItemInInventory(body, injector, request, autoPickUp, firstEmptySlot, pickUpItemCandidates, log);
            }
        }

        // 拾取 medkit（内含针剂）
        var granted = TryPlaceItemInInventory(body, medkit, medkitRequest, autoPickUp, firstEmptySlot, pickUpItemCandidates, log);
        if (granted)
            log.LogInfo("Granted starting medkit with injectors inside.");
        else
            log.LogWarning("Failed to place starting medkit in inventory.");
        return granted;
    }

    /// <summary>
    /// 创建并直接发放单个物品（非针剂路径，保留原有 拾取→配置 顺序）。
    /// </summary>
    private static bool GrantSingleItem(
        Body body, MedicalGrantRequest request,
        MethodInfo? autoPickUp, MethodInfo? firstEmptySlot,
        IReadOnlyList<MethodInfo> pickUpItemCandidates, ManualLogSource log)
    {
        var spawnCount = Math.Max(1, request.Count);
        var any = false;
        for (var i = 0; i < spawnCount; i++)
        {
            var item = CreateMedicalItem(request);
            if (item == null)
            {
                log.LogWarning($"Could not create loadout item '{request.SpawnItemId}'.");
                continue;
            }

            if (TryPlaceItemInInventory(body, item, request, autoPickUp, firstEmptySlot, pickUpItemCandidates, log))
            {
                // 拾取成功后配置自定义针剂（修改 id + 耐久 + 图标）
                ConfigureCustomItem(item, request);
                any = true;
            }
        }
        return any;
    }

    /// <summary>
    /// 尝试将已创建的物品放入玩家库存：AutoPickUpItem → PickUpItem(slot) → 丢在身边。
    /// 不负责 ConfigureCustomItem，由调用方按需配置。
    /// </summary>
    private static bool TryPlaceItemInInventory(
        Body body, Item item, MedicalGrantRequest request,
        MethodInfo? autoPickUp, MethodInfo? firstEmptySlot,
        IReadOnlyList<MethodInfo> pickUpItemCandidates, ManualLogSource log)
    {
        // 策略1: AutoPickUpItem（最可靠）
        if (autoPickUp != null)
        {
            try
            {
                autoPickUp.Invoke(body, new object[] { item });
                log.LogInfo($"Granted via AutoPickUpItem: {request.SpawnItemId}.");
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning($"AutoPickUpItem '{request.SpawnItemId}' threw: {ex.Message}");
            }
        }

        // 策略2: PickUpItem with slot
        var slotIndex = ResolveSlotIndex(firstEmptySlot?.Invoke(body, Array.Empty<object>()));
        if (slotIndex >= 0 && TryInvokePickUpItem(body, item, slotIndex, true, pickUpItemCandidates))
        {
            log.LogInfo($"Granted via PickUpItem: {request.SpawnItemId}.");
            return true;
        }

        // 策略3: 生成在世界中
        if (TryDropNearBody(body, item, request, log))
        {
            log.LogInfo($"Dropped near body: {request.SpawnItemId}.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 通过反射获取 Item 的 Container（公共属性 container 或私有字段 cont）。
    /// </summary>
    private static object? GetItemContainer(Item item)
    {
        var prop = AccessTools.PropertyGetter(typeof(Item), "container");
        if (prop != null) return prop.Invoke(item, null);
        var field = AccessTools.Field(typeof(Item), "cont");
        return field?.GetValue(item);
    }

    /// <summary>
    /// 将物品装入容器。Container.LoadItem 内部有距离检查（&lt;10），故先把物品定位到容器位置；
    /// 并通过反射调用以规避对 Container 成员可见性的假设。
    /// 成功判定：物品 transform 已被 SetParent 为容器 transform 的子级。
    /// </summary>
    private static bool TryLoadItemIntoContainer(object container, Item item)
    {
        try
        {
            if (container is not Component containerComponent) return false;

            var loadMethod = AccessTools.Method(container.GetType(), "LoadItem", new[] { typeof(Item) });
            if (loadMethod == null) return false;

            // 满足 LoadItem 内部的距离检查
            item.transform.position = containerComponent.transform.position;

            loadMethod.Invoke(container, new object[] { item });

            // 验证：LoadItem 成功会把物品 SetParent 到容器 transform
            return item.transform.parent == containerComponent.transform;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 根据请求类型配置自定义针剂（ETG-c 或 Zagustin）。
    /// </summary>
    private static void ConfigureCustomItem(Item item, MedicalGrantRequest request)
    {
        if (EtgCItemSystem.IsEtgRequest(request))
            EtgCItemSystem.ConfigureSpawnedItem(item, request);
        else if (ZagustinItemSystem.IsZagustinRequest(request))
            ZagustinItemSystem.ConfigureSpawnedItem(item, request);
        else if (MorphineItemSystem.IsMorphineRequest(request))
            MorphineItemSystem.ConfigureSpawnedItem(item, request);
        else if (SJ12ItemSystem.IsSJ12Request(request))
            SJ12ItemSystem.ConfigureSpawnedItem(item, request);
        else if (MuleItemSystem.IsMuleRequest(request))
            MuleItemSystem.ConfigureSpawnedItem(item, request);
    }

    public bool TryInjectWorldLoot(object worldGenerationInstance, IReadOnlyList<MedicalGrantRequest> plan, ManualLogSource log)
    {
        if (worldGenerationInstance is not WorldGeneration world) return false;

        var generateEntityAtPos = AccessTools.Method(world.GetType(), "GenerateEntityAtPos",
            new[] { typeof(Vector2), typeof(GameObject) });
        if (generateEntityAtPos == null) return false;

        var spawnBase = GetSpawnBase(world);
        var any = false;

        foreach (var request in plan)
        {
            var spawnCount = Math.Max(1, request.Count);
            for (var i = 0; i < spawnCount; i++)
            {
                try
                {
                    var prefab = CreateWorldSpawnPrefab(request);
                    if (prefab == null) continue;

                    generateEntityAtPos.Invoke(world, new object[] { spawnBase, prefab });
                    any = true;
                }
                catch (Exception ex)
                {
                    log.LogWarning($"World loot '{request.SpawnItemId}' failed: {ex.Message}");
                }
            }
        }

        return any;
    }

    private static Item? CreateMedicalItem(MedicalGrantRequest request)
    {
        // 策略1: Resources.Load 加载预制体
        try
        {
            var prefab = Resources.Load<GameObject>(request.SpawnItemId);
            if (prefab != null)
            {
                var go = UnityEngine.Object.Instantiate(prefab);
                var item = go.GetComponent<Item>();
                if (item != null) return item;
                UnityEngine.Object.Destroy(go);
            }
        }
        catch { }

        return null;
    }

    private static GameObject? CreateWorldSpawnPrefab(MedicalGrantRequest request)
    {
        var item = CreateMedicalItem(request);
        if (item == null) return null;

        // 配置自定义针剂（修改 id/耐久/图标/marker），否则世界掉落物会是原生 syringe
        ConfigureCustomItem(item, request);

        var root = new GameObject($"MedicalWorldPrefab_{request.SpawnItemId}");
        item.transform.SetParent(root.transform, false);
        item.transform.localPosition = Vector3.zero;

        var drop = item.gameObject.GetComponent<FreshItemDrop>();
        if (drop == null) drop = item.gameObject.AddComponent<FreshItemDrop>();
        drop.enabled = true;

        return root;
    }

    private static List<MethodInfo> ResolvePickUpItemCandidates(Type bodyType)
    {
        return bodyType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => string.Equals(m.Name, "PickUpItem", StringComparison.Ordinal))
            .Where(m => { var args = m.GetParameters(); return args.Length > 0 && args[0].ParameterType == typeof(Item); })
            .OrderByDescending(m => m.GetParameters().Length)
            .ToList();
    }

    private static int ResolveSlotIndex(object? nullableIndex)
    {
        if (nullableIndex == null) return -1;
        var t = nullableIndex.GetType();
        var hasValue = t.GetProperty("HasValue")?.GetValue(nullableIndex) as bool?;
        if (hasValue != true) return -1;
        var value = t.GetProperty("Value")?.GetValue(nullableIndex);
        return value != null ? Convert.ToInt32(value) : -1;
    }

    private static bool TryInvokePickUpItem(object body, Item item, int slotIndex, bool force, IReadOnlyList<MethodInfo> candidates)
    {
        foreach (var method in candidates)
        {
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];
            var ok = true;

            for (var i = 0; i < parameters.Length; i++)
            {
                var pt = parameters[i].ParameterType;
                if (i == 0 && pt == typeof(Item)) { args[i] = item; continue; }
                if (pt == typeof(int)) { args[i] = slotIndex; continue; }
                if (pt == typeof(bool)) { args[i] = force; continue; }

                var underlying = Nullable.GetUnderlyingType(pt);
                if (underlying == typeof(int)) { args[i] = slotIndex >= 0 ? Activator.CreateInstance(pt, slotIndex) : null; continue; }
                if (parameters[i].HasDefaultValue) { args[i] = parameters[i].DefaultValue; continue; }
                if (!pt.IsValueType) { args[i] = null; continue; }
                if (pt.IsEnum) { args[i] = Activator.CreateInstance(pt); continue; }
                ok = false; break;
            }

            if (!ok) continue;

            try
            {
                method.Invoke(body, args);
                return true;
            }
            catch { }
        }
        return false;
    }

    private static bool TryDropNearBody(Body body, Item item, MedicalGrantRequest request, ManualLogSource log)
    {
        try
        {
            var world = WorldGeneration.world;
            if (world != null)
            {
                var genMethod = AccessTools.Method(world.GetType(), "GenerateEntityAtPos",
                    new[] { typeof(Vector2), typeof(GameObject) });
                if (genMethod != null)
                {
                    var drop = item.gameObject.GetComponent<FreshItemDrop>();
                    if (drop == null) drop = item.gameObject.AddComponent<FreshItemDrop>();
                    drop.enabled = true;

                    var col = item.gameObject.GetComponent<BoxCollider2D>();
                    if (col == null) col = item.gameObject.AddComponent<BoxCollider2D>();

                    genMethod.Invoke(world, new object[] { (Vector2)body.transform.position, item.gameObject });
                    return true;
                }
            }

            item.transform.position = body.transform.position + new Vector3(0.6f, 0f, 0f);
            item.gameObject.layer = LayerMask.NameToLayer("Ground");
            item.gameObject.SetActive(true);
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning($"Drop failed for '{request.SpawnItemId}': {ex.Message}");
            return false;
        }
    }

    private static Vector2 GetSpawnBase(WorldGeneration world)
    {
        var body = world.body;
        return body != null ? (Vector2)body.transform.position : Vector2.zero;
    }
}

[HarmonyPatch]
public static class MedicalSpawnHooks
{
    private static ManualLogSource? _log;
    private static bool _startingLoadoutGrantedThisRun;

    public static bool WasStartingLoadoutGrantedThisRun => _startingLoadoutGrantedThisRun;

    public static void SetLog(ManualLogSource log) => _log = log;

    [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.Start))]
    [HarmonyPrefix]
    static void ResetRunState() => _startingLoadoutGrantedThisRun = false;

    [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.Start))]
    [HarmonyPostfix]
    static void GrantOnWorldStart()
    {
        if (!MedicalFrameworkApi.IsInitialized || _startingLoadoutGrantedThisRun) return;

        var plan = MedicalFrameworkApi.BuildStartingLoadout();
        if (plan.Count == 0)
        {
            plan = new[] { new MedicalGrantRequest(
                EtgCItemSystem.EtgItemKey, EtgCItemSystem.EtgDisplayName,
                1, "WorldStartFallback", EtgCItemSystem.EtgBaseGameItemId) };
        }

        var body = WorldGeneration.world?.body;
        if (body == null) return;

        if (MedicalInjectionBridge.TryGrantStartingLoadout(body, plan, _log ?? Plugin.Log))
        {
            _startingLoadoutGrantedThisRun = true;
            _log?.LogInfo($"WorldGeneration.Start grant completed. Items={plan.Count}");
        }
        else
        {
            MedicalStartLoadoutRetrier.Attach(body, plan, _log ?? Plugin.Log);
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.Start))]
    [HarmonyPostfix]
    static void GrantOnBodyStart()
    {
        if (!MedicalFrameworkApi.IsInitialized || _startingLoadoutGrantedThisRun) return;

        var plan = MedicalFrameworkApi.BuildStartingLoadout();
        if (plan.Count == 0) return;

        var body = WorldGeneration.world?.body;
        if (body == null) return;

        if (MedicalInjectionBridge.TryGrantStartingLoadout(body, plan, _log ?? Plugin.Log))
        {
            _startingLoadoutGrantedThisRun = true;
        }
        else
        {
            MedicalStartLoadoutRetrier.Attach(body, plan, _log ?? Plugin.Log);
        }
    }

    internal static void MarkGrantedFromRetry(int items)
    {
        _startingLoadoutGrantedThisRun = true;
        _log?.LogInfo($"Delayed grant completed. Items={items}");
    }

    public static void ForceDebugGrantCurrentRun() => _startingLoadoutGrantedThisRun = false;

    public static void TickGlobalGrantFallback()
    {
        if (_startingLoadoutGrantedThisRun || !MedicalFrameworkApi.IsInitialized) return;

        var body = WorldGeneration.world?.body;
        if (body == null) return;

        var plan = MedicalFrameworkApi.BuildStartingLoadout();
        if (plan.Count == 0)
        {
            plan = new[] { new MedicalGrantRequest(
                EtgCItemSystem.EtgItemKey, EtgCItemSystem.EtgDisplayName,
                1, "UpdateFallback", EtgCItemSystem.EtgBaseGameItemId) };
        }

        if (MedicalInjectionBridge.TryGrantStartingLoadout(body, plan, _log ?? Plugin.Log))
        {
            _startingLoadoutGrantedThisRun = true;
        }
    }
}

public sealed class MedicalStartLoadoutRetrier : MonoBehaviour
{
    private const float RetryIntervalSeconds = 1f;
    private const int MaxAttempts = 8;

    private readonly List<MedicalGrantRequest> _plan = new();
    private ManualLogSource? _log;
    private Body? _body;
    private float _timer;
    private int _attempt;

    public static void Attach(Body body, IReadOnlyList<MedicalGrantRequest> plan, ManualLogSource log)
    {
        if (body == null || plan.Count == 0) return;

        var retrier = body.gameObject.GetComponent<MedicalStartLoadoutRetrier>();
        if (retrier == null) retrier = body.gameObject.AddComponent<MedicalStartLoadoutRetrier>();

        retrier._body = body;
        retrier._log = log;
        retrier._plan.Clear();
        retrier._plan.AddRange(plan);
        retrier._attempt = 0;
        retrier._timer = 0f;
        retrier.enabled = true;
    }

    private void Awake() => enabled = false;

    private void Update()
    {
        if (_body == null || _plan.Count == 0) { enabled = false; return; }

        _timer += Time.deltaTime;
        if (_timer < RetryIntervalSeconds) return;

        _timer = 0f;
        _attempt++;

        if (MedicalInjectionBridge.TryGrantStartingLoadout(_body, _plan, _log ?? Plugin.Log))
        {
            MedicalSpawnHooks.MarkGrantedFromRetry(_plan.Count);
            enabled = false;
            return;
        }

        if (_attempt >= MaxAttempts) enabled = false;
    }
}

[HarmonyPatch]
public static class MedicalWorldLootHooks
{
    private static ManualLogSource? _log;
    public static void SetLog(ManualLogSource log) => _log = log;

    [HarmonyPatch(typeof(WorldGeneration), nameof(WorldGeneration.FinishWorldGeneration))]
    [HarmonyPostfix]
    static void Postfix()
    {
        if (!MedicalFrameworkApi.IsInitialized || MedicalFrameworkApi.EffectiveMode == MedicalFeatureMode.Disabled)
            return;

        // KrokMP 模式下补充发放
        if (!MedicalSpawnHooks.WasStartingLoadoutGrantedThisRun)
        {
            var loadoutPlan = MedicalFrameworkApi.BuildStartingLoadout();
            if (loadoutPlan.Count == 0)
            {
                loadoutPlan = new[] { new MedicalGrantRequest(
                    EtgCItemSystem.EtgItemKey, EtgCItemSystem.EtgDisplayName,
                    1, "FinishWorldGenFallback", EtgCItemSystem.EtgBaseGameItemId) };
            }

            var body = WorldGeneration.world?.body;
            if (body != null)
            {
                if (MedicalInjectionBridge.TryGrantStartingLoadout(body, loadoutPlan, _log ?? Plugin.Log))
                {
                    MedicalSpawnHooks.MarkGrantedFromRetry(loadoutPlan.Count);
                }
                else
                {
                    MedicalStartLoadoutRetrier.Attach(body, loadoutPlan, _log ?? Plugin.Log);
                }
            }
        }

        // World loot（KrokMP 安全模式下跳过）
        if (MedicalFrameworkApi.IsKrokMpDetected && MedicalFrameworkApi.EffectiveMode == MedicalFeatureMode.StartingLoadoutOnly)
            return;

        var plan = MedicalFrameworkApi.BuildWorldLoot();
        if (plan.Count == 0) return;

        MedicalInjectionBridge.TryInjectWorldLoot(WorldGeneration.world, plan, _log ?? Plugin.Log);
    }
}
