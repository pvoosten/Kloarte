<#
.SYNOPSIS
    Exports Kloarte (Godot .NET, preset "Windows Desktop") and zips it for distribution.

.DESCRIPTION
    1. Checks the Godot export templates of the Godot version used (-InstallTemplates downloads them from the
       official Godot release on GitHub, about 1 GB, when they are missing).
    2. Exports a release build into build\Kloarte (Kloarte.exe, Kloarte.console.exe, Kloarte.pck, the .NET
       assemblies). The exported build starts the show scene (project setting run/main_scene.dist).
    3. Copies the read-only app files the build reads from next to the exe (AppPaths.File): data\ (catalogue, GDTF,
       colour table, schemas), venue\*.yaml, the built-in gobos (gobos\*.png), shows\*.fixtures.json.
    4. Zips build\Kloarte into dist\Kloarte-<yyyy-MM-dd>.zip.
    Project data (mechanics, blocking, gobos made in the editor) is not shipped: the exported app keeps it in
    Documents\Kloarte.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\make_dist.ps1
    powershell -ExecutionPolicy Bypass -File tools\make_dist.ps1 -InstallTemplates
#>
param(
    # The Godot .NET console executable (also from the GODOT environment variable).
    [string]$Godot = $(if ($env:GODOT) { $env:GODOT } else {
        "$env:USERPROFILE\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" }),
    # Download and install the export templates when they are missing.
    [switch]$InstallTemplates,
    # Folder of the zip.
    [string]$OutDir = "dist"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
# The Godot project (the host); build\ and dist\ are in the repository root, outside it.
$project = Join-Path $root "src\Kloarte.Host"
Set-Location $root

if (-not (Test-Path $Godot)) { throw "Godot not found: $Godot (pass -Godot or set GODOT)" }

# --- 1. Export templates -----------------------------------------------------------------------------------------
# "4.7.2.stable.mono.official.ed1daf0bf" -> templates folder "4.7.2.stable.mono", release tag "4.7.2-stable".
$version = (& $Godot --version | Select-Object -Last 1).Trim()
if ($version -notmatch '^(\d+\.\d+(?:\.\d+)?)\.(\w+)\.mono') { throw "Not a Godot .NET build: $version" }
$number = $Matches[1]; $status = $Matches[2]
$templates = Join-Path $env:APPDATA "Godot\export_templates\$number.$status.mono"
if (-not (Test-Path (Join-Path $templates "windows_release_x86_64.exe"))) {
    if (-not $InstallTemplates) {
        throw "Export templates for Godot $number.$status.mono are missing ($templates). Install them in the editor " +
              "(Editor > Manage Export Templates) or run this script with -InstallTemplates."
    }
    $url = "https://github.com/godotengine/godot/releases/download/$number-$status/Godot_v$number-${status}_mono_export_templates.tpz"
    $tpz = Join-Path $env:TEMP "Godot_v$number-${status}_mono_export_templates.zip"
    Write-Host "Downloading $url ..."
    $ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar
    Invoke-WebRequest -Uri $url -OutFile $tpz
    $unpacked = Join-Path $env:TEMP "godot_templates_$number"
    if (Test-Path $unpacked) { Remove-Item -Recurse -Force $unpacked }
    Expand-Archive -Path $tpz -DestinationPath $unpacked
    New-Item -ItemType Directory -Force $templates | Out-Null
    Copy-Item -Path (Join-Path $unpacked "templates\*") -Destination $templates -Recurse -Force
    Remove-Item -Recurse -Force $unpacked
    Remove-Item -Force $tpz
}

# --- 2. Export ---------------------------------------------------------------------------------------------------
$build = Join-Path $root "build\Kloarte"
if (Test-Path $build) { Remove-Item -Recurse -Force $build }
New-Item -ItemType Directory -Force $build | Out-Null
& $Godot --headless --path $project --export-release "Windows Desktop" (Join-Path $build "Kloarte.exe")
if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $build "Kloarte.exe"))) { throw "Godot export failed ($LASTEXITCODE)" }
# Godot can write Kloarte.exe and still fail its .NET build ("Failed to build project"), and then the export has no
# assemblies: the exe would start with nothing to run. Check that the libraries came along.
$dotnetFolder = Get-ChildItem -Path $build -Directory -Filter "data_*" | Select-Object -First 1
foreach ($dll in @("Kloarte.Host.dll", "Kloarte.GodotBinding.dll", "GodotSharp.dll")) {
    if (-not $dotnetFolder -or -not (Test-Path (Join-Path $dotnetFolder.FullName $dll))) {
        throw "Godot export has no .NET assembly $dll (the .NET build failed; see the MSBuild output above)"
    }
}

# --- 3. Read-only app files next to the exe (AppPaths.File) ------------------------------------------------------
Copy-Item -Recurse (Join-Path $project "data") (Join-Path $build "data")
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $build "data\.gdignore")
foreach ($pattern in @("venue\*.yaml", "gobos\*.png", "shows\*.fixtures.json")) {
    $folder = Join-Path $build (Split-Path -Parent $pattern)
    New-Item -ItemType Directory -Force $folder | Out-Null
    Copy-Item (Join-Path $project $pattern) $folder
}

# --- 4. Zip ------------------------------------------------------------------------------------------------------
$dist = Join-Path $root $OutDir
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist ("Kloarte-{0:yyyy-MM-dd}.zip" -f (Get-Date))
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $build -DestinationPath $zip
Write-Host "Distribution: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
