<#
  Health check for Steam Frame eye tracking. Prints every check and SPEAKS a short summary,
  so it is usable with the headset on.

  usage:  powershell -ExecutionPolicy Bypass -File scripts\doctor.ps1 [-NoSpeech]
#>
param([switch]$NoSpeech, [string]$VrcftData)
$ErrorActionPreference = "Continue"
. "$PSScriptRoot\common.ps1"
$script:NoSpeech = $NoSpeech
$data = Get-VrcftDataDir $VrcftData

$problems = New-Object System.Collections.ArrayList   # spoken, most important first
$warnings = New-Object System.Collections.ArrayList
function Fail([string]$text, [string]$spoken, [string]$fix) { Write-Step fail $text; if ($fix) { Write-Host "        fix: $fix" -ForegroundColor DarkYellow }; [void]$problems.Add($spoken) }
function Warn([string]$text, [string]$spoken, [string]$fix) { Write-Step warn $text; if ($fix) { Write-Host "        fix: $fix" -ForegroundColor DarkYellow }; [void]$warnings.Add($spoken) }

Write-Host "Steam Frame eye tracking - status check`n"

$vrcft = Find-VrcftDir
if (-not $vrcft) { Fail "VRCFaceTracking is not installed" "VRCFaceTracking is not installed." "install VRCFaceTracking from Steam" }
else { Write-Step ok "VRCFaceTracking found: $vrcft" }

$ours = Get-OurModule $data
if (-not $ours) { Fail "the Steam Frame module is not installed" "The Steam Frame module is not installed." "run setup (Start Here, option 1)" }
else { Write-Step ok "module installed: $($ours.Dir)" }

$stock = @(Get-StockSteamLinkModules $data)
if ($stock.Count -gt 0) { Fail "the stock SteamLink VRCFT module is installed; it takes UDP 9015" "The old Steam Link module is still installed." "run setup, it moves it aside (reversible)" }

$vrcftRunning = [bool](Get-Process VRCFaceTracking -ErrorAction SilentlyContinue)
$moduleProc = [bool](Get-Process VRCFaceTracking.ModuleProcess -ErrorAction SilentlyContinue)
if (-not $vrcftRunning) { Fail "VRCFaceTracking is not running" "VRCFaceTracking is not running." "start it from Steam (not the exe directly)" }
elseif (-not $moduleProc) { Fail "VRCFaceTracking runs but no module process" "VRCFaceTracking started without its modules. Restart it from Steam." "close VRCFaceTracking and start it from Steam; starting the exe directly skips the modules" }
else { Write-Step ok "VRCFaceTracking and its module process are running" }

