# Casualties: Unknown - Tarkov-Style Medical Mod

**Version: 0.1.0**

A BepInEx mod for **Casualties: Unknown Demo** that brings the complete Escape from Tarkov medical system into the game — 16 combat stimulant injectors, 12 medical items, and a fully integrated medical framework.

Each stimulant has its own unique buff/side-effect mechanics, registered into the game's native item table via reflection. Medical kits hook into native minigame systems (BandageMinigame, SyringeMinigame, ShrapnelMinigame, DislocationMinigame).

---

## Requirements

| Requirement | Details |
|-------------|---------|
| **Game** | Casualties: Unknown Demo (Steam) |
| **Mod Loader** | BepInEx 5.x (BEPUEx or BepInExPack) |

### Installing BepInEx

If you haven't installed BepInEx yet:
1. Download [BepInEx 5.x stable](https://github.com/BepInEx/BepInEx/releases) (x64)
2. Extract the contents into your game root folder (`Steam/steamapps/common/Casualties Unknown Demo/`)
3. Launch the game once to generate the `BepInEx/` folder structure
4. Close the game

---

## Installation

1. Download the mod archive (`CUTarkovMedicalMod_v0.1.0.zip`)
2. Extract the **contents** of the zip into your game root folder
   - The zip contains: `BepInEx/plugins/CUTarkovMedicalMod/...`
   - After extraction, the structure should look like:
     ```
     [Game Root]/
     └── BepInEx/
         └── plugins/
             └── CUTarkovMedicalMod/
                 ├── CUTarkovMedicalMod.dll
                 ├── Framework/
                 │   └── Assets/        (icons + sound effects)
                 └── Lang/              (EN.json, zh_CN.json)
     ```
3. Launch the game

### Verifying Installation

Check `BepInEx/LogOutput.log` for:
```
Casualties: Unknown - Tarkov-Style Medical Mod loaded. Enabled=True
Medical content source: ...
Catalog item count: 38
```

If you see errors in the log, see the **Bug Reports** section below.

---

## Features

- **16 custom stimulants** — each with unique ItemKey, ItemInfo, useAction delegate, and effect controller
- **12 medical items** — first aid kits (BandageMinigame), surgical kits (auto-detection), balms/pills (liquid system)
- **Starting loadout** — fixed 5 medical items (Grizzly/AFAK/IFAK/Salewa/AI-2) + random medical items
- **World loot** — random medical items (including stims and drugs) spawn in the world
- **Container drops** — medical crates, supply crates, and corpses drop stims/drugs by probability
- **Buff indicator** — native MoodleManager displays stim effect icons and countdown timers
- **Tunnel vision** — URP Vignette post-processing for dark vignette overlay (triggered by some stim side effects)
- **Injector sound** — custom `med_stimulator_use.wav` sound effect on injection
- **Hover descriptions** — hold **SHIFT** to expand item effect details; release to show only brief lore text
- **Console spawn** — console `spawn` command supports all custom item IDs
- **Multiplayer compatible** — auto-detects KrokMP, enters safe mode (starting loadout only) when detected
- **Bilingual support** — English and Simplified Chinese

---

## 16 Stimulants

| # | Stimulant | ItemKey | Role | Buff | Side Effect | Duration |
|---|-----------|---------|------|------|-------------|----------|
| 1 | **eTG-c** | `etg_c` | Regenerative | +2 muscle/s per limb, blood volume +50ml/s to 5L | 20s: -1 hunger/hydration/s, chest pain +40 | 60s + 20s debuff |
| 2 | **Zagustin** | `zagustin` | Hemostatic | Long bleeding prevention | Hydration drain, tremors | 150s |
| 3 | **Morphine** | `cu_morphine` | Painkiller | Native Painkillers (opiateAmount=35) | One-time -10 hunger / -15 hydration; extreme opioid — fatal if untreated! | ~300s |
| 4 | **SJ12** | `sj12` | Thermoregulation | Body temp → 31.5°C, +2 resilience, +0.2 hunger/hydration/s | Body temp → 40.5°C overheating | 600s buff + 180s debuff |
| 5 | **M.U.L.E.** | `mule` | Carry weight | +50% carry capacity (Harmony Postfix) | -0.1 muscle/s per limb, consciousness capped at 90% | 900s |
| 6 | **Propital** | `propital` | Regenerative | +0.1 muscle/s +0.1 skin/s, opiate +20 | +10 sickness, delayed STR/RES -2, tunnel vision + tremors | 900s + 300s debuff |
| 7 | **SJ1** | `sj1` | Attribute boost | STR +5, RES +3, +30% stamina recovery | +10 sickness, -0.1 hunger/hydration/s | 300s |
| 8 | **SJ6** | `sj6` | Stamina boost | +20% stamina cap, +120% stamina recovery | +25 sickness, delayed tunnel vision + tremors | 900s + 300s debuff |
| 9 | **SJ9** | `sj9` | Temp suppressant | Body temp locked at 31°C | RES -2, delayed chest pain + muscle damage | 1200s + 600s debuff |
| 10 | **PNB** | `pnb` | Muscle repair | +0.2 muscle/s per limb (2min), RES +3 (5min) | Delayed STR -1, tremors 60s | 120s + 300s |
| 11 | **Obdolbos** | `obdolbos` | Gamble cocktail | Random one of 8 effects (including instant death) | Varies each time | Random |
| 12 | **Obdolbos 2** | `obdolbos2` | Permanent boost | Permanent STR/RES/INT +6, carry weight +3u | -30% stamina recovery, -20% stamina cap for 40min | Permanent + 300s debuff |
| 13 | **Blue Blood** | `blueblood` | Artificial blood / Antidote | Stop bleeding 120s, toxin -70%, radiation -10Gy | Delayed immunity -40%, 33% vomiting | 120s + 60s debuff |
| 14 | **xTG-12** | `xtg12` | Antidote | +70% resistance, toxin -100% | 20% vomiting, delayed tremors | 300s + 60s debuff |
| 15 | **Mildronate** | `mildronate` | Cardiac protection | Fibrillation -20%, +10% stamina cap, +50% recovery | -0.1 hunger/hydration/s | 1500s + 900s debuff |
| 16 | **2A2-(b-TG)** | `2a2btg` | Carry weight | +7u carry capacity, mood +5 | -0.1 hydration/s | 1200s + 900s debuff |

---

## 12 Medical Items

| # | Item | ItemKey | Usage | Effect | Side Effect |
|---|------|---------|-------|--------|-------------|
| 1 | **AI-2** | `ai2` | SyringeMinigame (100ml, 10ml/use) | Radiation -1Gy, opiate +0.2, internal bleeding -8% (per 10ml) | +3 sickness, -10% immunity, -1 hunger/hydration |
| 2 | **Grizzly** | `grizzlykit` | BandageMinigame | Major hemostasis + fracture/dislocation recovery + skin/muscle recovery + disinfection. Very high durability | Heavy (3u) |
| 3 | **AFAK** | `afak` | BandageMinigame | Moderate hemostasis + fracture/dislocation/skin recovery. High durability | — |
| 4 | **IFAK** | `ifak` | BandageMinigame | Moderate hemostasis + fracture/dislocation/skin recovery. Medium durability | — |
| 5 | **Salewa** | `salewa` | BandageMinigame | Moderate hemostasis. Very high durability | — |
| 6 | **Salewa (Thermal)** | `salewa` | Auto-trigger on chest when temp < 30°C | Bandage turns khaki, body temp rises to 36°C | — |
| 7 | **CMS Kit** | `cms` | Auto-detect on limb | Shrapnel removal / dislocation reduction / 50% fracture recovery (+50 pain) | — |
| 8 | **Surv12 Kit** | `multitool` | Auto-detect on limb | Shrapnel removal / dislocation reduction / 90% fracture recovery (+30 pain). Very high durability | Slightly heavy |
| 9 | **Golden Star** | `goldenstar` | Apply to limb (10ml, 2ml/use) | Disinfect 30s, delayed pain relief 15s (pain to 10%) | Skin -5, mood -5, consciousness reduced 10s |
| 10 | **Vaseline** | `vaseline` | Apply to limb (10ml, 2ml/use) | Dirt -2, skin +5. On hands: claw +10 | — |
| 11 | **Augmentin** | `libatine` | Drink from inventory (2ml, 1 use) | +80% resistance 5min, infection to 60% in 1min | Mood -3, 5/7/10min 5% vomiting each |
| 12 | **Ibuprofen** | `ibuprofen` | Drink from inventory (10ml, 2ml/use) | +50% resistance / infection to 15% / temp -2°C / pain relief / +20% stamina recovery 7min | Mood -3, 7/10min 10% vomiting; **second dose within 10min triggers overdose — can be fatal!** |

---

## Configuration

The mod auto-generates a config file at `BepInEx/config/com.yourname.cu.tarkovmedicalmod.cfg`.

| Category | Option | Default | Description |
|----------|--------|---------|-------------|
| General | `EnableMod` | `true` | Master toggle |
| General | `FeatureMode` | `Both` | Disabled / StartingLoadoutOnly / WorldLootOnly / Both |
| Compatibility | `CompatibilityMode` | `AutoSafe` | KrokMP detection strategy |
| Content | `UseExternalContentFile` | `true` | Load item definitions from JSON |
| Content | `AutoCreateContentFile` | `true` | Auto-create JSON if missing |
| StartingLoadout | `MinItems` / `MaxItems` | `1` / `3` | Random medical item count range (fixed 5 items excluded) |
| WorldLoot | `MinItems` / `MaxItems` | `1` / `4` | World loot item count range |
| Distribution | `AllowDuplicateItems` | `true` | Allow duplicate items |
| Distribution | `Seed` | `0` | Random seed (0 = random) |
| Debug | `LogGeneratedPlans` | `true` | Log distribution plans |

---

## Controls

| Key | Function |
|-----|----------|
| **Left Click** (item in inventory) | Use stimulant / drink liquid medicine |
| **Left Click** (on body limb) | Use medical kit / surgical kit / balm |
| **Hold SHIFT** | Expand hover description to show full effect details |
| **F7** / **Numpad 7** | Debug: output runtime status (mod init, mode, KrokMP, held item) |

---

## Console Commands

All custom items can be spawned via the developer console:

```
spawn etg_c
spawn cu_morphine
spawn grizzlykit
spawn cms
...
```

Use `spawn` followed by any ItemKey from the tables above.

---

## Container Drop Rates

| Container Type | Stim Drop | Medical Item Drop |
|----------------|-----------|-------------------|
| Medical Crate (`medcrate`) | 17% → 1-2 stims | 20% → 1-2 items |
| Supply Crate (`containercrate`) | — | 15% → 1-3 items |
| Corpse (`corpse`) | — | 10% → 1 item |

---

## Compatibility

- **KrokMP (Multiplayer)**: Auto-detected. In safe mode, only starting loadout is enabled to prevent desync.
- **Other BepInEx mods**: Should be compatible. The mod uses Harmony patches with unique GUID `com.yourname.cu.tarkovmedicalmod`.

---

## Uninstallation

Delete the `BepInEx/plugins/CUTarkovMedicalMod/` folder.

---

## Source Code & GitHub

This mod is open source! The full source code is available on GitHub:

**[https://github.com/hmm1313133/CUTarkovMedicalMod](https://github.com/hmm1313133/CUTarkovMedicalMod)**

---

## Bug Reports & Feedback

Found a bug? Have a suggestion? Please report it on GitHub Issues:

**[https://github.com/hmm1313133/CUTarkovMedicalMod/issues](https://github.com/hmm1313133/CUTarkovMedicalMod/issues)**

When filing a bug report, please include:

1. **Mod version** (currently 0.1.0)
2. **Game version** (check the game's main menu)
3. **BepInEx version**
4. **Other installed mods** (if any)
5. **Steps to reproduce** the bug
6. **The log file** — attach `BepInEx/LogOutput.log` (or paste relevant error lines)
7. **Screenshots** (if applicable)

> Before reporting, please check existing issues to avoid duplicates.

---

## Credits

- **Escape from Tarkov** by Battlestate Games — original item designs, descriptions, and mechanics inspiration
- **Casualties: Unknown** — the base game
- **BepInEx** — modding framework
- **Harmony** — runtime patching library

---

## License

This project is licensed under the terms of the LICENSE file included in the repository.

---

*This mod is not affiliated with or endorsed by Battlestate Games or the developers of Casualties: Unknown. All trademarks belong to their respective owners.*
