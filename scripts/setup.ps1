<#
  One-step setup for Steam Frame eye tracking on the PC (and optionally the headset).
  Safe to run again at any time: every step checks first and only changes what is not already right.

  usage:
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1                 # PC setup / update
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -DryRun         # show what would change, change nothing
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Headset steamos@192.168.1.50   # also set up the headset over SSH
    powershell -ExecutionPolicy Bypass -File scripts\setup.ps1 -Uninstall      # remove the module, restore the stock SteamLink module

  What it does (PC):
    1. finds VRCFaceTracking (any Steam library)
    2. gets the module: prebuilt DLL next to this script's folder, or builds it (installs a per-user .NET 10 SDK if needed, asks first)
    3. moves the stock SteamLink VRCFT module aside (it blocks UDP 9015; -Uninstall puts it back)
    4. makes VRCFaceTracking send to VRChat on port 9000
    5. Steam Link driver: OSC output port 9015 + share eye tracking (only while SteamVR is closed; backup kept)
    6. installs/updates the module DLL (your steamframe-config.json calibration is never overwritten)
    7. checks the Windows Firewall rule for the module (offers an admin prompt to add one)
    8. restarts VRCFaceTracking through Steam, then runs the status check
#>
param(
  [switch]$DryRun,
  [switch]$Yes,                 # answer yes to every question
  [switch]$Uninstall,
  [string]$Headset,             # user@host of the Steam Frame (optional)
  [string]$HeadsetTarget,       # PC address the headset should send to (default: auto-detect / keep existing)
  [string]$KeyFile = "$env:USERPROFILE\.ssh\id_ed25519_frame",
  [switch]$NoSpeech,
  [switch]$NoRestart,           # do not stop/start VRCFaceTracking (for testing against -VrcftData)
  [string]$VrcftData            # VRCFT data folder override (testing)
)
$ErrorActionPreference = "Continue"   # native tools (ssh, dotnet) write to stderr; each change below stops on its own errors
. "$PSScriptRoot\common.ps1"
$script:NoSpeech = $NoSpeech
$root = Split-Path -Parent $PSScriptRoot
$data = Get-VrcftDataDir $VrcftData
$parked = Join-Path $data "disabled-modules"
$changed = @()                # human-readable list of changes made
$needVrcftRestart = $false
$script:pendingStock = @()

function Ask([string]$q) {
  if ($Yes) { return $true }
  if ($DryRun) { return $false }
  $a = Read-Host "$q [Y/n]"
  return ($a -eq "" -or $a -match '^[yY]')
}
function Act([string]$what, [scriptblock]$do) {
  if ($DryRun) { Write-Step would $what; return }
  try { $ErrorActionPreference = "Stop"; & $do }
  catch { Write-Step fail "$what : $($_.Exception.Message)"; Say "Setup failed. See the screen."; exit 1 }
  Write-Step done $what
  $script:changed += $what
}

if ($DryRun) { Write-Host "DRY RUN: nothing will be changed.`n" -ForegroundColor Cyan }

# ---- 1. VRCFaceTracking -----------------------------------------------------------------------------
$vrcft = Find-VrcftDir
if (-not $vrcft) { Write-Step fail "VRCFaceTracking not found. Install it from Steam first (app $script:SteamAppId)."; Say "VRCFaceTracking is not installed."; exit 1 }
Write-Step ok "VRCFaceTracking: $vrcft"

# ---- uninstall ----------------------------------------------------------------------------------------
if ($Uninstall) {
  $ours = Get-OurModule $data
  $stockParked = @(Get-ChildItem $parked -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName $script:StockDll) })
  if (-not $ours -and -not $stockParked) { Write-Step ok "nothing to uninstall"; exit 0 }
  if (-not $NoRestart -and -not $DryRun) { [void](Stop-Vrcft) }
  if ($ours) {
    $cfg = Join-Path $ours.Dir "steamframe-config.json"
    Act "remove the Steam Frame module (calibration kept in $parked\steamframe-config.backup.json)" {
      New-Item -ItemType Directory -Force $parked | Out-Null
      if (Test-Path $cfg) { Copy-Item $cfg (Join-Path $parked "steamframe-config.backup.json") -Force }
      Remove-Item $ours.Dir -Recurse -Force
    }
  }
  foreach ($p in $stockParked) {
    $name = $p.Name -replace '^SteamLink-', ''
    Act "restore the stock SteamLink module ($($p.Name))" { Move-Item $p.FullName (Join-Path (Join-Path $data "CustomLibs") $name) }
  }
  Write-Step info "SteamVR's Steam Link OSC settings were left as they are (port 9015). Set 'OSC Output Port' back to 9000 in SteamVR if you want."
  if (-not $NoRestart -and -not $DryRun) { Start-VrcftViaSteam }
  Say "Uninstalled."
  exit 0
}

