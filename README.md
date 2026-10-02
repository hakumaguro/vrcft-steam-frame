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
3. **With SteamOS 0.4.3 and SteamVR 2.18.2 or newer you can skip this step**: SteamVR then sends each eye's eyelid
   itself (see [Newer SteamOS and SteamVR](#newer-steamos-and-steamvr-no-headset-part-needed)). On older versions, for
   **eyelids and winks**, choose **2** and enter the headset login (for example `steamos@192.168.1.50`; SSH must be
   enabled on the headset). You type the headset password once; after that it installs everything and starts it
   automatically with every boot. Not comfortable with SSH? Read [Why SSH?](#why-ssh) first: it is only needed once,
   you can turn it off afterwards, and there is a way without it.

   **What the password question looks like.** After the "authorising the key" line the window stops at:

   ```
   [info ] authorising the key on the headset: enter the headset password when asked (one time only)
   steamos@192.168.1.50's password:
   ```

   Type the password of the headset's user account and press **Enter**. **Nothing appears while you type** (no dots, no
   stars); that is normal for SSH, the keys are still being read. Then it prints `[ok   ] SSH login works` and carries on.
   If you see `Permission denied, please try again.` the password was wrong (you get three tries; run menu 2 again to
   restart). If you never set a password on the headset, set one first with `passwd` in a terminal on the headset.
4. Put the headset on and choose **4** to calibrate. A voice guides you (about 90 seconds) and tells you the result.

Something not working? Choose **3**: it checks every part and says what is wrong and how to fix it.

**Always start VRCFaceTracking from Steam.** Starting its `.exe` directly skips the modules (VRCFT quirk).

## Newer SteamOS and SteamVR: no headset part needed

From **SteamOS 0.4.3** on the Frame together with **SteamVR 2.18.2** on the PC (both in beta when this was written),
SteamVR sends how far each eye is closed to the PC by itself. The module uses that when the headset part is not sending,
so menu **1** and a calibration (menu **4**) are enough: blinks and winks work without SSH or anything installed on the
headset. Menu **3** then says "eye data from SteamVR".

- The gaze on this path is one direction for both eyes (the headset part sends one per eye).
- **Leave "Track Dominant Eye Only" off** (headset: VR Settings > General, shown with advanced settings). With it on, both
  eyelids follow one eye, so winks cannot work; menu **3** tells you when that is the case.
- The headset part (menu **2**) is optional on these versions. It adds one thing: a gaze direction per eye instead of one
  for both. When it is sending, the module uses it for the eyelids too, with its own calibration, so calibrate again
  (menu **4**) after installing or removing it.
- A headset part installed by version 0.2.2 or older stops working on SteamOS 0.4.3 (the headset's eye-data format
  changed). Nothing breaks: the module switches to SteamVR's values. Run menu **2** again if you want it back (it now
  builds frameeyeosc with a small patch for the new format), or remove it (see [Why SSH?](#why-ssh)).
- Calibrate again (menu **4**) after moving to these versions: the eyelid values are on a different scale.

## Using the headset with more than one PC

The headset sends its eye data to **one** PC address. After playing on another PC, the first PC gets nothing from the
headset: gaze still moves (Steam Link fallback), but blinks are guessed and winks don't work.

- **Every time you switch PCs, run menu 2 on the PC you are about to use.** It points the headset at that PC and changes
  nothing else; after the first time it takes a few seconds (nothing is rebuilt).
- SSH must be on in the headset for menu 2. If you turned it off after setup, turn it on again first.
- **You do not calibrate again when you switch.** Each PC keeps its own calibration (it is stored on the PC, in
  `steamframe-config.json`), and it stays valid while the headset is pointed elsewhere. Example: PCs A and B are both
  calibrated and you move from A to B: run menu **2** on B, nothing else.
- A PC you have never used needs menu **1** once and a calibration (menu **4**) once. Instead of calibrating there, you
  can copy `steamframe-config.json` from the other PC (menu **6** opens the folder on each PC): the values describe your
  eyes and the headset, not the PC.
- Calibrate again only when blinks or winks look wrong, the same as with one PC.
- Not sure which PC the headset is pointed at? Menu **3** on the PC you are using says "only the gaze is arriving"
  (or "eye data from SteamVR" on newer versions) when it is not this one.

## Why SSH?

**Short version:** the eyelid data never leaves the headset on its own, so a small program has to run *on the headset* to
send it to your PC. SSH is only how the PC installs that program for you. The eye data itself does not go through SSH.

**The longer version:**

- The Frame tracks your eyes with its own cameras, and the result (gaze and how open each eyelid is) sits in the headset's
  memory. Steam Link forwards only part of it to the PC: gaze, with both eyes identical, and an eyelid value that is
  always 0. That is why blinks and winks can't work from the PC side alone.
- [frameeyeosc](https://github.com/konsti219/frameeyeosc) is a small program that reads that data on the headset and sends
  it to your PC over the network (OSC, UDP port 9020).
- To install a program on the headset, something has to type commands on it. Menu **2** does that from the PC over SSH:
  it copies three small scripts, builds frameeyeosc, and sets it to start with the headset.

**What SSH is used for, and what not:**

| | SSH needed? |
|---|---|
| Installing or updating the headset part (menu 2) | yes, once |
| Sending eye data while you play | **no**: frameeyeosc sends it directly, SSH can be off |
| Everything on the PC (menus 1, 3, 4, 5) | no |

**What runs on the headset:** frameeyeosc, as your normal user (not root). It reads only the eye-tracking data and sends it
only to your PC's address. It opens no ports on the headset. It is built from its source code (about 300 lines) in
`~/frameeyeosc`, with Rust installed in `~/.cargo`. Nothing outside your home folder is changed.

**Keeping it tight:**

1. Use SSH once for menu 2, then **turn SSH off** again (the same way you turned it on, or `sudo systemctl disable --now sshd`
   on the headset). Eye tracking keeps working. Turn it back on only when you want to update.
2. Menu 2 logs in with a key it creates (`%USERPROFILE%\.ssh\id_ed25519_frame`); your password is typed once and never stored.
   To revoke the PC's access, delete that key's line from `~/.ssh/authorized_keys` on the headset.

**Without SSH at all**, you have two options:

- **Skip the headset part.** Menu 1 alone gives you gaze through Steam Link, but blinks are guessed and winks don't work.
- **Install it from the headset's own desktop.** Open a terminal in the Frame's desktop mode and run the same script
  menu 2 would run, with your PC's address (shown by `ipconfig` on the PC; use the one on the same network as the headset):
  ```
  git clone https://github.com/hakumaguro/vrcft-steam-frame ~/vrcft-steam-frame
  bash ~/vrcft-steam-frame/scripts/headset-install.sh <PC-IP>
  ```

**To remove the headset part completely:**
`systemctl --user disable --now frameeyeosc; loginctl disable-linger $USER; rm -rf ~/frameeyeosc ~/steamframe ~/.config/systemd/user/frameeyeosc.service`,
and `~/.cargo/bin/rustup self uninstall` for Rust.

## Troubleshooting

| You notice | Do this |
|---|---|
| VRCFaceTracking closes itself when you open **Module Registry** | Fixed in 0.2.1: run menu **1** once (it repairs the module's `module.json`, which lacked the links that page needs). |
| Winking closes **both** avatar eyes | The avatar links its eyelids: open the avatar menu and turn off **Eye Sync** / "Link eyes". |
| Status says "no eye data" | Wear the headset and connect Steam Link. For eyelids, the headset part must run: menu **2** sets it up again. |
| Status says "only the gaze is arriving" | Gaze works, eyelids are guessed. Update SteamVR (2.18.2+) and the headset (SteamOS 0.4.3+), or use the headset part: menu **2**, or on the headset `systemctl --user status frameeyeosc`. |
| Status says "Track Dominant Eye Only is on" | Both eyelids follow one eye, so winks cannot work. Turn it off in the headset: VR Settings > General (shown with advanced settings). |
| Eyelids stopped after a headset reboot or network change ("only the gaze is arriving") | The headset may now reach the PC over a different network. Run menu **2** again: it picks the address that works and keeps the rest. |
| Eyelids stopped after you used the headset with another PC | The headset still sends to that PC. Run menu **2** on this one (see "Using the headset with more than one PC"). |
| Status says "your settings file has an error" | The module ignored the broken `steamframe-config.json` and kept the previous settings. Fix it, restore `steamframe-config.json.bak`, or calibrate again (menu 4). |
| Status says "started without its modules" | Close VRCFaceTracking and start it from Steam. |
| Status mentions the old SteamLink module / port 9015 | Menu **1** moves the stock SteamLink module aside (menu **7** puts it back). |
| Eyes look wrong after a Frame update | With the headset part: menu **2** again; if the headset reports a new eye-data version, the headset part needs an update. Without it (SteamVR's own eyelid values): calibrate again (menu **4**). |
| Blinks too shallow, winks weak | Menu **4** (calibrate). Some people squint the other eye when winking; the calibration accounts for that. |

Logs (PC): `%TEMP%\steamframe-module.log` (only changes are logged, capped at 1 MB).

## What the menu does

| | |
|---|---|
| 1 Set up / update | `scripts\setup.ps1`: finds VRCFaceTracking in any Steam library, installs or updates the module (your calibration is kept), moves the stock SteamLink module aside, sets VRCFT's output to VRChat's port 9000, sets Steam Link to share eye data on port 9015 (only while SteamVR is closed, with a backup), checks the firewall rule (offers an admin prompt), restarts VRCFaceTracking. `-DryRun` shows what would change. |
| 2 Headset | `setup.ps1 -Headset user@host`: SSH key (password once), builds [frameeyeosc](https://github.com/konsti219/frameeyeosc) on the headset (a pinned revision plus a patch for SteamOS 0.4.3's eye-data format; Rust in `~/.cargo`, nothing outside your home folder), installs an auto-start service that sends to the PC address the headset sees. |
| 3 Status | `scripts\doctor.ps1`: checks every part and speaks a summary. |
| 4 Calibrate | `tools\tune.py calibrate`: spoken prompts (open, close, winks, blinks), then measures your eyes and applies the result. While SteamVR runs it also shows a dot in the headset to look at, and reports how many degrees your gaze is off (a report only; nothing is corrected yet). |
| 5 Live view | raw tracker vs output eyelids, updated live. |
| 7 Uninstall | removes the module (calibration saved and restored on reinstall) and restores the stock SteamLink module. |

## How it works

| | Steam Link OSC, older versions | Steam Link OSC, SteamOS 0.4.3+ with SteamVR 2.18.2+ | + frameeyeosc on the headset |
|---|---|---|---|
| Gaze | yes (both eyes mirrored) | yes (both eyes mirrored) | yes, per eye |
| Blink | guessed from eye movement | real eyelid openness | real eyelid openness |
| Wink | no | yes (with calibration) | yes (with calibration) |

Before SteamOS 0.4.3 / SteamVR 2.18.2 the Steam Link driver's eyelid value is always 0 on the Frame, and real per-eye eyelid
openness only exists in the headset's shared memory (`/dev/shm/eye-server.mmap`), which frameeyeosc reads and sends over OSC.
From those versions on, SteamVR sends each eye's closedness itself (`/sl/xrfb/facew/EyesClosedL` and `EyesClosedR`), and the
module uses it whenever frameeyeosc is not sending. Upstream frameeyeosc reads shared-memory version 4; SteamOS 0.4.3 has
version 5 (the eye record moved by 5 bytes), so menu **2** builds it with a small patch that reads both.

```
Steam Frame (SteamOS)                                PC (Windows)
  eye tracking service -> /dev/shm/eye-server.mmap
        |                                              VRCFaceTracking
  frameeyeosc  ---- OSC/UDP :9020 (network) ------->   Steam Frame module  --> VRChat (OSC :9000)
                                                            ^
  Steam Link driver ---- OSC/UDP :9015 (this PC) -----------+   (used when frameeyeosc is not sending)
```

Tested with Windows 11, VRCFaceTracking 5.4.5 (Steam), Steam Frame SteamOS build 20260922 (eye shared-memory version 4)
with frameeyeosc, and SteamOS 0.4.3 beta (eye shared-memory version 5) with SteamVR 2.18.2 beta through SteamVR's own eyelid values.

## Tuning by hand

The module reloads `steamframe-config.json` (menu **6** opens its folder) within a second, no restart needed.
Calibration (menu 4) writes the per-eye levels and blink/wink settings for you; these are the knobs:

| key | meaning |
|---|---|
| `lid.leftClosed/leftOpen/rightClosed/rightOpen` | fixed per-eye calibration in raw tracker units (null = adapt automatically) |
| `steamVrLid.leftClosed/leftOpen/rightClosed/rightOpen` | the same for the eyelid values SteamVR sends itself (units of 1 - closed; null = closed is 0 and the open level adapts) |
| `lid.deadband` | fraction of the range at each end that reads fully closed / open |
| `wink.threshold / range / strength` | amplify left/right asymmetry (0 strength = off) |
| `wink.assist` (+ `assistOpen / assistClosed / assistMin / assistPersistMs / assistReleaseMs`) | closing one eye tightens the other, so the tracker reports it partly closed during a wink. When one eye is at its floor and the other stays above its own floor for `assistPersistMs`, show the other eye open. Off by default |
| `blink.holdMs / releasePerSec` | hold the lowest lid value briefly so fast blinks reach full depth |
| `blink.saturatedRaw / glitchMinMs` | the tracker often reports a blink as one lid closed while the other is pinned at its ceiling (raw ~1.0); while that lasts, both eyes close (0 = off; frameeyeosc data only) |
| `blink.coupleMs / asymClosed / asymOpen` | any other lopsided closure shorter than `coupleMs` is a blink and closes both eyes; longer is a wink |
| `gaze.scale / invertX / invertY`, `swapEyes` | gaze scaling and orientation fixes |
| `trace` | write 30 ms samples to `%TEMP%\steamframe-trace.csv` (the calibration tool turns it on only while it needs it) |

Command line: `python tools/tune.py calibrate | record | analyze [--apply] | watch | show | dot` (`dot` only shows the
calibration dot at each position, to check that it is visible in the headset). If you cannot see the dot, calibrate with
`python tools/tune.py calibrate --no-dot`: spoken directions only, as before, without the gaze report.

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
