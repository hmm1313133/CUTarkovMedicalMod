[Reflection.Assembly]::LoadFrom("g:\modmake\TKF_medical\scripts\bin\Debug\net48\Mono.Cecil.dll") | Out-Null

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("g:\modmake\TKF_medical\CUTarkovMedicalMod\obj\Release\net48\publicized\Assembly-CSharp.dll")

# Search for string literals containing painkiller, bandage, bruisekit in all methods
foreach ($type in $asm.MainModule.Types) {
    foreach ($m in $type.Methods) {
        if ($m.Body -eq $null) { continue }
        foreach ($inst in $m.Body.Instructions) {
            if ($inst.OpCode.Name -eq "ldstr" -and $inst.Operand -is [string]) {
                $s = [string]$inst.Operand
                if ($s -match "painkiller|bandage|bruisekit|plaster|adhesive" -and $s.Length -lt 50) {
                    Write-Output "$($type.Name).$($m.Name): '$s'"
                }
            }
        }
    }
}

# Check WorldPlacePlayer state machine MoveNext for item spawning
foreach ($type in $asm.MainModule.Types) {
    if ($type.Name -match "WorldPlacePlayer") {
        Write-Output "`n=== $($type.Name) ==="
        Write-Output "Fields: $(($type.Fields | ForEach-Object { $_.Name }) -join ', ')"
        foreach ($m in $type.Methods) {
            if ($m.Name -eq "MoveNext" -and $m.Body -ne $null) {
                foreach ($inst in $m.Body.Instructions) {
                    if ($inst.OpCode.Name -eq "ldstr" -and $inst.Operand -is [string]) {
                        Write-Output "  String: '$($inst.Operand)'"
                    }
                    if ($inst.Operand -and $inst.Operand.GetType().Name -match "Method") {
                        $mName = $inst.Operand.Name
                        if ($mName -match "Item|Spawn|Create|Give|Drop|Loot|Place|Resources") {
                            Write-Output "  Call: $($inst.OpCode.Name) $($inst.Operand.DeclaringType.Name).$mName()"
                        }
                    }
                }
            }
        }
    }
}

# Check TutorialCourse GiveItemToBody state machine
foreach ($type in $asm.MainModule.Types) {
    if ($type.Name -match "GiveItemToBody") {
        Write-Output "`n=== $($type.Name) ==="
        Write-Output "Fields: $(($type.Fields | ForEach-Object { $_.Name }) -join ', ')"
        foreach ($m in $type.Methods) {
            if ($m.Name -eq "MoveNext" -and $m.Body -ne $null) {
                foreach ($inst in $m.Body.Instructions) {
                    if ($inst.OpCode.Name -eq "ldstr" -and $inst.Operand -is [string]) {
                        Write-Output "  String: '$($inst.Operand)'"
                    }
                    if ($inst.Operand -and $inst.Operand.GetType().Name -match "Method") {
                        $mName = $inst.Operand.Name
                        if ($mName -match "Item|Spawn|Create|Give|Drop|Loot|Place|Resources|Instantiate") {
                            Write-Output "  Call: $($inst.OpCode.Name) $($inst.Operand.DeclaringType.Name).$mName()"
                        }
                    }
                }
            }
        }
    }
}

Write-Output "`n=== DONE ==="
