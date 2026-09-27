# vrcft-steam-frame: notes for Claude Code sessions

Eye tracking for the Steam Frame in VRChat via VRCFaceTracking (VRCFT). Read `README.md` for the user-facing docs and
`CLAUDE.local.md` (git-ignored, if present) for machine-specific details (headset address, SSH key, paths).

## Current state (2026-09-27)

- Latest release **v0.2.1** (2026-09-27; fixes the Module Registry crash via a complete `module.json`), with a prebuilt-DLL zip. Working end to end on the author's setup: per-eye gaze, blinks, both
  winks, wink assist; frameeyeosc auto-starts on the headset and survived a real reboot; `doctor.ps1` reports "All good".
- `setup.ps1 -Headset` (menu 2) was run for real on 2026-09-27 against an already-set-up Frame: SSH login, target kept, build no-op,
  unit refreshed, service active, doctor "All good"; a second run changed nothing. Still never exercised: the first-time path on a fresh
  headset (key authorisation by password, Rust install + first build). Treat that as a test when someone new tries it.
- Open ideas: verify the gaze scale (frameeyeosc +-1 == +-45 deg -> radians) against a reference; a first-run check in `Start Here.cmd`
  that VRCFT is running (users forget to start it from Steam each session; the doctor catches it).

## Working with the user

The user is in VR most of the time and cannot read text in the headset. Prefer spoken output (`Say` in `common.ps1`, `speak` in `tune.py`),
one-click entry points (`Start Here.cmd`), and short answers. They test in VRChat and report what the avatar does.

## Layout

- `module/` C# (net10.0) VRCFT module, version in `SteamFrameVRCFTModule.csproj` (`<Version>`). `SteamFrameVRCFTModule.cs` (logic, status
  file, logging), `OscReceiver.cs` (UDP OSC parser), `ModuleConfig.cs` (hot-reloaded `steamframe-config.json`). Built against the installed
  VRCFT DLLs; scripts pass `-p:VrcftDir=<found install>` (the csproj default is only a fallback).
- `Start Here.cmd` user menu (must stay CRLF): 1 setup, 2 headset, 3 status, 4 calibrate, 5 live view, 6 config folder, 7 uninstall.
- `tools/tune.py` calibration (`calibrate` = record+analyze+apply with speech, `record`, `analyze [--apply] [--speak]`, `watch`, `show`).
  Stdlib only, Windows. It turns the module's `trace` config flag on only while it needs samples (class `Tracing`, restores the file exactly).
- `scripts/` (Windows PowerShell 5.1 compatible, share `common.ps1`):
  `setup.ps1` idempotent installer (`-DryRun`, `-Yes`, `-Uninstall`, `-Headset user@host`, `-HeadsetTarget`; test flags `-VrcftData <sandbox>`
  + `-NoRestart`), `doctor.ps1` spoken health check (reads `%TEMP%\steamframe-status.json`), `deploy-module.ps1` dev shortcut,
  `package.ps1` release zip (`dist/`, git-ignored; setup uses a DLL found next to `Start Here.cmd`).
  Headset side: `headset-install.sh` (`--info`, or `<PC-IP> [port]`: build + systemd user unit + linger), `headset-setup.sh`,
  `headset-run.sh`, `frameeyeosc.service`.

## Data flow

`Steam Frame eye service -> /dev/shm/eye-server.mmap -> frameeyeosc (on headset) -> OSC UDP :9020 -> module -> VRCFT -> VRChat :9000`.
Fallback when frameeyeosc is silent for 500 ms: Steam Link driver OSC on `127.0.0.1:9015` (gaze only, heuristic blink).
Either port may fail to open; the module only refuses to start if both do (reason in `startError`).
frameeyeosc sends `/avatar/parameters/FT/v2/{EyeLeftX,EyeLeftY,EyeRightX,EyeRightY,EyeLidLeft,EyeLidRight,EyeX,EyeY}`
(+-1 == +-45 degrees, lids 0 = closed, 1 = open) and `/avatar/parameters/FT/EyeTrackingActive`.

