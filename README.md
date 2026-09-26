# vrcft-steam-frame

Eye tracking for the **Steam Frame** in **VRChat** through [VRCFaceTracking](https://docs.vrcft.io/) (VRCFT):
a VRCFT module, a guided calibration/tuning tool, and setup scripts.

> Unofficial. It reads a private, undocumented shared-memory format on the headset, which can change with any
> Frame update. Use at your own risk.

## What you get

| | Steam Link OSC only | + frameeyeosc (this repo's preferred path) |
|---|---|---|
| Gaze | yes (both eyes mirrored) | yes, per eye |
| Blink | heuristic (both eyes together) | real eyelid openness |
| Wink | no | partly (the tracker couples the eyes, see below) |
| Mouth / face | no (the Frame has no lower-face cameras) | no |

The Steam Link driver's OSC eyelid value (`/tracking/eye/EyesClosedAmount`) is always 0 on the Frame and its left/right
values are identical. Real per-eye eyelid openness only exists in the headset's shared memory
(`/dev/shm/eye-server.mmap`), which [frameeyeosc](https://github.com/konsti219/frameeyeosc) reads and sends over OSC.

## How it fits together

```
Steam Frame (SteamOS, aarch64)                     PC (Windows)
  eyetracking service -> /dev/shm/eye-server.mmap
        |                                            VRCFaceTracking
  frameeyeosc  ---- OSC/UDP :9020 (LAN) --------->   SteamFrameVRCFTModule  --> VRChat (OSC :9000)
                                                          ^
  Steam Link driver ---- OSC/UDP :9015 (loopback) ---------+   (fallback when frameeyeosc is not running)
```

## Requirements

Tested with: Windows 11, VRCFaceTracking 5.4.5 (Steam version), Steam Frame on SteamOS build 20260922
(shared-memory ABI version 4), VRChat with a VRCFT unified-expressions avatar (`FT/v2/*` parameters).

- .NET 10 SDK on the PC (`winget install Microsoft.DotNet.SDK.10`, or `dotnet-install.ps1 -Channel 10.0` for a per-user install)
- SSH access to the headset (or a terminal on it) to build frameeyeosc
- The VRCFT **SteamLink module must be uninstalled/disabled**: both listen on UDP 9015 and only one may run

## Setup

1. **SteamVR settings** (`Steam\config\steamvr.vrsettings`, close SteamVR first), under `driver_vrlink`:
   `"OSCOutPort": 9015` and `"shareEyeTrackingData": true` (in the UI: Advanced settings, Video, Steam Link).
2. **Install the module on the PC**: `powershell -ExecutionPolicy Bypass -File scripts\deploy-module.ps1`.
   It builds, installs into `%APPDATA%\VRCFaceTracking\CustomLibs\<guid>` and relaunches VRCFT.
   **Always start VRCFT through Steam** (`steam://rungameid/3329480`): starting the exe directly never spawns the module process.
3. **Build frameeyeosc on the headset**: copy `scripts/headset-setup.sh` over and run it (installs Rust under `~/.cargo`, nothing else).
4. **Start it**: `scripts/headset-run.sh <PC-IP>` on the headset. Use a PC address the headset can reach
   (with Steam Link's dongle/AP that is the PC's address on the Frame's own subnet, e.g. `10.35.78.x`; check with `ping`).
   Optional auto-start: `scripts/frameeyeosc.service` (systemd user unit).
5. Windows Firewall must allow inbound UDP 9020 to `VRCFaceTracking.ModuleProcess.exe`.

Logs and traces (PC): `%TEMP%\steamframe-module.log`, `%TEMP%\steamframe-trace.csv`.

## Tuning

Every eye reports different "open" and "closed" levels (for example closed 0.17 on the left vs 0.33 on the right), so the
module calibrates each eyelid separately. Tune it with a guided session:

```
python tools/tune.py record            # spoken prompts (works inside the headset): open, close, wink L/R, blink, look
python tools/tune.py analyze --apply   # measures levels, checks winks/blinks, writes the module config
python tools/tune.py watch             # live raw vs output eyelids
python tools/tune.py show              # current config
```

`record` stores timestamped labels with the trace, so every stretch of data has a known ground truth.
The module hot-reloads `steamframe-config.json` (next to the module DLL) within a second, no restart needed:

| key | meaning |
|---|---|
| `lid.leftClosed/leftOpen/rightClosed/rightOpen` | fixed per-eye calibration in raw tracker units (null = adapt automatically) |
| `lid.deadband` | fraction of the range at each end that reads fully closed / open |
| `wink.threshold / range / strength` | amplify left/right asymmetry (0 strength = off) |
| `blink.holdMs / releasePerSec` | hold the lowest lid value briefly so fast blinks reach full depth |
| `gaze.scale / invertX / invertY`, `swapEyes` | gaze scaling and orientation fixes |

## Troubleshooting

- **Winking closes both avatar eyes:** the avatar itself may link the eyelids. Look for an "Eye Sync" / "Link eyes" toggle in its menu
  (some VRCFT avatar templates expose `FT/EyeSync`) and turn it off. The module and VRCFT send the two lids independently.
- **No module process / no data:** start VRCFT through Steam, make sure the stock SteamLink module is not installed (UDP 9015 clash),
  and check `%TEMP%\steamframe-module.log`.
- **frameeyeosc refuses to start:** it needs shared-memory ABI version 4; after a Frame update the layout may have changed.

## Known limits

- The tracker couples the eyes: closing one eye pulls the other toward half-closed, and in our tests a right-eye
  wink also lowered the left lid. Wink sharpening helps but cannot remove it.
- VRCFT sends `EyeLid = openness x 0.75` (0.75 is normal open, above that means wide); that is VRCFT's convention, not a bug.
- Gaze scale (frameeyeosc sends +-1 for +-45 degrees) is converted to radians the same way as the Steam Link path; it has
  not been verified against a reference.
- No mouth or lower-face tracking: the Frame has no hardware for it.

## Credits

- [frameeyeosc](https://github.com/konsti219/frameeyeosc) (MIT) by konsti219 reads the Frame's eye data. It is cloned and built on
  the headset by `scripts/headset-setup.sh`, not vendored here.
- [LinkFT](https://github.com/ykeara/LinkFT) (MIT) by ykeara, the Steam Link VRCFT module this one is modelled on.
- [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) by benaclejames.

## License

MIT, see [LICENSE](LICENSE).
