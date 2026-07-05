# Casualties: Unknown - Tarkov-Style Medical Mod

This is a BepInEx mod project scaffold for **Casualties: Unknown Demo**.

## Chinese name to English

- Original: `未知伤亡(Casualties: Unknown)：塔科夫医疗模组`
- Suggested English: `Casualties: Unknown - Tarkov-Style Medical Mod`

## Project layout

- `CUTarkovMedicalMod/CUTarkovMedicalMod.csproj`: mod project file
- `CUTarkovMedicalMod/Plugin.cs`: BepInEx plugin entry point
- `vars.targets`: local game path and output folder mapping

## Build and deploy flow (BepInEx)

1. Build the project.
2. MSBuild copies `CUTarkovMedicalMod.dll` into:
   `O:/SteamLibrary/steamapps/common/Casualties Unknown Demo/BepInEx/plugins/CUTarkovMedicalMod`
3. Launch the game.
4. Check `BepInEx/LogOutput.log` for plugin load logs.

## Requirements

- .NET SDK (for building)
- BepInEx already installed in the game folder (detected in your path)

## Quick build

```powershell
Set-Location "I:\CasualtiesUnknownTarkovMedicalMod"
dotnet build .\CUTarkovMedicalMod\CUTarkovMedicalMod.csproj -c Debug
```
