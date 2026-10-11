# Replicate Unity's script compilation without launching the editor.
#
# Background: Unity references the Mono 4.7.1-api assemblies (NOT the .NET Framework
# reference assemblies) plus the UnityEngine assemblies under Data\Managed. Feeding those
# to Roslyn reproduces what the Unity Console would report.
#
# Use case: the user keeps the editor open, so -batchmode would fight over the project lock.
# This script is the batchmode substitute.
#
# NOTE: keep every string ASCII-only. Windows PowerShell 5.1 reads .ps1 files using the ANSI
# code page, so non-ASCII here would be mangled and can break parsing.
#
# This folder is named "Tools~" on purpose: Unity ignores directories whose name ends with "~",
# so nothing placed here can ever be picked up by the asset pipeline or the script compiler.

$ErrorActionPreference = 'Stop'

$unityData = "D:\Program Files\2022.3.62f2c1\Editor\Data"
$work      = "C:\Users\Kard\AppData\Local\Temp\omen-compile"
$csc       = "C:\Program Files\dotnet\sdk\8.0.303\Roslyn\bincore\csc.dll"

# The project root is the parent of the folder this script lives in.
$root = Split-Path -Parent $PSScriptRoot

$apiDir = Join-Path $unityData "MonoBleedingEdge\lib\mono\4.7.1-api"
if (-not (Test-Path $apiDir)) { throw "Framework reference dir not found: $apiDir" }
if (-not (Test-Path $csc))    { throw "Roslyn compiler not found: $csc" }

if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

# ---- references ----
$refs = New-Object System.Collections.Generic.List[string]

foreach ($d in @($apiDir, (Join-Path $apiDir "Facades"))) {
    if (Test-Path $d) {
        Get-ChildItem $d -Filter *.dll | ForEach-Object { $refs.Add($_.FullName) }
    }
}

# UnityEngine's real types live in the per-module assemblies (UnityEngine.CoreModule etc).
# Do NOT add Data\Managed\UnityEngine.dll: it is a legacy facade that re-declares those same
# types and triggers CS0433 "type exists in both" for every attribute.
foreach ($f in (Get-ChildItem (Join-Path $unityData "Managed\UnityEngine") -Filter *.dll)) { $refs.Add($f.FullName) }

# ---- sources ----
$assetsDir = Join-Path $root "Assets"
$sources = Get-ChildItem -Path $assetsDir -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\Editor\\' } |
    Sort-Object FullName

Write-Host "Framework references : $($refs.Count) assemblies"
Write-Host "Source files         : $($sources.Count)"
Write-Host ""

# ---- response file ----
$defines = @(
    'UNITY_2022_3_62','UNITY_2022_3','UNITY_2022','UNITY_5_3_OR_NEWER','UNITY_2022_3_OR_NEWER',
    'UNITY_EDITOR','UNITY_EDITOR_64','UNITY_EDITOR_WIN',
    'PLATFORM_STANDALONE','PLATFORM_STANDALONE_WIN','UNITY_STANDALONE','UNITY_STANDALONE_WIN',
    'ENABLE_SPRITES','ENABLE_TILEMAP','ENABLE_LEGACY_INPUT_MANAGER',
    'ENABLE_MONO','NET_STANDARD_2_1','NETSTANDARD','NETSTANDARD2_1',
    'DEBUG','TRACE','UNITY_ASSERTIONS','CSHARP_7_OR_LATER','CSHARP_7_3_OR_NEWER'
) -join ';'

$rsp = New-Object System.Collections.Generic.List[string]
$rsp.Add('/nologo')
$rsp.Add('/target:library')
$rsp.Add('/langversion:9.0')
$rsp.Add('/nostdlib+')
$rsp.Add('/warn:4')
# 0169 unused field, USG0001 Unity source generator, 0649 field never assigned (Inspector-driven serialization)
$rsp.Add('/nowarn:0169;USG0001;0649')
$rsp.Add('/define:' + $defines)
$rsp.Add('/out:' + (Join-Path $work "Assembly-CSharp.dll"))

foreach ($r in $refs)    { $rsp.Add('/reference:"' + $r + '"') }
foreach ($s in $sources) { $rsp.Add('"' + $s.FullName + '"') }

$rspPath = Join-Path $work "csc.rsp"
[System.IO.File]::WriteAllLines($rspPath, $rsp, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Response file : $rspPath"
Write-Host "--- compiling ---"
Write-Host ""

$output = & dotnet $csc "@$rspPath" 2>&1
$code = $LASTEXITCODE
$output | ForEach-Object { $_ }

Write-Host ""
if ($code -eq 0) {
    Write-Host "RESULT: COMPILE OK (0 errors)"
} else {
    Write-Host "RESULT: COMPILE FAILED (exit $code)"
}
exit $code