# ---- 2. module DLL: prebuilt or build ---------------------------------------------------------------
$prebuilt = Join-Path $root $script:ModuleDll
$src = Join-Path $root "module"
$dll = $null
if (Test-Path $prebuilt) {
  $dll = $prebuilt
  Write-Step ok "using the prebuilt module: $prebuilt"
} elseif (Test-Path $src) {
  $dotnet = Find-DotnetSdk10
  if (-not $dotnet) {
    Write-Step warn ".NET 10 SDK not found (needed to build the module from source)"
    if ((-not $DryRun) -and (Ask "Download and install the .NET 10 SDK for your user only (about 300 MB, no admin)?")) {
      $inst = Join-Path $env:TEMP "dotnet-install.ps1"
      Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $inst -UseBasicParsing
      & $inst -Channel 10.0 -InstallDir "$env:USERPROFILE\.dotnet" -NoPath
      $dotnet = Find-DotnetSdk10
    }
    if (-not $dotnet) {
      if ($DryRun) { Write-Step would "install a per-user .NET 10 SDK and build the module" }
      else { Write-Step fail "cannot build without the .NET 10 SDK (or download a release zip that contains $script:ModuleDll)"; exit 1 }
    }
  }
  if ($dotnet) {
    if ($DryRun) { Write-Step would "build the module" }
    else {
      & $dotnet build $src -c Release --nologo -v q "-p:VrcftDir=$vrcft" | Out-Host
      if ($LASTEXITCODE -ne 0) { Write-Step fail "build failed"; Say "Setup failed. The build did not work."; exit 1 }
      Write-Step ok "module built"
    }
    $dll = Join-Path $src "bin\Release\net10.0\$script:ModuleDll"
  }
} else { Write-Step fail "no module found (neither $script:ModuleDll nor module\ next to scripts\)"; exit 1 }

# ---- 3. stock SteamLink module ------------------------------------------------------------------------
$stock = @(Get-StockSteamLinkModules $data)
if ($stock.Count -eq 0) { Write-Step ok "stock SteamLink VRCFT module is not installed" }
else {
  foreach ($m in $stock) {
    Write-Step warn "stock SteamLink module installed ($($m.Name)); it blocks UDP 9015"
    if ($DryRun -or (Ask "Move it aside? (setup.ps1 -Uninstall puts it back)")) {
      $needVrcftRestart = $true
      $script:pendingStock += ,$m
    }
  }
}

# ---- 4. VRCFT output port -----------------------------------------------------------------------------
$outPort = Get-VrcftOscOutPort $data
$fixOutPort = ($outPort -and $outPort -ne "9000")
if ($fixOutPort) { Write-Step warn "VRCFaceTracking sends to port $outPort (VRChat listens on 9000)"; $needVrcftRestart = $true }
elseif ($outPort) { Write-Step ok "VRCFaceTracking sends to VRChat on port 9000" }

# ---- 5. Steam Link driver settings ---------------------------------------------------------------------
$vrPath = Get-SteamVrSettingsPath
$vr = Get-VrlinkSettings $vrPath
if ($vr.Ok) { Write-Step ok "Steam Link shares eye data on port 9015" }
elseif (-not $vrPath -or -not (Test-Path $vrPath)) { Write-Step warn "steamvr.vrsettings not found (start SteamVR once, then run setup again)" }
elseif (Test-SteamVrRunning) {
  Write-Step warn "Steam Link OSC settings need changing, but SteamVR is running (it would overwrite the change)."
  Write-Host "        Close SteamVR and run setup again, or in SteamVR: Settings > Advanced > Video > Steam Link: OSC Output Port 9015, share eye tracking on." -ForegroundColor DarkYellow
} else {
  Act "Steam Link driver: OSC output port 9015 and eye data sharing (backup: steamvr.vrsettings.bak-steamframe)" {
    $bak = "$vrPath.bak-steamframe"
    if (-not (Test-Path $bak)) { Copy-Item $vrPath $bak }
    $j = Get-Content $vrPath -Raw | ConvertFrom-Json
    if (-not $j.driver_vrlink) { $j | Add-Member -NotePropertyName driver_vrlink -NotePropertyValue ([pscustomobject]@{}) }
    $j.driver_vrlink | Add-Member -NotePropertyName OSCOutPort -NotePropertyValue 9015 -Force
    $j.driver_vrlink | Add-Member -NotePropertyName shareEyeTrackingData -NotePropertyValue $true -Force
    [IO.File]::WriteAllText($vrPath, ($j | ConvertTo-Json -Depth 32), (New-Object Text.UTF8Encoding($false)))
  }
}

