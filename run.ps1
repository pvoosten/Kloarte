<#
.SYNOPSIS
    Builds the current code and runs Kloarte (the show scene) from the repository, with its MCP server.

.DESCRIPTION
    1. dotnet build Kloarte.sln (skipped with -NoBuild). Close a running Kloarte first: it locks the host's assemblies.
    2. Starts Godot on src\Kloarte.Host with the app's show scene (res://shows/risquons_tout.tscn, the scene the exported
       build starts). Run from the repository, the app looks for projects in res://Projects; -Project opens one from
       anywhere: a folder, or a name in Documents\Kloarte (where the exported app keeps its projects).
    The MCP server listens on http://localhost:8462/mcp (-McpPort, -NoMcp). The console window shows the app's log.

.EXAMPLE
    .\run.ps1 -Project Kruimels
    .\run.ps1 -Project C:\shows\Kruimels -McpPort 8470
    .\run.ps1 -NoBuild -NoMcp
#>
param(
    # The project to open at start: a folder, or the name of a project in Documents\Kloarte.
    [string]$Project,
    # The Godot .NET console executable (also from the GODOT environment variable).
    [string]$Godot = $(if ($env:GODOT) { $env:GODOT } else {
        "c:\tools\godot\Godot_v4.7.2-stable_mono_win64_console.exe" }),
    # The MCP server's port (default 8462).
    [int]$McpPort = 0,
    # No MCP server.
    [switch]$NoMcp,
    # Run without building first.
    [switch]$NoBuild,
    # More arguments for the app (after --), e.g. --magicq.
    [string[]]$AppArgs = @()
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

if (-not (Test-Path $Godot)) { throw "Godot not found: $Godot (pass -Godot or set GODOT)" }

if (-not $NoBuild) {
    dotnet build Kloarte.sln
    if ($LASTEXITCODE -ne 0) { throw "Build failed (is Kloarte still running? It locks the host's assemblies)" }
}

$userArgs = @() + $AppArgs
if ($Project) {
    $folder = if (Test-Path $Project -PathType Container) { $Project }
              else { Join-Path ([Environment]::GetFolderPath("MyDocuments")) "Kloarte\$Project" }
    if (-not (Test-Path $folder -PathType Container)) { throw "No project folder $folder" }
    $userArgs += "--project=$((Resolve-Path $folder).Path)"
}
if ($NoMcp) { $userArgs += "--no-mcp" }
elseif ($McpPort -gt 0) { $userArgs += "--mcp-port=$McpPort" }

$godotArgs = @("--path", (Join-Path $root "src\Kloarte.Host"), "res://shows/risquons_tout.tscn")
if ($userArgs.Count -gt 0) { $godotArgs += "--"; $godotArgs += $userArgs }
Write-Host "$Godot $($godotArgs -join ' ')"
& $Godot @godotArgs
