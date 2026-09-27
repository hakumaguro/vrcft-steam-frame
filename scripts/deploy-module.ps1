<#
  Developer shortcut: build the module, copy it over the installed one and relaunch VRCFT through Steam.
  Normal users: use "Start Here.cmd" or scripts\setup.ps1 instead.

  usage:  powershell -ExecutionPolicy Bypass -File scripts\deploy-module.ps1 [-NoLaunch]
#>
param([switch]$NoLaunch, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\common.ps1"
$root = Split-Path -Parent $PSScriptRoot

$dotnet = Find-DotnetSdk10
if (-not $dotnet) { throw ".NET 10 SDK not found (setup.ps1 can install one per-user)" }
$vrcft = Find-VrcftDir
if (-not $vrcft) { throw "VRCFaceTracking not found" }

& $dotnet build "$root\module" -c $Configuration --nologo -v q "-p:VrcftDir=$vrcft"
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = "$root\module\bin\$Configuration\net10.0\$script:ModuleDll"

$ours = Get-OurModule (Get-VrcftDataDir)
if (-not $ours) { throw "module not installed yet; run scripts\setup.ps1 once" }
[void](Stop-Vrcft)
Copy-Item $dll $ours.Dir -Force
Write-Host "installed to $($ours.Dir)"
if (-not $NoLaunch) { Start-VrcftViaSteam; Write-Host "VRCFT launching via Steam" }
