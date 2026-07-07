# Casualties: Unknown — Tarkov-Style Medical Mod

> `未知伤亡（Casualties: Unknown）：塔科夫医疗模组`

一个为 **Casualties: Unknown Demo** 开发的 BepInEx 模组，将《逃离塔科夫》中的 16 种战斗兴奋剂注射器及其医疗物品系统引入游戏。每根针剂拥有独立的增益/副作用机制，通过反射注册到游戏原生物品表，利用原生 `Body.UseItem` → `useAction` 委托链触发自定义效果。

## 目录

- [功能概览](#功能概览)
- [16 种针剂一览](#16-种针剂一览)
- [项目结构](#项目结构)
- [核心架构](#核心架构)
- [原生游戏系统](#原生游戏系统)
- [构建与部署](#构建与部署)
- [配置项](#配置项)
- [调试热键](#调试热键)
- [技术要点](#技术要点)

## 功能概览

| 功能 | 说明 |
|------|------|
| 16 种自定义针剂 | 每种针剂有独立的 ItemKey、ItemInfo、useAction 委托和效果控制器 |
| 开局发放 | 每局必发全部 16 根针剂，装入 1 个 medkit 容器中，不挤占库存 |
| 世界战利品 | 随机医疗物品（含针剂）在世界中刷新 |
| 医疗箱掉落 | 破坏医疗箱时 30% 概率掉落 0–3 根随机针剂 |
| Buff 指示器 | 通过原生 MoodleManager 显示针剂效果图标和倒计时 |
| 管视效果 | URP Vignette 后处理实现暗角遮罩，部分针剂副作用触发 |
| 悬停描述 | 按住 SHIFT 展开针剂效果详情，不按则仅显示简介 |
| 多人兼容 | 自动检测 KrokMP，安全模式下仅启用开局发放 |

## 16 种针剂一览

| # | 针剂 | ItemKey | 定位 | 增益 | 副作用 | 持续 |
|---|------|---------|------|------|--------|------|
| 1 | **eTG-c** | `etg_c` | 再生兴奋剂 | 每部位 +2 肌肉/s，血容量 +50ml/s 至 5L | 20s：每秒 -1 饱食/水分，胸口 +40 疼痛 | 60s + 20s debuff |
| 2 | **Zagustin** | `zagustin` | 止血剂（紫针） | 长时间防出血 | 饱食/水分消耗，颤栗 | 150s |
| 3 | **Morphine** | `cu_morphine` | 止痛剂 | 原生 Painkillers 止疼（opiateAmount=35） | 一次性 -10 饱食 / -15 水分 | ~300s |
| 4 | **SJ12** | `sj12` | 体温调节 | 体温 → 31.5°C（可规避炮塔热感应） | 体温 → 40.5°C 过热 | 600s buff + 180s debuff |
| 5 | **M.U.L.E.** | `mule` | 负重增强 | +50% 负重上限（Harmony Postfix） | 每秒随机部位 -0.1 肌肉 | 900s |
| 6 | **Propital** | `propital` | 再生兴奋剂 | 每部位 +0.1 肌肉/s +0.1 表皮/s，opiate +20 | 患病 +10，延迟 STR/RES -2，管视+震颤 | 900s + 300s debuff |
| 7 | **SJ1** | `sj1` | 属性强化 | STR +5、RES +3 | 患病 +10，每秒 -0.1 饱食/水分 | 300s |
| 8 | **SJ6** | `sj6` | 耐力强化 | +20% 耐力上限，+120% 耐力恢复 | 患病 +25，延迟管视+震颤 | 900s + 300s debuff |
| 9 | **SJ9** | `sj9` | 体温抑制 | 体温锁定 31°C | RES -2，延迟胸口疼痛+肌肉损伤 | 1200s + 600s debuff |
| 10 | **PNB** | `pnb` | 肌肉修复 | 每部位 +0.2 肌肉/s（2min），RES +3（5min） | 延迟 STR -1，震颤 60s | 120s + 300s |
| 11 | **Obdolbos** | `obdolbos` | 赌命鸡尾酒 | 随机触发 8 种效果之一（含猝死） | 每次不同 | 随机 |
| 12 | **Obdolbos 2** | `obdolbos2` | 永久强化 | 永久 STR/RES/INT +6，负重 +3u | -30% 耐力恢复，-20% 耐力上限 40min | 永久 + 300s debuff |
| 13 | **Blue Blood** | `blueblood` | 人造血/解毒 | 止血 120s，毒素 -70%，辐射 -10gy | 延迟免疫力 -40%，33% 呕吐 | 120s + 60s debuff |
| 14 | **xTG-12** | `xtg12` | 解毒剂 | +70% 抵抗力，毒素 -100% | 20% 呕吐，延迟震颤 | 300s + 60s debuff |
| 15 | **Mildronate** | `mildronate` | 心脏保护 | 纤颤 -20%，+10% 耐力上限，+50% 恢复 | 每秒 -0.1 饱食/水分 | 1500s + 900s debuff |
| 16 | **2A2-(b-TG)** | `2a2btg` | 负重增强 | +7u 负重上限，心情 +5 | 每秒 -0.1 水分 | 1200s + 900s debuff |

## 项目结构

```
CasualtiesUnknownTarkovMedicalMod/
├── CasualtiesUnknownTarkovMedicalMod.sln      # VS 解决方案
├── vars.targets                                # 游戏路径 & BepInEx 输出目录配置
├── README.md                                   # 本文件
└── CUTarkovMedicalMod/                         # 模组主项目
    ├── CUTarkovMedicalMod.csproj               # 项目文件（资源复制、依赖引用）
    ├── Plugin.cs                               # BepInEx 插件入口
    └── Framework/                              # 全部模组逻辑
        ├── MedicalFramework.cs                 # 核心框架：配置、目录、发放计划
        ├── MedicalInjectionBridge.cs            # 发放桥接：开局发放、世界战利品、Harmony 钩子
        ├── MedicalContentStore.cs               # JSON 内容文件 I/O
        ├── MedicalDebugHotkeys.cs               # 调试热键（F6/F7）
        ├── StimBuffIndicator.cs                 # Buff 指示器（MoodleManager 集成）
        ├── SkillEffectHelper.cs                 # 技能/震颤/管视辅助工具
        ├── TunnelVisionOverlay.cs               # URP Vignette 管视遮罩
        ├── HoverDescriptionHelper.cs            # 悬停描述 SHIFT 展开逻辑
        ├── StimConditionFix.cs                  # 修复针剂耐久被覆盖
        ├── MedcrateStimSpawner.cs               # 医疗箱破坏掉落针剂
        ├── EtgCItemSystem.cs                    # eTG-c + 全局注册 Patch (EtgStimRegistryPatch)
        ├── ZagustinItemSystem.cs                # Zagustin 止血剂
        ├── MorphineItemSystem.cs                # 吗啡止痛剂
        ├── SJ12ItemSystem.cs                    # SJ12 体温调节
        ├── MuleItemSystem.cs                    # M.U.L.E. 负重针
        ├── PropitalItemSystem.cs                # Propital 再生剂
        ├── Sj1ItemSystem.cs                     # SJ1 属性强化
        ├── SJ6ItemSystem.cs                     # SJ6 耐力强化
        ├── Sj9ItemSystem.cs                     # SJ9 体温抑制
        ├── PnbItemSystem.cs                     # PNB 肌肉修复
        ├── ObdolbosItemSystem.cs                # Obdolbos 赌命针
        ├── Obdolbos2ItemSystem.cs               # Obdolbos 2 永久强化
        ├── BluebloodItemSystem.cs               # 人造血（蓝血）
        ├── Xtg12ItemSystem.cs                   # xTG-12 解毒剂
        ├── MildronateItemSystem.cs              # 米屈肼
        ├── TwoATwoBTGItemSystem.cs              # 2A2-(b-TG) 负重针
        └── Assets/                              # 图标资源（.png 16x16 + .webp 512x512）
            ├── etg.png / etg.webp
            ├── zagustin.png / Zagustin.webp
            ├── morphine.png / Morphine.webp
            ├── sj12.png / sj12.webp
            ├── mule.png / M.U.L.E.webp
            ├── propital.png / propital.webp
            ├── sj6.png / sj6.webp
            ├── pnb.png / pnb.webp
            ├── sj1.png / sj1.webp
            ├── obdolbos.png / obd1.webp
            ├── sj9.png / sj9.webp
            ├── blueblood.png / blueblood.webp
            ├── xtg12.png / xtg12.webp
            ├── Mildronate.png / Mildronate.webp
            ├── 2a2btg.png / 2a2btg.webp
            └── obd2.png / obd2.webp
```

## 核心架构

### 启动流程

```
Plugin.Awake()
  ├─ SkillEffectHelper.InitializeTunnelVision()   # 创建管视遮罩单例
  ├─ MedicalFramework.Initialize()                 # 加载配置、目录、构建发放计划
  ├─ MedicalInjectionBridge.RegisterSink()         # 注册发放策略
  ├─ MedicalSpawnHooks.SetLog() / MedicalWorldLootHooks.SetLog()
  └─ harmony.PatchAll()                            # 挂载所有 Harmony 补丁
```

### 物品注册机制

每根针剂通过 `EnsureRegisteredInItemTable()` 注册：

1. 反射获取 `Item.GlobalItems` 字典（`Dictionary<string, ItemInfo>`）
2. 克隆 `syringe` 的 `ItemInfo` 作为基础模板
3. 覆盖 `fullName`、`description`、`category="ModStim"`、`tags`、`usable=true`
4. 通过 `Delegate.CreateDelegate` 将私有静态方法绑定为 `ItemInfo.Use` 委托
5. 写入字典：`map[itemKey] = clone`

注册由两处触发：
- `EtgStimRegistryPatch`（`Item.SetupItems` Postfix）— 游戏初始化物品表时
- `Plugin.Update` 每 300 帧轮询 — 确保注册不丢失

### 使用流程

```
玩家左键点击针剂
  → Body.UseItem(item)
    → 检查 Stats.usable == true（不检查 condition）
      → useAction.Invoke(body, item)
        → XxxUseAction(body, item)
          → 激活 EffectController（MonoBehaviour）
          → DropItem + Destroy(item)
```

### 开局发放流程

```
WorldGeneration.Start (Postfix)
  → MedicalSpawnHooks.GrantOnWorldStart()
    → MedicalInjectionBridge.TryGrantStartingLoadout(body, plan)
      → DefaultMedicalItemGrantSink.TryGrantStartingLoadout()
        ├─ 确保全部 16 根针剂在计划中
        ├─ 分离针剂请求 vs 其他医疗物品
        ├─ GrantMedkitWithInjectors()    # 创建 medkit，装入全部针剂
        │   ├─ Resources.Load("medkit")
        │   ├─ GetItemContainer(medkit)
        │   ├─ foreach injector: CreateMedicalItem → ConfigureCustomItem → Container.LoadItem
        │   └─ TryPlaceItemInInventory(medkit)
        └─ GrantSingleItem() × N         # 其他物品直接发放
```

### 效果控制器模式

每根针剂的效果由一个 `MonoBehaviour` 控制器实现，附加到 `Body` 游戏对象上：

- **Attach(Body)** — 获取或添加控制器组件
- **ActivateOrRefresh()** — 激活或刷新效果（重新计时）
- **Update()** — 每帧/每秒执行效果逻辑（治疗、体温操控、耐力恢复等）
- **Buff 指示器** — 每帧调用 `StimBuffIndicator.ShowBuff()` 更新 UI

### 世界战利品

```
WorldGeneration.FinishWorldGeneration (Postfix)
  → MedicalWorldLootHooks.Postfix()
    → MedicalInjectionBridge.TryInjectWorldLoot(world, plan)
      → foreach request: CreateWorldSpawnPrefab → GenerateEntityAtPos
```

### 医疗箱掉落

```
BuildingEntity.Update (Prefix)
  → MedcrateStimSpawner.Prefix()
    → 检测 medcrate 且 health < 0.5
    → 30% 概率掉落 0–3 根针剂（按权重分布）
```

## 原生游戏系统

模组深度利用以下游戏原生子系统（通过反编译 `Assembly-CSharp.dll` 分析所得）：

| 系统 | 关键字段/方法 | 模组使用方式 |
|------|-------------|-------------|
| **疼痛** | `Body.averagePain`、`Limb.pain`、`Painkillers` 组件 | 吗啡注入 `opiateAmount`，原生系统自动降低 `limb.pain` |
| **体温** | `Body.temperature`、`HandleBodyTemperature()` | SJ12/SJ9 直接设置/锁定体温，原生 moodle 和着色器自动响应 |
| **负重** | `Body.maxEncumberance`、`HandlePeriodicChecks()` | M.U.L.E. 用 Harmony Postfix 在重算后追加 50%，2A2-(b-TG) 追加 +7u |
| **耐力** | `Body.stamina`、`staminaStrength` 曲线 | SJ6/Mildronate 每帧操作 stamina 字段 |
| **技能** | `Skills.STR/RES/INT`、`AddExp()`、`UpdateExpBoundaries()` | SJ1/Propital/PNB/Obdolbos2 临时或永久调整等级 |
| **出血** | `Limb.bleedAmount` | Zagustin/Blue Blood 控制 |
| **毒素/辐射** | `Body.toxinAmount`、`Body.radiation` | Blue Blood/xTG-12 清除 |
| **Moodle** | `MoodleManager.AddMoodle()`、`icons` 字典 | StimBuffIndicator 注入自定义图标并显示 buff |
| **容器** | `Item.container`、`Container.LoadItem()` | 开局将针剂装入 medkit |
| **震颤** | `Body.miscShakeIntensity` | SkillEffectHelper.AddStimulantTremor |
| **着色器** | `PlayerCamera.HandleScreenShaders` | 体温/moodle 自动驱动，模组不直接修改 |

## 构建与部署

### 环境要求

- .NET SDK（支持 .NET Framework 4.8）
- BepInEx 已安装到游戏目录
- 游戏路径配置在 `vars.targets` 中

### 配置游戏路径

编辑 `vars.targets`，将 `BaseGamePath` 指向你的游戏安装目录：

```xml
<BaseGamePath>O:/SteamLibrary/steamapps/common/Casualties Unknown Demo</BaseGamePath>
```

### 构建

```powershell
Set-Location "I:\CasualtiesUnknownTarkovMedicalMod"
dotnet build .\CUTarkovMedicalMod\CUTarkovMedicalMod.csproj -c Debug
```

构建成功后，MSBuild 自动将以下文件复制到：
```
{BaseGamePath}/BepInEx/plugins/CUTarkovMedicalMod/
├── CUTarkovMedicalMod.dll
└── Framework/Assets/        # 全部 .png 和 .webp 图标
```

### 验证

启动游戏后检查 `BepInEx/LogOutput.log`，应看到：
```
Casualties: Unknown - Tarkov-Style Medical Mod loaded. Enabled=True
Medical content source: ...
Catalog item count: 27
```

## 配置项

模组在 `BepInEx/config/` 下自动生成配置文件，主要选项：

| 分类 | 选项 | 默认值 | 说明 |
|------|------|--------|------|
| General | `EnableMod` | `true` | 总开关 |
| General | `FeatureMode` | `Both` | 功能模式：Disabled / StartingLoadoutOnly / WorldLootOnly / Both |
| Compatibility | `CompatibilityMode` | `AutoSafe` | KrokMP 检测时的兼容策略 |
| Content | `UseExternalContentFile` | `true` | 从 JSON 文件加载物品定义 |
| Content | `AutoCreateContentFile` | `true` | JSON 不存在时自动创建 |
| StartingLoadout | `MinItems` / `MaxItems` | `1` / `3` | 随机医疗物品数量范围（针剂必发，不占用此配额） |
| WorldLoot | `MinItems` / `MaxItems` | `1` / `4` | 世界战利品数量范围 |
| Distribution | `AllowDuplicateItems` | `true` | 允许重复物品 |
| Distribution | `Seed` | `0` | 随机种子（0 = 随机） |
| Debug | `LogGeneratedPlans` | `true` | 日志输出发放计划 |

## 调试热键

| 按键 | 功能 |
|------|------|
| `F6` / `小键盘6` | 强制向当前玩家发放一根 eTG-c |
| `F7` / `小键盘7` | 输出运行时状态（模组初始化、模式、KrokMP、手持物品） |

## 技术要点

### 物品键命名

- 所有针剂使用**独立 ItemKey**（如 `etg_c`、`cu_morphine`），绝不与原生物品重名
- `EnsureRegisteredInItemTable` 的 `if(map.Contains(key)) return` 会跳过已存在的键 —— 键冲突会导致自定义 `useAction` **永远无法注册**
- 吗啡的键从 `morphine` 改为 `cu_morphine` 即为修复此问题（原生 `morphine` 是液体药瓶 `LiquidItemInfo`）

### useAction 委托绑定

```csharp
clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(
    typeof(ItemInfo.Use), useMethod);
```

游戏原生 `Body.UseItem` 在检查 `usable==true` 后直接调用 `useAction.Invoke(body, item)`，无需 Harmony 拦截。

### 体温操控对抗

原生 `HandleBodyTemperature` 在 `Update` 中将体温 lerp 向环境温度。SJ12/SJ9 的效果控制器在 `LateUpdate` 中覆写体温，使用足够大的 LerpStrength 对抗原生 lerp。

### 负重上限重算

原生 `HandlePeriodicChecks` 每 0.5 秒重算 `maxEncumberance`，直接设置会被覆盖。M.U.L.E. 使用 Harmony Postfix 在重算后追加 50%。

### 管视遮罩自愈

`TunnelVisionOverlay` 使用 URP Volume + Vignette 实现。Volume 的 GameObject 可能被场景切换销毁，控制器在 `Update` 中检测引用失效后自动重建。

### 耐久修复

`WaterContainerItem.UpdateCondition` 会覆写 `item.condition`，导致针剂耐久显示异常。`StimConditionFix`（Harmony Postfix）在调用后强制将模组针剂的 condition 恢复为 1f。

## 依赖

| 包 | 版本 | 用途 |
|----|------|------|
| BepInEx.Core | 5.* | 插件框架 |
| BepInEx.AssemblyPublicizer | 0.4.2 | 公开化游戏程序集私有成员 |
| UnityEngine.Modules | 2022.3.18 | Unity 引擎模块 |
| Assembly-CSharp | — | 游戏主程序集（通过 `vars.targets` 路径引用） |
| Unity.RenderPipelines.Universal.Runtime | — | URP 后处理（管视遮罩） |
| Unity.TextMeshPro | — | 文本渲染 |
| UnityEngine.UI | — | UI 组件 |