## Facts established by measurement (trust these, re-verify only if hardware/software changed)

- Steam Link OSC eyelid (`/tracking/eye/EyesClosedAmount`) is always 0 and left == right (mirrored). Real per-eye lids exist only via frameeyeosc.
- Steam Link driver settings (`driver_vrlink` in `Steam\config\steamvr.vrsettings`): `OSCOutPort` 9000 = direct to VRChat, 9015 = alt (for the
  module); `shareEyeTrackingData` must be true. Edit only with SteamVR closed (it rewrites the file on exit). `setup.ps1` does this.
- VRCFT outputs `EyeLid = openness * 0.75` (0.75 = normal open). VRCFT only sends a parameter when it changes, and sends OSC inside bundles.
- The module -> VRCFT chain keeps the eyes independent (synthetic sweep: L closed/R open -> `EyeLidLeft 0, EyeLidRight 0.75`, and reverse).
- Tracker levels differ per eye: closed about L 0.14 / R 0.32, open about L 0.65 / R 0.75 (calibrations 2026-09-26).
- Many blinks are reported lopsided: one lid ~0 while the other is pinned at raw ~1.0, up to ~350 ms. Real winks keep the open eye ~0.7-0.85.
- Closing one eye tightens the other: during a right wink the left lid reads ~0.23-0.45 (vs ~0.14 when both are closed).
- After a headset reboot the network comes up after the service starts; frameeyeosc exits with "Network is unreachable" and systemd
  `Restart=always` retries until it works (observed 2026-09-27). This is expected.
- Avatars can link the eyelids themselves: Haku_FT_Comfy has a Bool `FT/EyeSync` toggle; with it on, a wink closes both eyes. Check the
  avatar before debugging the module when "a wink closes both eyes".

## Wink/blink pipeline (module `UpdateFromFrame`), in order

per-eye lid calibration (fixed levels from `tune.py analyze --apply`, else adaptive) -> blink glitch rule (`blink.saturatedRaw`: one lid closed
while the other is pinned at raw ~1.0 for `glitchMinMs` => both close) -> lopsided-closure timer (`blink.coupleMs`) -> wink assist
(`wink.assist`, off by default, on in the user's config: one eye at its floor and the other above its floor for `assistPersistMs` => other eye
shown open) -> wink sharpening -> peak hold (`blink.holdMs`).
A "latch on lopsided start" design was tried and abandoned: a right wink drops both lids almost together, so it never armed, and arming on the
first frame would turn blinks into winks. Validate new rules by replaying labelled sessions (`sessions/*`) offline before deploying.

## Releasing

1. Bump `<Version>` in `module/SteamFrameVRCFTModule.csproj` and `$script:ModuleVersion` in `scripts/common.ps1`.
2. Test on the headset (doctor "All good", user checks blink/wink in VRChat), commit, push.
3. `powershell -ExecutionPolicy Bypass -File scripts\package.ps1` -> `dist\vrcft-steam-frame-<ver>.zip`; unzip it and run
   `scripts\setup.ps1 -DryRun` from there to confirm it picks up the prebuilt DLL.
4. `gh release create v<ver> dist\vrcft-steam-frame-<ver>.zip --repo hakumaguro/vrcft-steam-frame --title v<ver> --notes-file <notes>`,
   then download the asset back and compare hashes. Releases are public: ask the user first.

## Testing without disturbing the user's install

- Hash `steamframe-config.json`, `steamvr.vrsettings` and VRCFT's `LocalSettings.json` before, and check them after.
- Setup/uninstall changes: sandbox = a copy of a VRCFT data folder, run `setup.ps1 -VrcftData <sandbox> -NoRestart -Yes` (and `-Uninstall`).
- Module source switching: send a synthetic frameeyeosc stream to UDP 9020 and read `%TEMP%\steamframe-status.json`.
- Doctor branches: point `$env:TEMP`/`$env:TMP` at a folder with a hand-written status file.
- Measuring VRCFT output: temporarily set `"OSCOutPort":"9001"` in `%APPDATA%\VRCFaceTracking\VRCFaceTracking\ApplicationData\LocalSettings.json`
  (VRCFT closed), relaunch via Steam, stop frameeyeosc, send a synthetic stream to 9020 and parse OSC bundles on 9001.
  **Always restore 9000 and restart frameeyeosc afterwards**, otherwise VRChat gets no eye data.

