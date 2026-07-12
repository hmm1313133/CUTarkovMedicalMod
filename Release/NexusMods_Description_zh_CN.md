# 未知伤亡：塔科夫医疗模组 (Casualties: Unknown - Tarkov-Style Medical Mod)

**版本：0.1.0**

一个为 **Casualties: Unknown Demo** 开发的 BepInEx 模组，将《逃离塔科夫》中的 16 种战斗兴奋剂注射器、12 种医疗物品及其完整医疗系统引入游戏。每根针剂拥有独立的增益/副作用机制，通过反射注册到游戏原生物品表；医疗包通过原生小游戏系统（BandageMinigame / SyringeMinigame / ShrapnelMinigame / DislocationMinigame）接入。

---

## 前置要求

| 要求 | 说明 |
|------|------|
| **游戏** | Casualties: Unknown Demo（Steam） |
| **模组加载器** | BepInEx 5.x |

### 安装 BepInEx

1. 下载 [BepInEx 5.x 稳定版](https://github.com/BepInEx/BepInEx/releases)（x64）
2. 将内容解压到游戏根目录（`Steam/steamapps/common/Casualties Unknown Demo/`）
3. 启动一次游戏以生成 `BepInEx/` 文件夹结构
4. 关闭游戏

---

## 安装方法

1. 下载模组压缩包（`CUTarkovMedicalMod_v0.1.0.zip`）
2. 将压缩包内容解压到游戏根目录
   - 压缩包内含：`BepInEx/plugins/CUTarkovMedicalMod/...`
   - 解压后结构：
     ```
     [游戏根目录]/
     └── BepInEx/
         └── plugins/
             └── CUTarkovMedicalMod/
                 ├── CUTarkovMedicalMod.dll
                 ├── Framework/
                 │   └── Assets/        (图标 + 音效)
                 └── Lang/              (EN.json, zh_CN.json)
     ```
3. 启动游戏

### 验证安装

检查 `BepInEx/LogOutput.log`，应看到：
```
Casualties: Unknown - Tarkov-Style Medical Mod loaded. Enabled=True
Medical content source: ...
Catalog item count: 38
```

---

## 功能概览

- **16 种自定义针剂** - 每种针剂有独立的 ItemKey、ItemInfo、useAction 委托和效果控制器
- **12 种医疗物品** - 急救包（BandageMinigame）、手术包（自动检测）、药膏/药片（液体系统）
- **开局发放** - 固定发放 5 件医疗物品（Grizzly/AFAK/IFAK/Salewa/AI-2）+ 随机医疗物品
- **世界战利品** - 随机医疗物品（含针剂和药品）在世界中刷新
- **容器掉落** - 医疗箱/物资箱/尸体破坏时按概率掉落针剂或药品
- **Buff 指示器** - 通过原生 MoodleManager 显示针剂效果图标和倒计时
- **管视效果** - URP Vignette 后处理实现暗角遮罩，部分针剂副作用触发
- **注射器音效** - 自定义 `med_stimulator_use.wav` 音效
- **悬停描述** - 按住 SHIFT 展开物品效果详情，不按则仅显示简介
- **控制台 Spawn** - 控制台 `spawn` 命令支持所有自定义物品 ID
- **多人兼容** - 自动检测 KrokMP，安全模式下仅启用开局发放
- **双语支持** - 英语和简体中文

---

## 16 种针剂

| # | 针剂 | ItemKey | 定位 | 增益 | 副作用 | 持续 |
|---|------|---------|------|------|--------|------|
| 1 | **eTG-c** | `etg_c` | 再生兴奋剂 | 每部位 +2 肌肉/s，血容量 +50ml/s 至 5L | 20s：每秒 -1 饱食/水分，胸口 +40 疼痛 | 60s + 20s debuff |
| 2 | **Zagustin** | `zagustin` | 止血剂 | 长时间防出血 | 饱食/水分消耗，颤栗 | 150s |
| 3 | **吗啡** | `cu_morphine` | 止痛剂 | 原生 Painkillers 止疼（opiateAmount=35） | 极大幅阿片影响 - 不自救会致死！ | ~300s |
| 4 | **SJ12** | `sj12` | 体温调节 | 体温 -> 31.5°C，韧性 +2，每秒 +0.2 饱食/水分 | 体温 -> 40.5°C 过热 | 600s buff + 180s debuff |
| 5 | **M.U.L.E.** | `mule` | 负重增强 | +50% 负重上限 | 每秒随机部位 -0.1 肌肉，意识上限 90% | 900s |
| 6 | **Propital** | `propital` | 再生兴奋剂 | 每部位 +0.1 肌肉/s +0.1 表皮/s，opiate +20 | 患病 +10，延迟 STR/RES -2，管视+震颤 | 900s + 300s debuff |
| 7 | **SJ1** | `sj1` | 属性强化 | STR +5、RES +3、耐力恢复 +30% | 患病 +10，每秒 -0.1 饱食/水分 | 300s |
| 8 | **SJ6** | `sj6` | 耐力强化 | +20% 耐力上限，+120% 耐力恢复 | 患病 +25，延迟管视+震颤 | 900s + 300s debuff |
| 9 | **SJ9** | `sj9` | 体温抑制 | 体温锁定 31°C | RES -2，延迟胸口疼痛+肌肉损伤 | 1200s + 600s debuff |
| 10 | **PNB** | `pnb` | 肌肉修复 | 每部位 +0.2 肌肉/s（2min），RES +3（5min） | 延迟 STR -1，震颤 60s | 120s + 300s |
| 11 | **Obdolbos** | `obdolbos` | 赌命鸡尾酒 | 随机触发 8 种效果之一（含猝死） | 每次不同 | 随机 |
| 12 | **Obdolbos 2** | `obdolbos2` | 永久强化 | 永久 STR/RES/INT +6，负重 +3u | -30% 耐力恢复，-20% 耐力上限 40min | 永久 + 300s debuff |
| 13 | **蓝血** | `blueblood` | 人造血/解毒 | 止血 120s，毒素 -70%，辐射 -10Gy | 延迟免疫力 -40%，33% 呕吐 | 120s + 60s debuff |
| 14 | **xTG-12** | `xtg12` | 解毒剂 | +70% 抵抗力，毒素 -100% | 20% 呕吐，延迟震颤 | 300s + 60s debuff |
| 15 | **米屈肼** | `mildronate` | 心脏保护 | 纤颤 -20%，+10% 耐力上限，+50% 恢复 | 每秒 -0.1 饱食/水分 | 1500s + 900s debuff |
| 16 | **2A2-(b-TG)** | `2a2btg` | 负重增强 | +7u 负重上限，心情 +5 | 每秒 -0.1 水分 | 1200s + 900s debuff |

---

## 12 种医疗物品

| # | 物品 | ItemKey | 使用方式 | 效果 | 副作用 |
|---|------|---------|----------|------|--------|
| 1 | **AI-2** | `ai2` | 贴肢 SyringeMinigame（100ml，每次10ml） | 辐射 -1Gy，阿片 +0.2，内出血 -8%（每10ml） | +3 患病，-10% 免疫力，-1 饱食/水分 |
| 2 | **Grizzly** | `grizzlykit` | 贴肢 BandageMinigame | 大幅止血+骨折恢复+脱臼恢复+表皮+肌肉恢复+消毒，耐久极高 | 重量 3u（很重） |
| 3 | **AFAK** | `afak` | 贴肢 BandageMinigame | 中幅止血+骨折/脱臼恢复+表皮恢复，耐久较高 | - |
| 4 | **IFAK** | `ifak` | 贴肢 BandageMinigame | 中幅止血+骨折/脱臼恢复+表皮恢复，耐久中等 | - |
| 5 | **Salewa** | `salewa` | 贴肢 BandageMinigame | 中幅止血，耐久很高 | - |
| 6 | **Salewa（保温）** | `salewa` | 胸部+体温<30°C 时自动触发 | 绷带变卡其色，体温回升至 36°C | - |
| 7 | **CMS 手术包** | `cms` | 贴肢自动检测 | 拔弹片/复位脱臼/加速50%骨折恢复(+50疼痛) | - |
| 8 | **Surv12 手术包** | `multitool` | 贴肢自动检测 | 拔弹片/复位脱臼/加速90%骨折恢复(+30疼痛)，耐久很高 | 略重 |
| 9 | **金星药膏** | `goldenstar` | 贴肢（10ml，每次2ml） | 消毒 30s，延迟止痛 15s（疼痛降至10%） | 表皮 -5，心情 -5，意识降低 10s |
| 10 | **凡士林** | `vaseline` | 贴肢（10ml，每次2ml） | 脏污度 -2，表皮 +5，手部使用爪子 +10 | - |
| 11 | **力百汀** | `libatine` | 物品栏饮用（2ml，1次） | 抵抗力 +80 持续5min，1min内感染降至60% | 心情 -3，5/7/10min各5%概率呕吐 |
| 12 | **布洛芬** | `ibuprofen` | 物品栏饮用（10ml，每次2ml） | 抵抗力+50%/感染降至15%/体温-2°C/止痛/耐力恢复+20% 持续7min | 心情-3，7/10min各10%呕吐；**10min内二次服用触发过量 - 可能致死！** |

---

## 配置项

模组在 `BepInEx/config/com.yourname.cu.tarkovmedicalmod.cfg` 自动生成配置文件。

| 分类 | 选项 | 默认值 | 说明 |
|------|------|--------|------|
| General | `EnableMod` | `true` | 总开关 |
| General | `FeatureMode` | `Both` | 功能模式：Disabled / StartingLoadoutOnly / WorldLootOnly / Both |
| Compatibility | `CompatibilityMode` | `AutoSafe` | KrokMP 检测时的兼容策略 |
| Content | `UseExternalContentFile` | `true` | 从 JSON 文件加载物品定义 |
| Content | `AutoCreateContentFile` | `true` | JSON 不存在时自动创建 |
| StartingLoadout | `MinItems` / `MaxItems` | `1` / `3` | 随机医疗物品数量范围（固定发放的5件不占用此配额） |
| WorldLoot | `MinItems` / `MaxItems` | `1` / `4` | 世界战利品数量范围 |
| Distribution | `AllowDuplicateItems` | `true` | 允许重复物品 |
| Distribution | `Seed` | `0` | 随机种子（0 = 随机） |
| Debug | `LogGeneratedPlans` | `true` | 日志输出发放计划 |

---

## 操作说明

| 按键 | 功能 |
|------|------|
| **左键点击**（物品栏中的物品） | 使用针剂 / 饮用液体药品 |
| **左键点击**（身体部位上） | 使用医疗包 / 手术包 / 药膏 |
| **按住 SHIFT** | 展开悬停描述，显示完整效果详情 |
| **F7** / **小键盘7** | 调试：输出运行时状态（模组初始化、模式、KrokMP、手持物品） |

---

## 控制台命令

所有自定义物品均可通过开发者控制台生成：

```
spawn etg_c
spawn cu_morphine
spawn grizzlykit
spawn cms
...
```

使用 `spawn` + 上表中的任意 ItemKey。

---

## 容器掉落概率

| 容器类型 | 针剂掉落 | 药品掉落 |
|----------|----------|----------|
| 医疗箱（medcrate） | 17% 掉 1-2 根 | 20% 掉 1-2 件 |
| 物资箱（containercrate） | - | 15% 掉 1-3 件 |
| 尸体（corpse） | - | 10% 掉 1 件 |

---

## 兼容性

- **KrokMP（多人模组）**：自动检测。安全模式下仅启用开局发放，防止不同步。
- **其他 BepInEx 模组**：应兼容。模组使用唯一 GUID `com.yourname.cu.tarkovmedicalmod` 的 Harmony 补丁。

---

## 卸载

删除 `BepInEx/plugins/CUTarkovMedicalMod/` 文件夹即可。

---

## 源代码 & GitHub

本模组完全开源，源代码托管在 GitHub：

**[https://github.com/hmm1313133/CUTarkovMedicalMod](https://github.com/hmm1313133/CUTarkovMedicalMod)**

---

## Bug 反馈 & 建议

发现 Bug？有建议？请在 GitHub Issues 中反馈：

**[https://github.com/hmm1313133/CUTarkovMedicalMod/issues](https://github.com/hmm1313133/CUTarkovMedicalMod/issues)**

提交 Bug 报告时，请包含以下信息：

1. **模组版本**（当前 0.1.0）
2. **游戏版本**（查看游戏主菜单）
3. **BepInEx 版本**
4. **其他已安装模组**（如有）
5. **复现步骤** - 如何触发 Bug
6. **日志文件** - 附上 `BepInEx/LogOutput.log`（或粘贴相关错误行）
7. **截图**（如有）

> 提交前请先查看已有 Issues，避免重复报告。

---

## 致谢

- **Escape from Tarkov** (Battlestate Games) - 原始物品设计、描述和机制灵感
- **Casualties: Unknown** - 基础游戏
- **BepInEx** - 模组框架
- **Harmony** - 运行时补丁库

---

## 许可证

本项目使用仓库中包含的 LICENSE 文件所述的许可证。

---

*本模组与 Battlestate Games 或 Casualties: Unknown 开发者无关，也未获得其认可。所有商标归其各自所有者所有。*
