# Shared helpers for setup.ps1 / doctor.ps1 / deploy-module.ps1. Dot-source it: . "$PSScriptRoot\common.ps1"
# Windows PowerShell 5.1 compatible (no ternaries, no ??).

$script:SteamAppId = "3329480"     # VRCFaceTracking on Steam
$script:ModuleDll = "SteamFrameVRCFTModule.dll"
$script:StockDll = "SteamLinkVRCFTModule.dll"

function Get-VrcftDataDir {
  param([string]$Override)
  if ($Override) { return $Override }
  return (Join-Path $env:APPDATA "VRCFaceTracking")
}

# VRCFaceTracking install folder: Steam uninstall key, then Steam library folders, then a running process.
function Find-VrcftDir {
  foreach ($k in @("HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App $script:SteamAppId",
                   "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App $script:SteamAppId")) {
    $p = (Get-ItemProperty $k -ErrorAction SilentlyContinue).InstallLocation
    if ($p -and (Test-Path (Join-Path $p "VRCFaceTracking.exe"))) { return $p.TrimEnd('\') }
  }
  $steam = Get-SteamDir
  if ($steam) {
    $libs = @($steam)
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
      foreach ($m in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"')) { $libs += ($m.Matches[0].Groups[1].Value -replace '\\\\', '\') }
    }
    foreach ($l in $libs) {
      $c = Join-Path $l "steamapps\common\VRCFaceTracking"
      if (Test-Path (Join-Path $c "VRCFaceTracking.exe")) { return $c }
    }
  }
  $proc = Get-Process VRCFaceTracking -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($proc -and $proc.Path) { return (Split-Path $proc.Path) }
  return $null
}

function Get-SteamDir {
  $s = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
  if ($s -and (Test-Path $s)) { return ($s -replace '/', '\') }
  foreach ($c in @("${env:ProgramFiles(x86)}\Steam", "$env:ProgramFiles\Steam")) { if (Test-Path $c) { return $c } }
  return $null
}

# All installed VRCFT module folders as objects: Dir, Dll, Name, Id
function Get-InstalledModules {
  param([string]$DataDir)
  $libs = Join-Path $DataDir "CustomLibs"
  $out = @()
  foreach ($d in (Get-ChildItem $libs -Directory -ErrorAction SilentlyContinue)) {
    $mj = Join-Path $d.FullName "module.json"
    $dll = $null; $name = $d.Name
    if (Test-Path $mj) {
      try { $j = Get-Content $mj -Raw | ConvertFrom-Json; $dll = $j.DllFileName; if ($j.ModuleName) { $name = $j.ModuleName } } catch {}
    }
    if (-not $dll) { $f = Get-ChildItem $d.FullName -Filter *.dll -ErrorAction SilentlyContinue | Select-Object -First 1; if ($f) { $dll = $f.Name } }
    $out += [pscustomobject]@{ Dir = $d.FullName; Dll = $dll; Name = $name; Id = $d.Name }
  }
  return $out
}

function Get-OurModule { param([string]$DataDir) Get-InstalledModules $DataDir | Where-Object { $_.Dll -eq $script:ModuleDll } | Select-Object -First 1 }
function Get-StockSteamLinkModules { param([string]$DataDir) Get-InstalledModules $DataDir | Where-Object { $_.Dll -eq $script:StockDll } }

function Get-SteamVrSettingsPath {
  $steam = Get-SteamDir
  if (-not $steam) { return $null }
  return (Join-Path $steam "config\steamvr.vrsettings")
}

# Read the two Steam Link driver values that matter. Returns @{Port=..; ShareEye=..} (nulls when unset)
function Get-VrlinkSettings {
  param([string]$Path)
  $r = @{ Port = $null; ShareEye = $null; Ok = $false }
  if (-not $Path -or -not (Test-Path $Path)) { return $r }
  try {
    $j = Get-Content $Path -Raw | ConvertFrom-Json
    if ($j.driver_vrlink) { $r.Port = $j.driver_vrlink.OSCOutPort; $r.ShareEye = $j.driver_vrlink.shareEyeTrackingData }
  } catch {}
  $r.Ok = ("$($r.Port)" -eq "9015" -and $r.ShareEye -eq $true)
  return $r
}

function Test-SteamVrRunning { return [bool](Get-Process vrserver, vrmonitor, vrcompositor -ErrorAction SilentlyContinue) }

# VRCFT's own OSC output port (must be 9000 for VRChat). Stored as a string in LocalSettings.json.
function Get-VrcftLocalSettingsPath { param([string]$DataDir) Join-Path $DataDir "VRCFaceTracking\ApplicationData\LocalSettings.json" }
function Get-VrcftOscOutPort {
  param([string]$DataDir)
  $p = Get-VrcftLocalSettingsPath $DataDir
  if (-not (Test-Path $p)) { return $null }
  $m = Select-String -Path $p -Pattern '"OSCOutPort"\s*:\s*"?(\d+)"?' | Select-Object -First 1
  if ($m) { return $m.Matches[0].Groups[1].Value }
  return $null
}

# Inbound firewall state for the module process: "ok", "blocked", "missing" (+ which network categories are uncovered)
function Test-ModuleFirewall {
  param([string]$VrcftDir)
  $exe = Join-Path $VrcftDir "VRCFaceTracking.ModuleProcess.exe"
  $rules = @(Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue | Where-Object { $_.Program -ieq $exe } |
             Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object { "$($_.Enabled)" -eq "True" -and "$($_.Direction)" -eq "Inbound" })
  if ($rules | Where-Object { "$($_.Action)" -eq "Block" }) { return @{ State = "blocked"; Exe = $exe } }
  $allow = @($rules | Where-Object { "$($_.Action)" -eq "Allow" })
  $cats = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue | ForEach-Object { ("$($_.NetworkCategory)" -replace 'DomainAuthenticated', 'Domain') } | Select-Object -Unique)
  $missing = @()
  foreach ($c in $cats) {
    $covered = $allow | Where-Object { "$($_.Profile)" -match "Any" -or "$($_.Profile)" -match $c }
    if (-not $covered) { $missing += $c }
  }
  if ($missing.Count -gt 0) { return @{ State = "missing"; Exe = $exe; Missing = ($missing -join ", ") } }
  return @{ State = "ok"; Exe = $exe }
}

function Read-ModuleStatus {
  $p = Join-Path ([IO.Path]::GetTempPath()) "steamframe-status.json"
  if (-not (Test-Path $p)) { return $null }
  try {
    $s = Get-Content $p -Raw | ConvertFrom-Json
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $s | Add-Member -NotePropertyName AgeMs -NotePropertyValue ($now - [long]$s.updated) -Force
    return $s
  } catch { return $null }
}

function Say {
  param([string]$Text)
  if ($script:NoSpeech) { return }
  try {
    Add-Type -AssemblyName System.Speech
    $s = New-Object System.Speech.Synthesis.SpeechSynthesizer
    $s.Speak($Text); $s.Dispose()
  } catch {}
}

function Start-VrcftViaSteam {
  # Starting VRCFaceTracking.exe directly never spawns the module process; Steam must launch it.
  Start-Process "steam://rungameid/$script:SteamAppId"
}

function Stop-Vrcft {
  $p = Get-Process VRCFaceTracking* -ErrorAction SilentlyContinue
  if ($p) { $p | Stop-Process -Force; Start-Sleep 2; return $true }
  return $false
}

# A .NET 10 SDK: PATH first, then the per-user install location. Returns the dotnet.exe path or $null.
function Find-DotnetSdk10 {
  foreach ($c in @("dotnet", "$env:USERPROFILE\.dotnet\dotnet.exe")) {
    $cmd = Get-Command $c -ErrorAction SilentlyContinue
    if ($cmd -and (& $cmd.Source --list-sdks 2>$null | Select-String "^10\.")) { return $cmd.Source }
  }
  return $null
}

function Write-Step { param([string]$State, [string]$Text)
  $color = @{ ok = "Green"; done = "Green"; warn = "Yellow"; fail = "Red"; would = "Cyan"; skip = "DarkGray"; info = "Gray" }[$State]
  if (-not $color) { $color = "Gray" }
  Write-Host ("[{0,-5}] {1}" -f $State, $Text) -ForegroundColor $color
}
