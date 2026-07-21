Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$pluginDir = "F:/SteamLibrary/steamapps/common/Casualties Unknown Demo/BepInEx/plugins/CUTarkovMedicalMod"
$outDir = "G:/modmake/mod存档"
if (!(Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$zipPath = Join-Path $outDir "CUTarkovMedicalMod_v0.3.0.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
$count = 0

# DLL at root
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$pluginDir/CUTarkovMedicalMod.dll", "CUTarkovMedicalMod.dll")
$count++

# Framework/Assets/ at root
$assetsDir = Join-Path $pluginDir "Framework/Assets"
if (Test-Path $assetsDir) {
    Get-ChildItem $assetsDir -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($pluginDir.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $rel)
        $count++
    }
}

# Lang/ at root
$langDir = Join-Path $pluginDir "Lang"
if (Test-Path $langDir) {
    Get-ChildItem $langDir -File | ForEach-Object {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, "Lang/$($_.Name)")
        $count++
    }
}

$zip.Dispose()
$size = (Get-Item $zipPath).Length / 1MB
Write-Output "Created: $zipPath"
Write-Output "  Files: $count"
Write-Output "  Size:  $('{0:N2}' -f $size) MB"
