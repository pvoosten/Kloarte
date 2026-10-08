<#
.SYNOPSIS
    Renders the boot splash (src\Kloarte.Host\splash.png) with Kloarte itself: five actors in coloured light and haze, the
    words of the title on their shirts and under them (src\Kloarte.Host\render\SplashRender.cs).
.DESCRIPTION
    Run after changing the look of the actors, lamps or haze, or to get other colours (-Seed). Needs a window (it renders
    for real, about 5 s), so not headless. Build first: dotnet build Kloarte.sln.
#>
param(
    # The Godot .NET console executable (also from the GODOT environment variable).
    [string]$Godot = $(if ($env:GODOT) { $env:GODOT } else {
        "$env:USERPROFILE\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" }),
    # Seed of the random gel colours.
    [int]$Seed = 11
)
$ErrorActionPreference = "Stop"
$project = Join-Path (Split-Path -Parent $PSScriptRoot) "src\Kloarte.Host"
$out = Join-Path $project "splash.png"
if (-not (Test-Path $Godot)) { throw "Godot not found: $Godot (pass -Godot or set GODOT)" }
$log = & $Godot --path $project --resolution 1280x720 res://render/splash_render.tscn -- "--out=$out" "--seed=$Seed" | Out-String
if ($log -notmatch "SPLASH saved") { throw "The splash was not rendered:`n$log" }
Write-Host "Splash: $out"
