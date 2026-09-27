# vrcft-steam-frame

Eye tracking for the **Steam Frame** in **VRChat**, through [VRCFaceTracking](https://docs.vrcft.io/) (VRCFT):
per-eye gaze, real blinks and winks. Mouth tracking is not possible: the Frame has no lower-face cameras.

> Unofficial. It reads a private, undocumented format on the headset that a Frame update can change. Use at your own risk.

## Quick start

You need: **VRCFaceTracking** (from Steam), **SteamVR + Steam Link** with the Frame, and an avatar with VRCFT eye tracking
(`FT/v2` parameters). For calibration, also **Python 3** ([python.org](https://www.python.org/downloads/)).

1. Download **`vrcft-steam-frame-<version>.zip`** from the [latest release](https://github.com/hakumaguro/vrcft-steam-frame/releases/latest)
   and unzip it anywhere. (It contains the ready-built module, so nothing else needs installing. Downloading the source
   instead also works: setup then builds the module and offers to install the .NET SDK for your user.)
2. **Close SteamVR**, then double-click **`Start Here.cmd`** and choose **1**.
   It sets everything up on the PC, restarts VRCFaceTracking and **speaks** the result. Run it again any time: it only
   changes what is not already right.
3. For **eyelids and winks**, choose **2** and enter the headset login (for example `steamos@192.168.1.50`; SSH must be
   enabled on the headset). You type the headset password once; after that it installs everything and starts it
   automatically with every boot.
4. Put the headset on and choose **4** to calibrate. A voice guides you (about 90 seconds) and tells you the result.

Something not working? Choose **3**: it checks every part and says what is wrong and how to fix it.

**Always start VRCFaceTracking from Steam.** Starting its `.exe` directly skips the modules (VRCFT quirk).

## Troubleshooting

| You notice | Do this |
|---|---|
| Winking closes **both** avatar eyes | The avatar links its eyelids: open the avatar menu and turn off **Eye Sync** / "Link eyes". |
| Status says "no eye data" | Wear the headset and connect Steam Link. For eyelids, the headset part must run: menu **2** sets it up again. |
| Status says "only the Steam Link fallback" | Gaze works, eyelids are guessed. The headset part is not sending: menu **2**, or on the headset `systemctl --user status frameeyeosc`. |
| Status says "started without its modules" | Close VRCFaceTracking and start it from Steam. |
| Status mentions the old SteamLink module / port 9015 | Menu **1** moves the stock SteamLink module aside (menu **7** puts it back). |
| Eyes look wrong after a Frame update | Menu **2** again; if the headset reports a new eye-data version, the headset part needs an update upstream. |
| Blinks too shallow, winks weak | Menu **4** (calibrate). Some people squint the other eye when winking; the calibration accounts for that. |

Logs (PC): `%TEMP%\steamframe-module.log` (only changes are logged, capped at 1 MB).

## What the menu does

| | |
|---|---|
| 1 Set up / update | `scripts\setup.ps1`: finds VRCFaceTracking in any Steam library, installs or updates the module (your calibration is kept), moves the stock SteamLink module aside, sets VRCFT's output to VRChat's port 9000, sets Steam Link to share eye data on port 9015 (only while SteamVR is closed, with a backup), checks the firewall rule (offers an admin prompt), restarts VRCFaceTracking. `-DryRun` shows what would change. |
| 2 Headset | `setup.ps1 -Headset user@host`: SSH key (password once), builds [frameeyeosc](https://github.com/konsti219/frameeyeosc) on the headset (Rust in `~/.cargo`, nothing outside your home folder), installs an auto-start service that sends to the PC address the headset sees. |
| 3 Status | `scripts\doctor.ps1`: checks every part and speaks a summary. |
| 4 Calibrate | `tools\tune.py calibrate`: spoken prompts (open, close, winks, blinks), then measures your eyes and applies the result. |
| 5 Live view | raw tracker vs output eyelids, updated live. |
| 7 Uninstall | removes the module (calibration saved and restored on reinstall) and restores the stock SteamLink module. |

## How it works

| | Steam Link OSC only | + frameeyeosc on the headset |
|---|---|---|
| Gaze | yes (both eyes mirrored) | yes, per eye |
| Blink | guessed from eye movement | real eyelid openness |
| Wink | no | yes (with calibration) |

The Steam Link driver's eyelid value is always 0 on the Frame and its left/right values are identical. Real per-eye eyelid
openness only exists in the headset's shared memory (`/dev/shm/eye-server.mmap`), which frameeyeosc reads and sends over OSC.

```
Steam Frame (SteamOS)                                PC (Windows)
  eye tracking service -> /dev/shm/eye-server.mmap
        |                                              VRCFaceTracking
  frameeyeosc  ---- OSC/UDP :9020 (network) ------->   Steam Frame module  --> VRChat (OSC :9000)
                                                            ^
  Steam Link driver ---- OSC/UDP :9015 (this PC) -----------+   (fallback when frameeyeosc is not sending)
```

Tested with Windows 11, VRCFaceTracking 5.4.5 (Steam), Steam Frame SteamOS build 20260922 (eye shared-memory version 4).

## Tuning by hand

The module reloads `steamframe-config.json` (menu **6** opens its folder) within a second, no restart needed.
Calibration (menu 4) writes the per-eye levels and blink/wink settings for you; these are the knobs:

| key | meaning |
|---|---|
| `lid.leftClosed/leftOpen/rightClosed/rightOpen` | fixed per-eye calibration in raw tracker units (null = adapt automatically) |
| `lid.deadband` | fraction of the range at each end that reads fully closed / open |
| `wink.threshold / range / strength` | amplify left/right asymmetry (0 strength = off) |
| `wink.assist` (+ `assistOpen / assistClosed / assistMin / assistPersistMs / assistReleaseMs`) | closing one eye tightens the other, so the tracker reports it partly closed during a wink. When one eye is at its floor and the other stays above its own floor for `assistPersistMs`, show the other eye open. Off by default |
| `blink.holdMs / releasePerSec` | hold the lowest lid value briefly so fast blinks reach full depth |
| `blink.saturatedRaw / glitchMinMs` | the tracker often reports a blink as one lid closed while the other is pinned at its ceiling (raw ~1.0); while that lasts, both eyes close (0 = off) |
| `blink.coupleMs / asymClosed / asymOpen` | any other lopsided closure shorter than `coupleMs` is a blink and closes both eyes; longer is a wink |
| `gaze.scale / invertX / invertY`, `swapEyes` | gaze scaling and orientation fixes |
| `trace` | write 30 ms samples to `%TEMP%\steamframe-trace.csv` (the calibration tool turns it on only while it needs it) |

Command line: `python tools/tune.py calibrate | record | analyze [--apply] | watch | show`.

## Known limits

- The tracker couples the eyes: closing one pulls the other toward half-closed. Calibration and the wink assist compensate.
- VRCFT sends `EyeLid = openness x 0.75` (0.75 is normal open; above that means wide). That is VRCFT's convention.
- Gaze scale (frameeyeosc sends +-1 for +-45 degrees) has not been checked against a reference.
- The module listens for frameeyeosc on UDP 9020 on all networks; anyone on your network could send it eye data.

## Building from source

`scripts\deploy-module.ps1` (build + replace the installed DLL + restart VRCFT) for development,
`scripts\package.ps1` for a release zip with a prebuilt DLL (no .NET SDK needed by users). Needs the .NET 10 SDK and an
installed VRCFaceTracking (its DLLs are referenced).

## Credits

- [frameeyeosc](https://github.com/konsti219/frameeyeosc) (MIT) by konsti219 reads the Frame's eye data. It is cloned and built on
  the headset by the setup, not included here.
- [LinkFT](https://github.com/ykeara/LinkFT) (MIT) by ykeara, the Steam Link VRCFT module this one is modelled on.
- [VRCFaceTracking](https://github.com/benaclejames/VRCFaceTracking) by benaclejames.

## License

MIT, see [LICENSE](LICENSE).