## Pitfalls

- **Start VRCFT through Steam** (`steam://rungameid/3329480`). Launching `VRCFaceTracking.exe` directly never spawns the module process.
- VRCFT's **Module Registry page crashes the whole app** (WinUI `0xc000027b`, stowed E_INVALIDARG) if any installed module's `module.json`
  lacks `DownloadUrl`/`ModulePageUrl`. Reproduced and bisected 2026-09-27 with `%TEMP%\click_registry.ps1`-style UI Automation (select the
  "Module Registry" list item, check the process survives 8 s). `Write-ModuleJson` in `common.ps1` writes every field; setup repairs old files.
  Bump `$script:ModuleVersion` in `common.ps1` together with the csproj `<Version>`.
- The module DLL is locked while VRCFT runs; setup/deploy close VRCFT first.
- `dotnet` on PATH may be runtime-only; the build needs a .NET 10 SDK (`Find-DotnetSdk10` in `common.ps1` checks `~\.dotnet` too).
- Windows PowerShell 5.1: no ternary/`??`; `ConvertTo-Json` needs `-Depth`; stderr redirection of native tools under
  `ErrorActionPreference=Stop` throws (setup uses Continue and stops inside `Act`); remote `ssh` commands must not contain double quotes
  (put logic in `headset-install.sh`); `Start-Process -ArgumentList` does not quote, so the elevated firewall command uses `-EncodedCommand`.
- Line endings: `.cmd`/`.ps1` CRLF, `.sh`/`.service`/`.py` LF (`.gitattributes`). Python rewrites with `newline=''` on text read in universal
  mode silently turn CRLF files into LF; fix `Start Here.cmd` afterwards. Setup also strips CR on the headset after `scp`.
- The bash tool mangles backslash escapes in inline heredocs; write patch scripts with the Write tool. Bash `/tmp` is not visible to Windows Python.
- frameeyeosc needs shared-memory ABI version 4 (`od -A d -t u4 -N 16 /dev/shm/eye-server.mmap` -> 4, 1); `headset-install.sh` warns otherwise.
- The headset's service target must be one of the PC's addresses; `setup.ps1 -Headset` keeps an existing target only if it still is
  (the Frame-AP address only exists while the headset is on).
- GitHub: `gh auth status` may show a different keyring account than `gh api user`; the repo owner is `gh api user --jq .login`.
- `tools/capture.ps1` (untracked, local) captures only the VRChat window via PrintWindow. Never use a screen-region grab: it catches other windows.

## Diagnostics

`%TEMP%\steamframe-status.json` (module state, 1 s; `source` = frameeyeosc / steamlink / none, port states, `startError`),
`%TEMP%\steamframe-module.log` (state changes only, rotated to `.old` at 1 MB) and `%TEMP%\steamframe-trace.csv` (only while config
`trace` is true; 30 ms samples `time,FRAME,lx,ly,rx,ry,rawL,rawR,outL,outR,loL,hiL,loR,hiR`, rotated at 20 MB).
`tune.py record` saves labelled sessions under `sessions/` (git-ignored). Headset: `journalctl --user -u frameeyeosc`.

## Conventions

- Keep tunables in `ModuleConfig` (hot-reloaded) rather than constants, and teach `tune.py analyze` to measure them.
- Don't push to the public repo until a change has been tested on the headset. Never commit IPs, SSH keys or personal paths
  (scan tracked files before pushing).
- Commits as the repo owner: `git -c user.name=Hakumaguro -c user.email=257888523+hakumaguro@users.noreply.github.com commit ...`.
- frameeyeosc is cloned and built on the headset, not vendored (credit: konsti219, MIT).
