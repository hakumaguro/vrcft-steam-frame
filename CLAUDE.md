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

## Resolved: "a wink closes both avatar eyes" (2026-09-26)

Cause was on the avatar, not the module: Haku_FT_Comfy has a Bool toggle `FT/EyeSync` (avatar menu, Face Tracking) that links the two
eyelids. The user turned it off and blinking/winking then worked. The module -> VRCFT chain was already proven per-eye by a synthetic sweep
(see "Measuring VRCFT output"). If a future avatar closes both eyes on a wink, check its menu/parameters for an eye-sync/link option first.
The `FT/EyeSync` toggle can also be set over OSC: send a Bool to `/avatar/parameters/FT/EyeSync` on UDP 9000.

Still possible limits: the tracker couples the eyes on a right-eye wink (left lid drops to ~0.25 in the raw data), so a right wink may look
weaker than a left wink; raise `wink.strength` or lower `wink.threshold` if so. Not yet re-verified after EyeSync was turned off: how
`blink.coupleMs` (154) and the per-eye fixed calibration feel in daily use; adjust with `tools/tune.py`.

## Ideas / next steps

- frameeyeosc auto-start is set up on the user's headset as a systemd user unit (`scripts/frameeyeosc.service`) with lingering enabled; a real
  reboot test has not been done yet. If the PC's address on the Frame's subnet changes, the unit's `--target` must be updated.
- Re-run `tools/tune.py record` + `analyze` now that eye sync is off, and tune `wink.*` / `blink.*` from labelled data.
- Verify the gaze scale (frameeyeosc +-1 == +-45 deg is converted to radians) against a reference.
- `tools/capture.ps1` (untracked, local) captures only the VRChat window via PrintWindow (never a region screen-grab, which would include other
  windows) and is useful for judging the avatar's eyes from the desktop mirror in third-person view.

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