$st = Read-ModuleStatus
if ($vrcftRunning -and $moduleProc) {
  if (-not $st -or $st.AgeMs -gt 5000) {
    if (-not $st) { Fail "the installed module reports no status (older version, or it never started)" "The Steam Frame module is outdated or not active. Run setup." "run setup to install the current version" }
    elseif ($st.startError) { Fail "the Steam Frame module is idle: $($st.startError)" "The Steam Frame module could not start. See the screen." "" }
    else { Fail "the Steam Frame module is not active (status is $([int]($st.AgeMs/1000)) s old)" "The Steam Frame module is not active." "check the Modules page in VRCFaceTracking; if another module claims eye tracking, disable it" }
  } else {
    if ($st.startError) { Warn "module reports: $($st.startError)" "The module reported a start problem. See the screen." "" }
    if ($st.configError) { Warn "$($st.configError)" "Your settings file has an error, so the previous settings are still used." "fix the file (menu 5 opens its folder) or restore steamframe-config.json.bak" }
    switch ($st.source) {
      "frameeyeosc" { Write-Step ok "eye data from the headset (per-eye gaze and eyelids), $($st.frameAgeMs) ms old" }
      "steamlink"   {
        if ($st.lids -eq "steamvr") {
          Write-Step ok "eye data from SteamVR (per-eye eyelids, one gaze direction for both eyes), $($st.steamLinkAgeMs) ms old"
          if ($st.openFrom -eq "learned" -and $st.learnedOpen) { Write-Step ok ("open eyelid level learned while you wear the headset: L {0} R {1}" -f $st.learnedOpen[0], $st.learnedOpen[1]) }
          elseif ($st.openFrom) { Write-Step info "open eyelid level: $($st.openFrom)" }
          if ($st.dominantEyeOnly -eq $true) { Warn "both eyelids always carry the same value: 'Track Dominant Eye Only' is on in the headset, so winks are not possible" "Track Dominant Eye Only is on in the headset, so winks do not work. Turn it off in V R settings, General." "in the headset: VR Settings > General > Track Dominant Eye Only (shown with advanced settings)" }
        } else {
          Warn "only the Steam Link gaze is arriving: eyelids are guessed" "Only the gaze is arriving. Eyelids are guessed. Update Steam V R and the headset." "update SteamVR to 2.18+ and the headset to SteamOS 0.4.3+ (both on the normal channels); the optional headset part (Start Here, A) also works with older versions"
        }
      }
      default       { Warn "no eye data at all right now" "No eye data right now. Put the headset on and check Steam Link is connected." "wear the headset / connect Steam Link; on the headset check: systemctl --user status frameeyeosc" }
    }
    if ($st.eyeRateSlow -eq $true) {
      Warn "eye data is slow: $($st.eyeRate) new values a second with open eyes (normal is 40-90), so the avatar's eyes stutter. SteamVR on the headset may have slowed the eye cameras (its eyeTrackingRateMin setting, SteamOS 0.5.7+)" "Eye data is slow, so the eyes may stutter. Steam V R may have slowed the eye cameras." "restart SteamVR; if it stays slow, set the headset's SteamVR setting eyeTrackingRateMin to 90"
    } elseif ($st.eyeRate -ne $null -and $st.source -eq "steamlink") {
      if ($st.eyeRate -lt 5) { Write-Step info ("the eye values barely change ({0} a second): is the headset being worn?" -f $st.eyeRate) }
      else { Write-Step ok ("eye data rate: {0} new values a second" -f $st.eyeRate) }
    }
    if ($st.dropoutsLastMin -ge 3) {
      Warn "eye data dropped out $($st.dropoutsLastMin) times in the last minute (for under 5 s each)" "Eye data keeps dropping out. Check the Wi-Fi or Steam Link connection." "a weak Wi-Fi link or a busy network; the PC on a cable and the headset close to the router or the Frame's adapter help"
    }
    Write-Step info ("module {0}; frameeyeosc port {1}, Steam Link port {2}" -f $st.version,
      $(if ($st.frameListening) { "open" } else { "CLOSED" }), $(if ($st.steamLinkListening) { "open" } else { "CLOSED" }))
  }
}

$out = Get-VrcftOscOutPort $data
if ($out -and $out -ne "9000") { Fail "VRCFaceTracking sends OSC to port $out, VRChat listens on 9000" "VRCFaceTracking is sending to the wrong port, so VRChat gets nothing." "run setup, it resets the port to 9000" }
elseif ($out) { Write-Step ok "VRCFaceTracking sends to VRChat on port 9000" }

$vr = Get-VrlinkSettings (Get-SteamVrSettingsPath)
if (-not $vr.Ok) { Warn ("Steam Link OSC settings: port {0}, share eye data {1} (want 9015 / true)" -f $vr.Port, $vr.ShareEye) "Steam Link eye sharing is not set up. Only the fallback is affected." "run setup with SteamVR closed" }
else { Write-Step ok "Steam Link shares eye data on port 9015" }

if ($vrcft) {
  $fw = Test-ModuleFirewall $vrcft
  if ($fw.State -eq "blocked") { Fail "Windows Firewall BLOCKS the module process" "Windows firewall is blocking the headset data." "run setup, it offers to add an allow rule (admin prompt)" }
  elseif ($fw.State -eq "missing") { Warn "no firewall allow rule for the module on: $($fw.Missing) network" "The firewall may block headset data on this network." "run setup, it offers to add an allow rule (admin prompt)" }
  else { Write-Step ok "firewall allows the module process" }
}

if (-not (Get-Process VRChat -ErrorAction SilentlyContinue)) { Write-Step info "VRChat is not running (fine for this check)" }

Write-Host ""
if ($problems.Count -eq 0 -and $warnings.Count -eq 0) {
  Write-Host "All good." -ForegroundColor Green
  Say "All good. Eye tracking is working with headset data."
} else {
  $n = $problems.Count; $w = $warnings.Count
  Write-Host "$n problem(s), $w warning(s)." -ForegroundColor $(if ($n) { "Red" } else { "Yellow" })
  $first = @($problems) + @($warnings)
  $intro = ""
  if ($n) { $intro = "$n problem" + $(if ($n -gt 1) { "s" } else { "" }) + ". " } else { $intro = "Mostly working. " }
  Say ($intro + ($first | Select-Object -First 2) -join " ")
}
if ($problems.Count) { exit 1 } else { exit 0 }
