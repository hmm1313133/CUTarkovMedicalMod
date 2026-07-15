# Casualties: Unknown - Tarkov-Style Medical Mod

> `未知伤亡（Casualties: Unknown）：塔科夫医疗模组`
>
> **v0.2.5**

一个为 **Casualties: Unknown Demo** 开发的 BepInEx 模组，将《逃离塔科夫》中的 16 种战斗兴奋剂注射器、12 种医疗物品及其完整医疗系统引入游戏。

> **相关模组：** 枪械/弹药/弹匣系统已拆分至独立模组 [CUTarkovWeaponMod](https://github.com/hmm1313133/CUTarkovWeaponMod)（`com.yourname.cu.tarkovweaponmod`）。
> 武器模组依赖本医疗模组，需同时安装。

## 目录

- [功能概览](#功能概览)
- [16 种针剂一览](#16-种针剂一览)
- [12 种医疗物品一览](#12-种医疗物品一览)
- [项目结构](#项目结构)
- [核心架构](#核心架构)
- [原生游戏系统](#原生游戏系统)
- [构建与部署](#构建与部署)
- [配置项](#配置项)
- [调试热键](#调试热键)
- [技术要点](#技术要点)
- [依赖](#依赖)

## 功能概览

| 功能 | 说明 |
|------|------|
| 16 种自定义针剂 | 每种针剂有独立的 ItemKey、ItemInfo、useAction 委托和效果控制器；无临时技能等级增加 |
| 12 种医疗物品 | 急救包（BandageMinigame）、手术包（自动检测）、药膏/药片（液体系统） |
| 开局发放 | 固定发放 5 件医疗物品（Grizzly/AFAK/IFAK/Salewa/AI-2）+ 随机医疗物品 |
| 世界战利品 | 医疗物品在世界中刷新 |
| 容器掉落 | 医疗箱/物资箱/尸体破坏时按概率掉落针剂、药品 |
| 智力识别系统 | 所有物品支持原生 Recognition 系统，智力不足时隐藏名称和效果 |
| 多语言支持 | 中/英双语 I18n 系统，通过 Lang 文件夹下的 JSON 加载 |
| 外部模组集成 | I18n 支持外部翻译注入；ConsoleSpawnPatch 支持外部物品配置回调 |
| Buff 指示器 | 通过原生 MoodleManager 显示针剂效果图标和倒计时 |
| 管视效果 | URP Vignette 后处理实现暗角遮罩，部分针剂副作用触发 |
| 注射器音效 | 自定义 `med_stimulator_use.wav` 音效，使用时播放 |
| 悬停描述 | 按住 SHIFT 展开物品效果详情，不按则仅显示简介 |
| 控制台 Spawn | 控制台 `spawn` 命令支持所有自定义物品 ID |
| 多人兼容 | 自动检测 KrokMP，安全模式下仅启用开局发放 |

## 16 种针剂一览

| # | 针剂 | ItemKey | 定位 | 增益 | 副作用 | 持续 |
|---|------|---------|------|------|--------|------|
| 1 | **eTG-c** | `etg_c` | 再生兴奋剂 | 每部位 +2 肌肉/s +2 表皮/s，血容量 +50ml/s 至 5L | 60s 后：每秒 -1 饱食/水分，胸口 +40 疼痛 | 60s + 20s debuff |
| 2 | **Zagustin** | `zagustin` | 止血剂（紫针） | 立即止血 + 180s 防出血 | 血液粘稠度 +50，前 120s 每秒 -0.3 水分 | 180s |
| 3 | **Morphine** | `cu_morphine` | 止痛剂 | 原生 Painkillers 止疼（opiateAmount=100） | 一次性 -10 饱食 / -15 水分 | ~300s |
| 4 | **SJ12** | `sj12` | 体温调节 | 体温 -4°C，每秒 +0.2 饱食/水分 | +4 患病，-2kg；增益后体温 +4°C 过热 | 600s + 120s debuff |
| 5 | **M.U.L.E.** | `mule` | 负重增强 | 负重上限 +15（Transpiler） | +10 患病，每秒 -0.2 肌肉/s（25min），意识 ≤90 | 2400s（40min） |
| 6 | **Propital** | `propital` | 再生兴奋剂 | 每部位 +0.1 肌肉/s +0.1 表皮/s，阿片 +20 | +10 患病；3min 后 RES/STR 永久 -2；10min 后管视+震颤 5min | 900s（15min） |
| 7 | **SJ1** | `sj1` | 耐力强化 | 耐力上限 +10%、耐力恢复 +50%，阿片镇痛 +5 | +10 患病，每秒 -0.1 饱食/水分 | 300s（5min） |
| 8 | **SJ6** | `sj6` | 耐力强化 | +20% 耐力上限，+120% 耐力恢复 | +25 患病，10min 后管视+震颤 5min | 900s（15min） |
| 9 | **SJ9** | `sj9` | 体温抑制 | 体温锁定 31°C | +15 患病，RES 永久 -2；10min 后胸口疼痛+肌肉损伤 10min | 1200s（20min） |
| 10 | **PNB** | `pnb` | 肌肉修复 | 指甲恢复满值，2min 内 +0.2 肌肉/s | 增益后 STR 永久 -1，震颤 60s | 300s + 60s debuff |
| 11 | **Obdolbos** | `obdolbos` | 赌命鸡尾酒 | 随机触发 8 种效果之一（含猝死） | 每次不同 | 随机 |
| 12 | **Obdolbos 2** | `obdolbos2` | 永久强化 | 永久 STR/RES/INT +6，负重 +3u（40min） | -30% 耐力恢复，-20% 耐力上限（40min）；5min 后饱食/水分 -0.2/s + 肌肉 -0.3/s（5min） | 2400s（40min） |
| 13 | **Blue Blood** | `blueblood` | 人造血/解毒 | 止血+防出血 120s，毒素 -70%，辐射 -10Gy | 延迟 3min 后免疫力 -40（60s），33% 呕吐，-0.3 饱食/s | 120s + 60s debuff |
| 14 | **xTG-12** | `xtg12` | 解毒剂 | +70 免疫力，毒素清零，感染 -80%（2min），败血 -20%（2min） | 3min 时 20% 呕吐；5min 后颤栗 1min | 300s + 60s debuff |
| 15 | **Mildronate** | `mildronate` | 心脏保护 | 纤颤 -20%，+10% 耐力上限，+50% 耐力恢复 | 前 15min 每秒 -0.1 饱食/水分 | 1500s（25min） |
| 16 | **2A2-(b-TG)** | `2a2btg` | 负重增强 | 负重 +7u，心情 +5 | 前 15min 每秒 -0.1 水分 | 1200s（20min） |

## 12 种医疗物品一览

| # | 物品 | ItemKey | 模板 | 使用方式 | 效果 | 副作用 |
|---|------|---------|------|----------|------|--------|
| 1 | **AI-2** | `ai2` | syringe | 贴肢 SyringeMinigame（100ml，每次10ml） | 辐射 -1Gy，阿片 +0.2，内出血 -2（每10ml） | +3 患病，-10% 免疫力，-1 饱食/水分 |
| 2 | **Grizzly** | `grizzlykit` | bruisekit | 贴肢 BandageMinigame | 大幅止血+骨折恢复+脱臼恢复+表皮+肌肉恢复+消毒，耐久极高 | 重量 3u（很重） |
| 3 | **AFAK** | `afak` | bruisekit | 贴肢 BandageMinigame | 中幅止血+骨折/脱臼恢复+表皮恢复，耐久较高 | - |
| 4 | **IFAK** | `ifak` | bruisekit | 贴肢 BandageMinigame | 中幅止血+骨折/脱臼恢复+表皮恢复，耐久中等 | - |
| 5 | **Salewa** | `salewa` | bruisekit | 贴肢 BandageMinigame | 中幅止血+骨折/脱臼恢复+表皮恢复，耐久很高 | - |
| 6 | **Salewa（保温）** | `salewa` | - | 胸部+体温<30°C 时触发 | 绷带变卡其色，体温回升至 36°C | - |
| 7 | **CMS 手术包** | `cms` | bruisekit | 贴肢自动检测 | 拔弹片(ShrapnelMinigame)/复位脱臼(DislocationMinigame)/加速50%骨折恢复(+50疼痛) | - |
| 8 | **Surv12 手术包** | `multitool` | bruisekit | 贴肢自动检测 | 拔弹片/复位脱臼/加速90%骨折恢复(+30疼痛)，耐久很高 | - |
| 9 | **金星药膏** | `goldenstar` | bruisekit | 贴肢（10ml，每次2ml） | 消毒 30s，延迟止痛 15s（疼痛降至10%） | 表皮 -5，心情 -5，意识清醒度降低 10s |
| 10 | **凡士林** | `vaseline` | bruisekit | 贴肢（10ml，每次2ml） | 脏污度 -2，表皮 +5，手部使用爪子 +10 | - |
| 11 | **力百汀** | `libatine` | bruisekit | 物品栏饮用（2ml，1次） | 抵抗力 +80 持续5min，1min内感染降至60% | 心情 -3，5/7/10min各5%概率呕吐 |
| 12 | **布洛芬** | `ibuprofen` | bruisekit | 物品栏饮用（10ml，每次2ml） | 抵抗力+50%/感染降至15%/体温-2°C/止痛/耐力恢复+20% 持续7min | 心情-3，7/10min各10%呕吐；10min内二次服用触发过量（可能致死） |

## 项目结构

```
CasualtiesUnknownTarkovMedicalMod/
├── CasualtiesUnknownTarkovMedicalMod.sln      # VS 解决方案（含医疗+武器两个项目）
├── vars.targets                                # 游戏路径 & BepInEx 输出目录配置
├── README.md                                   # 本文件
└── CUTarkovMedicalMod/                         # 医疗模组主项目
    ├── CUTarkovMedicalMod.csproj               # 项目文件（资源复制、依赖引用、InternalsVisibleTo）
    ├── Plugin.cs                               # BepInEx 插件入口
    └── Framework/                              # 全部模组逻辑
        ├── MedicalFramework.cs                 # 核心框架：配置、目录、发放计划
        ├── MedicalInjectionBridge.cs            # 发放桥接：开局发放、世界战利品、Harmony 钩子
        ├── MedicalContentStore.cs               # JSON 内容文件 I/O
        ├── MedicalDebugHotkeys.cs               # 调试热键（F7）
        ├── StimBuffIndicator.cs                 # Buff 指示器（MoodleManager 集成）
        ├── SkillEffectHelper.cs                 # 技能/震颤/管视辅助工具
        ├── TunnelVisionOverlay.cs               # URP Vignette 管视遮罩
        ├── HoverDescriptionHelper.cs            # 悬停描述 SHIFT 展开逻辑
        ├── StimConditionFix.cs                  # 修复针剂耐久被覆盖
        ├── InjectorSound.cs                     # 注射器音效播放
        ├── ImmunityReductionManager.cs          # 免疫力降低/加成管理（Transpiler + BonusManager）
        ├── ConsoleSpawnPatch.cs                 # 控制台 spawn + ConfigureCustomItem + ExternalItemConfigurer
        ├── HasTagNullSafetyPatch.cs             # ItemInfo.HasTag 空安全补丁
        ├── WorldContainerLootSpawner.cs         # 世界容器战利品刷新
        ├── I18n.cs                              # 多语言系统（支持外部模组翻译注入）
        ├── EtgCItemSystem.cs                    # eTG-c + 全局注册 Patch
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
        ├── AI2ItemSystem.cs                     # AI-2 急救组合注射器
        ├── GrizzlyKitItemSystem.cs              # Grizzly 急救包
        ├── AfakKitItemSystem.cs                 # AFAK 急救包
        ├── IfakKitItemSystem.cs                 # IFAK 急救包
        ├── SalewaKitItemSystem.cs               # Salewa 急救包（含保温机制）
        ├── CmsKitItemSystem.cs                  # CMS 手术包
        ├── MultiToolItemSystem.cs               # Surv12 野战手术包
        ├── GoldenStarItemSystem.cs              # 金星药膏
        ├── VaselineItemSystem.cs                # 凡士林
        ├── LibatineItemSystem.cs                # 力百汀抗生素
        ├── IbuprofenItemSystem.cs               # 布洛芬止痛药
        ├── Lang/                                # 中英文翻译 JSON
        └── Assets/                              # 图标/音效资源
            ├── *.png / *.webp                   # 物品图标
            └── *.wav                            # 音效（注射器、药瓶等）
```

## 核心架构

### 启动流程

```
Plugin.Awake()
  ├─ SkillEffectHelper.InitializeTunnelVision()   # 创建管视遮罩单例
  ├─ MedicalFramework.Initialize()                 # 加载配置、目录、构建发放计划
  ├─ MedicalInjectionBridge.RegisterSink()         # 注册发放策略
  └─ harmony.PatchAll()                            # 挂载所有 Harmony 补丁
```

### 物品注册机制

每根针剂通过 `EnsureRegisteredInItemTable()` 注册：

1. 反射获取 `Item.GlobalItems` 字典（`Dictionary<string, ItemInfo>`）
2. 克隆 `syringe` 的 `ItemInfo` 作为基础模板（药品类克隆 `bruisekit`）
3. 覆盖 `fullName`、`description`、`category`、`tags`、`usable`/`usableOnLimb`
4. 通过 `Delegate.CreateDelegate` 将私有静态方法绑定为 `ItemInfo.Use` / `ItemInfo.UseLimb` 委托
5. 写入字典：`map[itemKey] = clone`

注册由 `EtgStimRegistryPatch`（`Item.SetupItems` Postfix）触发，游戏初始化物品表时执行。

### 使用流程

**针剂类（useAction）**：
```
玩家左键点击针剂
  -> Body.UseItem(item)
    -> useAction.Invoke(body, item)
      -> XxxUseAction(body, item)
        -> InjectorSound.Play()                    # 播放注射音效
        -> 激活 EffectController（MonoBehaviour）
        -> DropItem + Destroy(item)
```

**医疗包类（useLimbAction）**：
```
玩家在肢体上使用医疗包
  -> Body.UseItemOnLimb(item, limb)
    -> useLimbAction.Invoke(limb, item)
      -> MinigameBase.main.StartMinigame(...)      # 启动 BandageMinigame / ShrapnelMinigame 等
      -> minigame 回调中消耗耐久 + 应用效果
```

### 效果控制器模式

每根针剂的效果由一个 `MonoBehaviour` 控制器实现，附加到 `Body` 游戏对象上：

- **Attach(Body)** - 获取或添加控制器组件
- **ActivateOrRefresh()** - 激活或刷新效果（重新计时）
- **Update()** - 每帧/每秒执行效果逻辑
- **Buff 指示器** - 每帧调用 `StimBuffIndicator.ShowBuff()` 更新 UI

### 多来源 Bonus 叠加系统

当多个针剂提供同类加成（免疫力/耐力恢复/耐力上限）时，使用管理器确保正确叠加：

| 管理器 | 加成类型 | 应用方式 |
|--------|---------|---------|
| `ImmunityBonusManager` | 免疫力加成 | Transpiler（集中注入到 HandlePeriodicChecks） |
| `ImmunityReductionManager` | 免疫力降低 | Transpiler（在 Clamp 后的 stfld 前插入减法） |
| `StaminaBonusManager` | 耐力恢复加成 | 各 EffectController 的 `IsTopSource` 门控 |
| `StaminaCapBonusManager` | 耐力上限加成 | 各 EffectController 的 `IsTopSource` 门控 |

**叠加规则：** 取最强生效；同源刷新时间不叠加；最强过期后次强自动接管。

### 外部模组集成接口

医疗模组为武器模组（CUTarkovWeaponMod）提供以下集成接口：

| 接口 | 位置 | 用途 |
|------|------|------|
| `I18n.RegisterExternalLangDir(dir)` | I18n.cs | 外部模组注入翻译文件目录，合并到翻译系统 |
| `ConsoleSpawnPatch.ExternalItemConfigurer` | ConsoleSpawnPatch.cs | 外部物品配置回调，返回 true 表示已处理 |
| `ConsoleSpawnPatch.CustomItemPrefabs` | ConsoleSpawnPatch.cs | 物品ID->预制体映射字典，外部模组可添加条目 |
| `InternalsVisibleTo("CUTarkovWeaponMod")` | csproj | 暴露 internal 成员给武器模组 |

## 原生游戏系统

| 系统 | 关键字段/方法 | 模组使用方式 |
|------|-------------|-------------|
| **疼痛** | `Body.averagePain`、`Limb.pain`、`Painkillers` 组件 | 吗啡注入 `opiateAmount`，原生系统自动降低 `limb.pain` |
| **体温** | `Body.temperature`、`HandleBodyTemperature()` | SJ12/SJ9 直接设置/锁定体温；Salewa 低温保温；布洛芬 -2°C |
| **负重** | `Body.maxEncumberance`、`HandlePeriodicChecks()` | M.U.L.E. 用 Transpiler 追加 +15；2A2-(b-TG) 追加 +7u；Obdolbos2 追加 +3u |
| **耐力** | `Body.stamina`、`staminaStrength` 曲线 | SJ6/Mildronate/SJ1/Ibuprofen 通过 StaminaBonusManager 每帧操作 |
| **技能** | `Skills.STR/RES/INT`、`AddExp()` | Propital/SJ9 永久降低等级；Obdolbos2/Obdolbos 永久提升等级 |
| **智力识别** | `Recognition.min`、`recognizable` | 所有物品设置智力要求；Hover Patch 检查 recognizable |
| **出血** | `Limb.bleedAmount`、`blockedBleeding` | Zagustin/Blue Blood/急救包控制 |
| **骨折/脱臼** | `Limb.boneHealTimer`、`Limb.dislocated` | 急救包加速恢复；CMS/Surv12 手术治疗 |
| **弹片** | `Limb.shrapnel`、`ShrapnelMinigame` | CMS/Surv12 自动检测并启动镊子 minigame |
| **感染** | `Limb.infectionAmount` | 力百汀降至 60%；布洛芬降至 15% |
| **毒素/辐射** | `Body.venomCurrent`、`Body.radiationSickness` | Blue Blood/xTG-12 清除；AI-2 每次降辐射 |
| **免疫力** | `Body.immunity`、`HandlePeriodicChecks` | Transpiler 在 IL 层注入加成/减幅 |
| **Moodle** | `MoodleManager.AddMoodle()` | StimBuffIndicator 注入自定义图标并显示 buff |
| **液体系统** | `Liquids.Registry`、`WaterContainerItem` | AI-2/力百汀/布洛芬/金星/凡士林注册自定义液体 |
| **Minigame** | `BandageMinigame`、`SyringeMinigame`、`ShrapnelMinigame`、`DislocationMinigame` | 医疗包/手术包/注射器接入原生小游戏 |
| **震颤** | `Body.miscShakeIntensity` | SkillEffectHelper.AddStimulantTremor |
| **管视** | `TunnelVisionOverlay` (URP Vignette) | Propital/SJ6 副作用触发 |

## 构建与部署

### 环境要求

- .NET SDK（支持 .NET Framework 4.8）
- BepInEx 已安装到游戏目录
- 游戏路径配置在 `vars.targets` 中

### 配置游戏路径

编辑 `vars.targets`，将 `BaseGamePath` 指向你的游戏安装目录：

```xml
<BaseGamePath>F:/SteamLibrary/steamapps/common/Casualties Unknown Demo</BaseGamePath>
```

### 构建

```powershell
dotnet build .\CUTarkovMedicalMod\CUTarkovMedicalMod.csproj -c Release
```

构建成功后，MSBuild 自动将以下文件复制到：
```
{BaseGamePath}/BepInEx/plugins/CUTarkovMedicalMod/
├── CUTarkovMedicalMod.dll
├── Framework/Assets/        # 全部 .png、.webp 图标和 .wav 音效
└── Lang/                    # 中英文翻译 JSON
```

### 验证

启动游戏后检查 `BepInEx/LogOutput.log`，应看到：
```
Casualties: Unknown - Tarkov-Style Medical Mod loaded. Enabled=True
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
| StartingLoadout | `MinItems` / `MaxItems` | `1` / `3` | 随机医疗物品数量范围 |
| WorldLoot | `MinItems` / `MaxItems` | `1` / `4` | 世界战利品数量范围 |
| Distribution | `AllowDuplicateItems` | `true` | 允许重复物品 |
| Distribution | `Seed` | `0` | 随机种子（0 = 随机） |
| Debug | `LogGeneratedPlans` | `true` | 日志输出发放计划 |

## 调试热键

| 按键 | 功能 |
|------|------|
| `F7` / `小键盘7` | 输出运行时状态（模组初始化、模式、KrokMP、手持物品） |

## 技术要点

### 物品键命名

- 所有针剂使用**独立 ItemKey**（如 `etg_c`、`cu_morphine`），绝不与原生物品重名
- `EnsureRegisteredInItemTable` 的 `if(map.Contains(key)) return` 会跳过已存在的键

### useAction / useLimbAction 委托绑定

```csharp
clone.useAction = (ItemInfo.Use)Delegate.CreateDelegate(typeof(ItemInfo.Use), useMethod);
clone.useLimbAction = (ItemInfo.UseLimb)Delegate.CreateDelegate(typeof(ItemInfo.UseLimb), useLimbMethod);
```

游戏原生 `Body.UseItem` / `Body.UseItemOnLimb` 直接调用委托，无需 Harmony 拦截。

### 液体药品系统

AI-2、力百汀、布洛芬、金星药膏、凡士林使用原生 `LiquidItemInfo` + `Liquids.Registry` 系统：

1. 注册自定义液体到 `Liquids.Registry`，定义 `onDrink`/`onHealthUse` 回调
2. 物品使用 `LiquidItemInfo`，设置 `capacity` 和 `defaultContents`
3. `WaterContainerItem.Drink()` / `.Inject()` / `.ApplyToLimb()` 消耗液体并触发回调

### 免疫力操控（Transpiler）

原生 `Body.HandlePeriodicChecks` 每 0.5 秒重算 `immunity`。直接修改会被覆盖。通过 Transpiler 在 IL 的 `stfld immunity` 前插入加成/减幅指令。

### 体温操控对抗

原生 `HandleBodyTemperature` 在 `Update` 中将体温 lerp 向环境温度。SJ12/SJ9 的效果控制器在 `LateUpdate` 中覆写体温，使用足够大的 LerpStrength 对抗原生 lerp。

### 负重上限重算

原生 `HandlePeriodicChecks` 每 0.5 秒重算 `maxEncumberance`。通过 Transpiler 在 `stfld maxEncumberance` 前插入 `GetEncumberanceBonus()` 调用，使加成只在重算时应用一次。

### 智力识别系统（Recognition）

| 物品类别 | rec.min | 说明 |
|----------|---------|------|
| 手术包（Surv12/CMS） | 6 | 基础外科手术 |
| 药品/急救包 | 8 | 常见医疗知识 |
| 吗啡 | 9 | 简单止痛剂 |
| 注射器/兴奋剂 | 13 | 高级医疗化合物 |

### 多语言支持（I18n）

模组内置中/英双语系统，通过 `Lang/zh_CN.json` 和 `Lang/EN.json` 加载翻译。自动检测游戏 `Locale.currentLangName` 切换语言。

`I18n.RegisterExternalLangDir()` 方法允许其他模组（如武器模组）注册额外的翻译文件目录，翻译键会合并到统一的翻译字典中。

## 依赖

| 包 | 版本 | 用途 |
|----|------|------|
| BepInEx.Core | 5.* | 插件框架 |
| BepInEx.AssemblyPublicizer | 0.4.2 | 公开化游戏程序集私有成员 |
| UnityEngine.Modules | 2022.3.18 | Unity 引擎模块 |
| Assembly-CSharp | - | 游戏主程序集（通过 `vars.targets` 路径引用） |
| Unity.RenderPipelines.Universal.Runtime | - | URP 后处理（管视遮罩） |
| Unity.TextMeshPro | - | 文本渲染 |
| UnityEngine.UI | - | UI 组件 |
