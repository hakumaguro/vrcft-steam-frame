<#
  Build the module, install it into VRCFT's CustomLibs and relaunch VRCFT through Steam.
  VRCFT must be started via Steam: launching the exe directly never spawns the module process.

  usage:  powershell -ExecutionPolicy Bypass -File scripts\deploy-module.ps1 [-NoLaunch]
#>
param(
  [switch]$NoLaunch,
  [string]$Configuration = "Release",
  [string]$SteamAppId = "3329480"      # VRCFaceTracking on Steam
)
$ErrorActionPreference = "Stop"
$root   = Split-Path -Parent $PSScriptRoot
# pick a dotnet that actually has an SDK (a runtime-only install on PATH cannot build)
$dotnet = $null
foreach ($c in @("$env:USERPROFILE\.dotnet\dotnet.exe", "dotnet")) {
  $cmd = Get-Command $c -ErrorAction SilentlyContinue
  if ($cmd -and (& $cmd.Source --list-sdks 2>$null | Select-String "^10\.")) { $dotnet = $cmd.Source; break }
}
if (-not $dotnet) { throw ".NET 10 SDK not found (winget install Microsoft.DotNet.SDK.10, or dotnet-install.ps1 -Channel 10.0)" }

& $dotnet build "$root\module" -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = "$root\module\bin\$Configuration\net10.0\SteamFrameVRCFTModule.dll"

Get-Process VRCFaceTracking* -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2

$libs = "$env:APPDATA\VRCFaceTracking\CustomLibs"
$dir  = Get-ChildItem $libs -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path "$($_.FullName)\SteamFrameVRCFTModule.dll" } | Select-Object -First 1
if (-not $dir) {
  $id = [guid]::NewGuid().ToString()
  $dir = New-Item -ItemType Directory -Force "$libs\$id"
  @{InstallationState=0; ModuleId=$id; LastUpdated=(Get-Date).ToString("s"); Version="0.1.0"; IsLocal=$true; Downloads=0; Ratings=0; Rating=0;
    AuthorName="local"; ModuleName="Steam Frame Eye Tracking"; ModuleDescription="Eye tracking for Steam Frame (frameeyeosc / Steam Link OSC)";
    UsageInstructions="See README"; DllFileName="SteamFrameVRCFTModule.dll"} | ConvertTo-Json | Set-Content "$($dir.FullName)\module.json" -Encoding utf8
}
Copy-Item $dll $dir.FullName -Force
Write-Host "installed to $($dir.FullName)"

foreach ($f in "$env:TEMP\steamframe-trace.csv","$env:TEMP\steamframe-module.log") { if (Test-Path $f) { Clear-Content $f } }
if (-not $NoLaunch) { Start-Process "steam://rungameid/$SteamAppId"; Write-Host "VRCFT launching via Steam" }
