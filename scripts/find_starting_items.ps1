[Reflection.Assembly]::LoadFrom("g:\modmake\TKF_medical\scripts\bin\Debug\net48\Mono.Cecil.dll") | Out-Null

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("g:\modmake\TKF_medical\CUTarkovMedicalMod\obj\Release\net48\publicized\Assembly-CSharp.dll")

# Search for methods related to starting items, loadout, grant
foreach ($type in $asm.MainModule.Types) {
    foreach ($m in $type.Methods) {
        if ($m.Name -match "StartingItem|GrantItem|GrantStart|SpawnStart|StartingLoadout|GiveItem|GiveStart" -and $m.Name -notmatch "get_|set_") {
            Write-Output "$($type.Name).$($m.Name)($(($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ', '))"
        }
    }
}

# Also search Body for methods that spawn items
foreach ($type in $asm.MainModule.Types) {
    if ($type.Name -eq "Body") {
        Write-Output "`n=== Body methods ==="
        foreach ($m in $type.Methods) {
            if ($m.Name -match "Item|Spawn|Grant|Start|Loot|Give" -and $m.Name -notmatch "get_|set_") {
                Write-Output "  $($m.Name)($(($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ', '))"
            }
        }
    }
}

# Search WorldGeneration for item spawning
foreach ($type in $asm.MainModule.Types) {
    if ($type.Name -eq "WorldGeneration") {
        Write-Output "`n=== WorldGeneration methods ==="
        foreach ($m in $type.Methods) {
            if ($m.Name -match "Item|Spawn|Grant|Start|Loot|Give|Player" -and $m.Name -notmatch "get_|set_") {
                Write-Output "  $($m.Name)($(($m.Parameters | ForEach-Object { $_.ParameterType.Name }) -join ', '))"
            }
        }
    }
}

Write-Output "`n=== DONE ==="
