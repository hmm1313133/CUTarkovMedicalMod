# CUTarkovMedicalMod Changelog

## v0.3.1 (2026-07-23)

### Bug 修复 / Bug Fixes

- **[严重/Critical] 英文本地化不生效**: `I18n.DetectLanguage()` 使用 `GetField("currentLangName")` 获取语言，但 `currentLangName` 是属性（property）而非字段，返回 null 导致始终回退中文。改为直接访问 `Locale.currentLangName`
- **[严重/Critical] English Localization Not Working**: `I18n.DetectLanguage()` used `GetField("currentLangName")` but `currentLangName` is a property, not a field, returning null and always falling back to Chinese. Fixed by direct `Locale.currentLangName` access
- **[严重/Critical] 切换语言后物品名称不刷新**: CUCoreLib `ItemRegistry.Register()` 在注册时调用 `LocaleRegistry.Get()` 一次性烘焙 `fullName`/`description`，切换语言后不刷新。新增 `ItemI18nRegistry` 在语言切换时刷新所有已注册物品的名称和描述
- **[严重/Critical] Item Names Not Refreshing on Language Switch**: CUCoreLib bakes `fullName`/`description` at registration time. Added `ItemI18nRegistry` to refresh all registered item names/descriptions on language change
- **[严重/Critical] 注射后状态栏不显示**: 性能优化中 `EnsureLoaded()` 使用 `Time.realtimeSinceStartup` 做节流，该 API 在特定时序上下文中可能抛出异常，导致效果控制器 `Update()` 被 Unity 永久禁用。改为纯调用计数器节流
- **[严重/Critical] Buff Status Bar Not Showing After Performance Optimization**: `Time.realtimeSinceStartup` in `EnsureLoaded()` could throw in certain timing contexts, permanently disabling effect controller `Update()`. Replaced with pure call counter
- **[严重/Critical] 技能等级调整反弹**: `SkillEffectHelper.AdjustLevel()` 修改等级后未同步经验值（exp），游戏 `CheckForLevelUp`（`while(exp >= max) level++`）和 `CheckForLevelDown`（`exp < min`）立即恢复原等级。修复：所有 delta（正/负）均同步 `exp = min`
- **[严重/Critical] Skill Level Adjustment Bounce-Back**: `AdjustLevel()` modified level without syncing experience, causing `CheckForLevelUp`/`CheckForLevelDown` to immediately restore original level. Fixed: sync `exp = min` for ALL deltas
- **[中等/Medium] 存档恢复技能等级反弹**: `EffectBackup.cs` 和 `KrokMpStateBridge.cs` 直接赋值 `body.skills.STR = ...`，未同步 exp。改用新增的 `SkillEffectHelper.SetLevel()` 方法
- **[Medium/Medium] Save Restoration Skill Level Bounce-Back**: `EffectBackup.cs` and `KrokMpStateBridge.cs` directly assigned `body.skills.STR` without exp sync. Switched to new `SkillEffectHelper.SetLevel()` method

### 变更 / Changes

- **Obdolbos 2 重新平衡 / Obdolbos 2 Rebalance**:
  - STR/RES/INT 永久 +6 → +8 / Permanent STR/RES/INT +6 → +8
  - 负重上限 +3u → +10u / Carry weight +3u → +10u
  - 耐力恢复惩罚 -30% → -20% / Stamina recovery penalty -30% → -20%
  - 增益持续时间 2400s(40min) → 1800s(30min) / Buff duration 2400s → 1800s
  - 饱食/水分消耗 0.2/s → 0.1/s / Food/water drain 0.2/s → 0.1/s
  - 肌肉损伤 0.3/s → 0.5/s（头部+胸部）/ Muscle drain 0.3/s → 0.5/s (head + chest)

### 性能优化 / Performance Optimizations

- **ImmunityReductionManager**: 6 处 `List.RemoveAll(lambda)` 替换为手动 for 循环 + `RemoveRange`，消除每帧 lambda 闭包 GC 分配
- **ImmunityReductionManager**: Replaced 6 `List.RemoveAll(lambda)` calls with manual for-loop + `RemoveRange` to eliminate per-frame lambda closure GC allocation
- **StimBuffIndicator**: 移除 `AddBuffs()` 节流（`Time.deltaTime` 累加器）和 `buff.Remaining -= dt` 递减，每帧直接刷新
- **StimBuffIndicator**: Removed `AddBuffs()` throttling (`Time.deltaTime` accumulator) and `buff.Remaining -= dt` decrement
- **I18n**: 移除 `TrAll()` 哈希缓存（每次创建新数组），`EnsureLoaded()` 改用纯调用计数器替代 `Time.realtimeSinceStartup`
- **I18n**: Removed `TrAll()` hash cache, replaced `Time.realtimeSinceStartup` with pure call counter in `EnsureLoaded()`
- **KrokMpHelper**: 缓存 `IsKrokMpInstalled` 检测结果；`ShouldSpawnLoot` 缓存 2 秒
- **KrokMpHelper**: Cached `IsKrokMpInstalled` result; `ShouldSpawnLoot` cached for 2 seconds
- **StimConditionFix**: 新增 `ConditionalWeakTable<WaterContainerItem, Item>` 缓存 `GetComponent<Item>()`
- **StimConditionFix**: Added `ConditionalWeakTable<WaterContainerItem, Item>` cache for `GetComponent<Item>()`
- **MuleItemSystem**: `ClampConscious()` 节流至 0.5 秒
- **MuleItemSystem**: Throttled `ClampConscious()` to 0.5s interval
- **TunnelVisionOverlay**: `Update()` 在非激活且 alpha < 0.001 时提前返回
- **TunnelVisionOverlay**: `Update()` early return when inactive and alpha < 0.001
- **MedcrateStimSpawner**: 新增 `HashSet<int>` 防止已损坏医疗箱每帧重复处理
- **MedcrateStimSpawner**: Added `HashSet<int>` guard to prevent per-frame reprocessing of damaged medcrates
