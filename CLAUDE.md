# vrcft-steam-frame: notes for Claude Code sessions

Eye tracking for the Steam Frame in VRChat via VRCFaceTracking (VRCFT). Read `README.md` for the user-facing docs and
`CLAUDE.local.md` (git-ignored, if present) for machine-specific details (headset address, SSH key, paths).

## Layout

- `module/` C# (net10.0) VRCFT module. `SteamFrameVRCFTModule.cs` (logic), `OscReceiver.cs` (UDP OSC parser),
  `ModuleConfig.cs` (hot-reloaded `steamframe-config.json`). Built against the installed VRCFT DLLs (`VrcftDir` in the csproj).
- `tools/tune.py` guided calibration (`record`, `analyze [--apply]`, `watch`, `show`). Stdlib only, Windows.
- `scripts/` `deploy-module.ps1` (build + install + relaunch VRCFT), `headset-setup.sh`, `headset-run.sh`, `frameeyeosc.service`.

## Data flow

`Steam Frame eye service -> /dev/shm/eye-server.mmap -> frameeyeosc (on headset) -> OSC UDP :9020 -> module -> VRCFT -> VRChat :9000`.
Fallback when frameeyeosc is silent for 500 ms: Steam Link driver OSC on `127.0.0.1:9015` (gaze only, heuristic blink).
frameeyeosc sends `/avatar/parameters/FT/v2/{EyeLeftX,EyeLeftY,EyeRightX,EyeRightY,EyeLidLeft,EyeLidRight,EyeX,EyeY}`
(+-1 == +-45 degrees, lids 0 = closed, 1 = open) and `/avatar/parameters/FT/EyeTrackingActive`.

## Facts established by measurement (trust these, re-verify only if hardware/software changed)

- Steam Link OSC eyelid (`/tracking/eye/EyesClosedAmount`) is always 0 and left == right (mirrored). Real per-eye lids exist only via frameeyeosc.
- Steam Link driver settings (`driver_vrlink` in `Steam\config\steamvr.vrsettings`): `OSCOutPort` 9000 = direct to VRChat, 9015 = alt (for the module);
  `shareEyeTrackingData` must be true. Edit only with SteamVR closed (it rewrites the file on exit).
- VRCFT outputs `EyeLid = openness * 0.75` (0.75 = normal open). VRCFT only sends a parameter when it changes, and sends OSC inside bundles.
- The module -> VRCFT chain keeps the eyes independent: input (L closed, R open) gives `EyeLidLeft 0, EyeLidRight 0.75` and the reverse
  (verified with a synthetic sweep, see "Measuring VRCFT output").
- Tracker levels differ per eye: closed about L 0.16 / R 0.32, open about L 0.68 / R 0.76 (calibration session 2026-09-26).
- Blinks are reported lopsided (one lid ~0, the other ~1.0) for ~100 ms; real winks last seconds. `blink.coupleMs` exists for this.
- Left wink is clean in the raw data. A right wink also lowers the left lid (to ~0.25): tracker cross-talk.

## Open issue (where we stopped)

User reports a wink on either eye closes BOTH avatar eyes in VRChat (avatar Haku_FT_Comfy). The module and VRCFT output were shown to be
per-eye, so suspect the avatar side: it exposes a parameter `FT/EyeSync` (a toggle whose behaviour has NOT been verified; the name suggests it
links the two eyelids). VRChat's OSC output did not report its value. Next steps, in order:
1. Ask the user to look for an eye-sync / link-eyes option in the avatar's menu and turn it off, or test with an avatar without one.
2. Or set it over OSC: send `/avatar/parameters/FT/EyeSync` (bool/float 0) to VRChat on UDP 9000 and have the user wink again.
3. If a right wink still closes both, that is the tracker coupling (see above); raise `wink.strength`/lower `wink.threshold`, or accept it.
Also untested on the avatar: blink coupling (`blink.coupleMs` 154) and the fixed per-eye calibration written by `tune.py analyze --apply`.

## Pitfalls

- **Start VRCFT through Steam** (`steam://rungameid/3329480`). Launching `VRCFaceTracking.exe` directly never spawns the module process.
- Only one module may bind UDP 9015: the stock SteamLink module must stay uninstalled/disabled.
- The module DLL is locked while VRCFT runs; `deploy-module.ps1` closes VRCFT first.
- `dotnet` on PATH may be runtime-only; the build needs a .NET 10 SDK (`~\.dotnet` per-user install works, no admin).
- The bash tool mangled backslash escapes (`\\n`, `\\0`) in inline Python heredocs; write files with the Write/Edit tools and use
  `chr(92)` or marker strings when patching. Bash `/tmp` is not visible to Windows Python.
- frameeyeosc needs shared-memory ABI version 4 (`od -A d -t u4 -N 16 /dev/shm/eye-server.mmap` -> first value 4, second 1) and stops on headset reboot.
- GitHub: `gh auth status` may show a different keyring account than `gh api user`; the repo owner is whatever `gh api user --jq .login` returns.

## Measuring VRCFT output (recipe)

To see exactly what VRCFT sends VRChat, temporarily set `"OSCOutPort":"9001"` in
`%APPDATA%\VRCFaceTracking\VRCFaceTracking\ApplicationData\LocalSettings.json` (VRCFT closed), relaunch via Steam, stop frameeyeosc, send a
synthetic frameeyeosc stream to UDP 9020 (open/closed lid steps) and parse OSC bundles arriving on 9001 (VRChat holds 9000).
**Always restore 9000 afterwards and restart frameeyeosc**, otherwise VRChat gets no eye data.

## Diagnostics

`%TEMP%\steamframe-module.log` (events) and `%TEMP%\steamframe-trace.csv` (30 ms samples:
`time,FRAME,lx,ly,rx,ry,rawL,rawR,outL,outR,loL,hiL,loR,hiR`). `tune.py record` saves labelled sessions under `sessions/` (git-ignored).

## Conventions

- Keep tunables in `ModuleConfig` (hot-reloaded) rather than constants, and teach `tune.py analyze` to measure them.
- Don't push to the public repo until a change has been tested on the headset. Never commit IPs, SSH keys or personal paths.
- frameeyeosc is cloned and built on the headset, not vendored (credit: konsti219, MIT).