# ---- 6. module install --------------------------------------------------------------------------------
$ours = Get-OurModule $data
$installDir = $null; $dllChanged = $true
if ($ours) {
  $installDir = $ours.Dir
  $cur = Join-Path $installDir $script:ModuleDll
  if ($dll -and (Test-Path $dll) -and (Test-Path $cur) -and ((Get-FileHash $dll).Hash -eq (Get-FileHash $cur).Hash)) { $dllChanged = $false }
}
if (-not $dllChanged) { Write-Step ok "module is up to date" }
else { $needVrcftRestart = $true }

$mjFix = $false
if ($ours) {
  try { $mj = Get-Content (Join-Path $ours.Dir "module.json") -Raw | ConvertFrom-Json } catch { $mj = $null }
  if (-not $mj -or -not $mj.ModulePageUrl -or -not $mj.DownloadUrl -or $mj.Version -ne $script:ModuleVersion) {
    Write-Step warn "module.json is incomplete or outdated (missing links make VRCFaceTracking's Module Registry page crash)"
    $mjFix = $true; $needVrcftRestart = $true
  } else { Write-Step ok "module.json is complete" }
}

# apply the VRCFT-side changes together, with VRCFT closed (it holds the DLL and rewrites its settings on exit)
if ($needVrcftRestart) {
  $wasRunning = [bool](Get-Process VRCFaceTracking -ErrorAction SilentlyContinue)
  if ($wasRunning -and -not $NoRestart) { Act "close VRCFaceTracking to apply changes" { [void](Stop-Vrcft) } }
  foreach ($m in @($script:pendingStock)) {
    if (-not $m) { continue }
    Act "move the stock SteamLink module to $parked" {
      New-Item -ItemType Directory -Force $parked | Out-Null
      Move-Item $m.Dir (Join-Path $parked ("SteamLink-" + $m.Id)) -Force
    }
  }
  if ($fixOutPort) {
    Act "VRCFaceTracking output port $outPort -> 9000" {
      $p = Get-VrcftLocalSettingsPath $data
      $raw = [IO.File]::ReadAllText($p)
      $raw = [regex]::Replace($raw, '("OSCOutPort"\s*:\s*"?)\d+', '${1}9000')
      [IO.File]::WriteAllText($p, $raw, (New-Object Text.UTF8Encoding($true)))
    }
  }
  if ($mjFix) { Act "rewrite module.json" { Write-ModuleJson $installDir $ours.Id } }
  if ($dllChanged) {
    if (-not $installDir) {
      $id = [guid]::NewGuid().ToString()
      $installDir = Join-Path (Join-Path $data "CustomLibs") $id
      Act "install the module into $installDir" {
        New-Item -ItemType Directory -Force $installDir | Out-Null
        Write-ModuleJson $installDir $id
        Copy-Item $dll $installDir -Force
        $saved = Join-Path $parked "steamframe-config.backup.json"
        if (Test-Path $saved) { Copy-Item $saved (Join-Path $installDir "steamframe-config.json"); Write-Step info "restored your earlier calibration" }
      }
    } else {
      Act "update the module DLL (calibration kept)" { Copy-Item $dll $installDir -Force }
    }
  }
}

# ---- 7. firewall --------------------------------------------------------------------------------------
$fw = Test-ModuleFirewall $vrcft
if ($fw.State -eq "ok") { Write-Step ok "firewall allows the module process" }
else {
  $why = "blocked"
  if ($fw.State -eq "missing") { $why = "no allow rule on: $($fw.Missing) network" }
  Write-Step warn "firewall: $why (headset data on UDP 9020 may not arrive)"
  if ($DryRun) { Write-Step would "add an inbound UDP 9020 allow rule for the module process (admin prompt)" }
  elseif (Ask "Add a firewall rule allowing UDP 9020 to the module? (Windows will ask for admin)") {
    $cmd = "New-NetFirewallRule -DisplayName 'Steam Frame eye tracking (VRCFT module)' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 9020 -Program '$($fw.Exe)' -Profile Any"
    # -EncodedCommand: Start-Process joins arguments without quoting, which breaks paths with spaces
    $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile", "-EncodedCommand", $enc
    if ((Test-ModuleFirewall $vrcft).State -eq "ok") { Write-Step done "firewall rule added"; $changed += "firewall rule" }
    else { Write-Step warn "firewall rule not confirmed (admin prompt declined?)" }
  }
}

