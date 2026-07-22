[Reflection.Assembly]::LoadFrom("g:\modmake\TKF_medical\scripts\bin\Debug\net48\Mono.Cecil.dll") | Out-Null

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("g:\modmake\TKF_medical\CUTarkovMedicalMod\obj\Release\net48\publicized\Assembly-CSharp.dll")

function Format-Operand($op) {
    if ($op -eq $null) { return "" }
    $typeName = $op.GetType().Name
    switch -Wildcard ($typeName) {
        "FieldDefinition" { return "$($op.DeclaringType.Name)::$($op.Name)" }
        "FieldReference" { return "$($op.DeclaringType.Name)::$($op.Name)" }
        "MethodDefinition" { return "$($op.DeclaringType.Name).$($op.Name)()" }
        "MethodReference" { return "$($op.DeclaringType.Name).$($op.Name)()" }
        "TypeDefinition" { return $op.FullName }
        "TypeReference" { return $op.FullName }
        "Instruction" { return "IL_$( '{0:X4}' -f $op.Offset)" }
        default { return $op.ToString() }
    }
}

# Check Body.Start() for item spawning
foreach ($type in $asm.MainModule.Types) {
    if ($type.Name -eq "Body") {
        foreach ($m in $type.Methods) {
            if ($m.Name -eq "Start") {
                Write-Output "=== Body.Start() ==="
                Write-Output "Locals: $(($m.Body.Variables | ForEach-Object { $_.VariableType.Name }) -join ', ')"
                foreach ($inst in $m.Body.Instructions) {
                    $opStr = Format-Operand $inst.Operand
                    Write-Output "  IL_$( '{0:X4}' -f $inst.Offset): $($inst.OpCode.Name) $opStr"
                }
            }
        }
    }
}

# Check WorldGeneration.WorldPlacePlayer
foreach ($type in $asm.MainModule.Types) {
    if ($type.Name -eq "WorldGeneration") {
        foreach ($m in $type.Methods) {
            if ($m.Name -eq "WorldPlacePlayer") {
                Write-Output "`n=== WorldGeneration.WorldPlacePlayer() ==="
                Write-Output "Locals: $(($m.Body.Variables | ForEach-Object { $_.VariableType.Name }) -join ', ')"
                foreach ($inst in $m.Body.Instructions) {
                    $opStr = Format-Operand $inst.Operand
                    Write-Output "  IL_$( '{0:X4}' -f $inst.Offset): $($inst.OpCode.Name) $opStr"
                }
            }
        }
    }
}

# Check TutorialCourse.GiveItemToBody
foreach ($type in $asm.MainModule.Types) {
    if ($type.Name -eq "TutorialCourse") {
        foreach ($m in $type.Methods) {
            if ($m.Name -eq "GiveItemToBody") {
                Write-Output "`n=== TutorialCourse.GiveItemToBody(String) ==="
                Write-Output "Params: $(($m.Parameters | ForEach-Object { $_.ParameterType.Name + ' ' + $_.Name }) -join ', ')"
                Write-Output "Locals: $(($m.Body.Variables | ForEach-Object { $_.VariableType.Name }) -join ', ')"
                foreach ($inst in $m.Body.Instructions) {
                    $opStr = Format-Operand $inst.Operand
                    Write-Output "  IL_$( '{0:X4}' -f $inst.Offset): $($inst.OpCode.Name) $opStr"
                }
            }
        }
    }
}

Write-Output "`n=== DONE ==="