# ---- 8. headset (optional) ----------------------------------------------------------------------------
if ($Headset) {
  $sshOpts = @("-i", $KeyFile, "-o", "IdentitiesOnly=yes", "-o", "ConnectTimeout=8", "-o", "StrictHostKeyChecking=accept-new")
  Write-Host ""
  Write-Step info "headset: $Headset"
  if (-not (Test-Path $KeyFile)) {
    if ($DryRun) { Write-Step would "create SSH key $KeyFile" }
    else { ssh-keygen -q -t ed25519 -f $KeyFile -N '""' -C "vrcft-steam-frame"; Write-Step done "SSH key created: $KeyFile" }
  }
  $keyOk = $false
  if (Test-Path $KeyFile) { & ssh @sshOpts -o BatchMode=yes $Headset "true" 2>$null; $keyOk = ($LASTEXITCODE -eq 0) }
  if (-not $keyOk -and -not $DryRun) {
    Write-Step info "authorising the key on the headset: enter the headset password when asked (one time only)"
    Get-Content "$KeyFile.pub" | & ssh -o ConnectTimeout=8 -o StrictHostKeyChecking=accept-new $Headset "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
    & ssh @sshOpts -o BatchMode=yes $Headset "true" 2>$null; $keyOk = ($LASTEXITCODE -eq 0)
  }
  if ($DryRun) { Write-Step would "copy the headset scripts, build frameeyeosc, install the auto-start service" }
  elseif (-not $keyOk) { Write-Step fail "cannot log in to $Headset with the key; is SSH enabled on the headset and the address right?" }
  else {
    Write-Step ok "SSH login works"
    # copy the scripts first; remote commands stay free of quotes (Windows PowerShell 5.1 mangles them)
    & ssh @sshOpts -o BatchMode=yes $Headset "mkdir -p ~/steamframe"
    & scp @sshOpts -q "$root\scripts\headset-setup.sh" "$root\scripts\headset-install.sh" "$root\scripts\frameeyeosc.service" "${Headset}:steamframe/"
    & ssh @sshOpts -o BatchMode=yes $Headset 'sed -i s/\r$// ~/steamframe/*'
    $info = & ssh @sshOpts -o BatchMode=yes $Headset "bash ~/steamframe/headset-install.sh --info"
    $client = (($info | Where-Object { $_ -like "CLIENT=*" }) -replace '^CLIENT=', '')
    $unitTarget = (($info | Where-Object { $_ -like "UNIT=*" }) -replace '^UNIT=', '')
    $pick = Select-HeadsetTarget -Override $HeadsetTarget -Existing $unitTarget -Client $client
    $target = $pick.Target
    Write-Step info $pick.Reason
    if (-not $target) { Write-Step fail "could not work out this PC's address as seen from the headset; pass -HeadsetTarget <PC-IP>"; exit 1 }
    & ssh @sshOpts -o BatchMode=yes $Headset "bash ~/steamframe/headset-install.sh $target 9020"
    if ($LASTEXITCODE -eq 0) { Write-Step done "headset: frameeyeosc installed and sending to ${target}:9020"; $changed += "headset service" }
    else { Write-Step fail "headset setup failed (see the output above)" }
  }
}

# ---- 9. restart + status ------------------------------------------------------------------------------
Write-Host ""
if ($DryRun) { Write-Host "Dry run finished." -ForegroundColor Cyan; exit 0 }
if (-not $NoRestart -and -not (Get-Process VRCFaceTracking -ErrorAction SilentlyContinue)) {
  Write-Step info "starting VRCFaceTracking through Steam, then checking (about 20 s)"
  Start-VrcftViaSteam
  for ($i = 0; $i -lt 30; $i++) { Start-Sleep 1; $s = Read-ModuleStatus; if ($s -and $s.AgeMs -lt 2000 -and $s.source -ne "starting") { break } }
}
if ($changed.Count -eq 0) { Write-Host "Nothing needed changing." -ForegroundColor Green } else { Write-Host ("Changed: " + ($changed -join "; ")) -ForegroundColor Green }
if ($NoRestart) { exit 0 }
$docArgs = @("-ExecutionPolicy", "Bypass", "-File", "$PSScriptRoot\doctor.ps1")
if ($NoSpeech) { $docArgs += "-NoSpeech" }
& powershell @docArgs
