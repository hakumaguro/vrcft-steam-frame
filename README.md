# vrcft-steam-frame

Eye tracking for the **Steam Frame** in **VRChat**, through [VRCFaceTracking](https://docs.vrcft.io/) (VRCFT):
per-eye gaze, real blinks and winks. Mouth tracking is not possible: the Frame has no lower-face cameras.

> Unofficial. It uses eye data that SteamVR shares over OSC, and (optionally) a private, undocumented format on the
> headset. A SteamOS or SteamVR update can change either. Use at your own risk.

## What you need

| | Version |
|---|---|
| Steam Frame | **SteamOS 0.4.3 or newer** |
| SteamVR on the PC | **2.18.2 or newer** |
| VRCFaceTracking | from Steam (tested with 5.4.5) |
| Avatar | VRCFT eye tracking (`FT/v2` parameters) |

From these versions on, SteamVR sends each eye's eyelid to the PC itself, so **nothing has to be installed on the
headset** and no SSH is needed. With older versions use [v0.2.2](https://github.com/hakumaguro/vrcft-steam-frame/releases/tag/v0.2.2).

## New in 0.3

- **No calibration needed.** The module learns how open your eyes are while you wear the headset (it starts about 20 s
  after you put it on and settles within a few minutes) and remembers it for next time. Calibration (menu **3**) is
  now optional, for fine-tuning winks.
- **Blinks and winks from SteamVR itself**, per eye, with the open level that matches your eyes today. Right winks
  work more often than with fixed levels, and blinks close both eyes together.
- **Clearer health check:** the status (menu **2**) warns when the eye cameras run slow or the data keeps dropping out.
- **Simpler menu:** the headset part moved to **A** (Advanced), with a new option to turn it off. It is not needed any more.
- Half-closed (sleepy) eyelids can be tuned (`steamVrLid.sleepyLevel`), and calibration shows a live eyelid meter in the headset.

## Quick start

1. Download **`vrcft-steam-frame-<version>.zip`** from the [latest release](https://github.com/hakumaguro/vrcft-steam-frame/releases/latest)
   and unzip it anywhere. (It contains the ready-built module, so nothing else needs installing. Downloading the source
   instead also works: setup then builds the module and offers to install the .NET SDK for your user.)
2. **Close SteamVR**, then double-click **`Start Here.cmd`** and choose **1**.
   It sets everything up on the PC, restarts VRCFaceTracking and **speaks** the result. Run it again any time: it only
   changes what is not already right.
3. Start SteamVR, put the headset on and play. If SteamVR asks whether to share eye tracking data with PC applications,
   choose **allow**. Leave **"Track Dominant Eye Only" off** in the headset (VR Settings > General, shown with advanced
   settings): with it on, both eyelids follow one eye and winks cannot work.

Something not working? Choose **2**: it checks every part and says what is wrong and how to fix it.

Blinks or winks not quite right after a few minutes of wear? Choose **3** (needs [Python 3](https://www.python.org/downloads/)):
a voice guides you through about 70 seconds of open / closed / wink steps and applies the result.

**Always start VRCFaceTracking from Steam** (menu **S** does that). Starting its `.exe` directly skips the modules (VRCFT quirk).

**Updating from 0.2.x:** run menu **1** again (your settings are kept). If you installed the headset part (old menu 2),
it is no longer needed and an old one cannot read the eye data of SteamOS 0.4.3+ (it restarts in a loop, harmlessly).
Turn it off with **A**, then **2**, or on the headset `systemctl --user disable --now frameeyeosc`. While it sends, the
module prefers its data over SteamVR's.

## The headset part (Advanced, optional)

Menu **A** > **1** installs [frameeyeosc](https://github.com/konsti219/frameeyeosc) on the headset over SSH. On SteamOS
0.4.3+ it adds only one thing: a gaze direction per eye instead of one for both. When it is sending, the module uses it
for the eyelids too, with its own calibration, so calibrate (menu **3**) after installing or removing it.

1. Enable SSH on the headset (Developer Mode), then choose **A**, **1** and enter the headset login (for example
   `steamos@192.168.1.50`). You type the headset password once; after that it installs everything and starts it with
   every boot. Read [Why SSH?](#why-ssh) first if you are unsure.

   **What the password question looks like.** After the "authorising the key" line the window stops at:

   ```
   [info ] authorising the key on the headset: enter the headset password when asked (one time only)
   steamos@192.168.1.50's password:
   ```

   Type the password of the headset's user account and press **Enter**. **Nothing appears while you type** (no dots, no
   stars); that is normal for SSH. Then it prints `[ok   ] SSH login works` and carries on. If you see
   `Permission denied, please try again.` the password was wrong (three tries; run **A** > **1** again to restart). If
   you never set a password on the headset, set one first with `passwd` in a terminal on the headset.
   If you already log in to the headset with your own SSH key (or have no password set), setup uses that login to
   authorise its key and never asks for a password. When the login fails it prints what ssh said and what to check.
2. **A** > **2** turns it off again (stops it and its auto-start; the files stay).

## Using the headset with more than one PC

Without the headset part there is nothing to do: each PC gets its eye data from its own SteamVR, and each PC learns
the open level by itself. Run menu **1** once on every PC.

With the headset part, the headset sends its data to **one** PC address. **Every time you switch PCs, run A > 1 on
the PC you are about to use** (SSH must be on in the headset; after the first time it takes a few seconds). Until
then the module on this PC uses SteamVR's data, which works too, just with one gaze for both eyes. Each PC keeps its own
calibration in `steamframe-config.json` (menu **5** opens the folder); you can copy that file between PCs.

## Why SSH? (headset part only)

**Short version:** with SteamOS 0.4.3+ and SteamVR 2.18.2+ you do not need SSH at all. The headset part only adds a gaze
per eye; it is a small program that has to run *on the headset*, and SSH is only how the PC installs it for you. The eye
data itself does not go through SSH.

**The longer version:**

- The Frame tracks your eyes with its own cameras, and the result (gaze and how open each eyelid is) sits in the headset's
  memory. Steam Link forwards part of it to the PC: one gaze for both eyes and (from SteamOS 0.4.3 / SteamVR 2.18.2) each
  eye's eyelid. Older versions sent an eyelid value that was always 0, which is why the headset part used to be needed.
- [frameeyeosc](https://github.com/konsti219/frameeyeosc) is a small program that reads that data on the headset and sends
  it to your PC over the network (OSC, UDP port 9020).
- To install a program on the headset, something has to type commands on it. **A** > **1** does that from the PC over SSH:
  it copies three small scripts, builds frameeyeosc, and sets it to start with the headset.

**What SSH is used for, and what not:**

| | SSH needed? |
|---|---|
| Installing or updating the headset part (Advanced > 1) | yes, once |
| Sending eye data while you play | **no**: frameeyeosc sends it directly, SSH can be off |
| Everything on the PC (menus 1-6, S) | no |

**What runs on the headset:** frameeyeosc, as your normal user (not root). It reads only the eye-tracking data and sends it
only to your PC's address. It opens no ports on the headset. It is built from its source code (about 300 lines) in
`~/frameeyeosc`, with Rust installed in `~/.cargo`. Nothing outside your home folder is changed.

**Keeping it tight:**

1. Use SSH once for Advanced > 1, then **turn SSH off** again (the same way you turned it on, or `sudo systemctl disable --now sshd`
   on the headset). Eye tracking keeps working. Turn it back on only when you want to update.
2. Advanced > 1 logs in with a key it creates (`%USERPROFILE%\.ssh\id_ed25519_frame`); your password is typed once and never stored.
   To revoke the PC's access, delete that key's line from `~/.ssh/authorized_keys` on the headset.

**Without SSH at all**, you have two options:

- **Skip the headset part.** Menu 1 alone gives you gaze, blinks and winks through SteamVR (SteamOS 0.4.3+, SteamVR 2.18.2+).
- **Install it from the headset's own desktop.** Open a terminal in the Frame's desktop mode and run the same script
  Advanced > 1 would run, with your PC's address (shown by `ipconfig` on the PC; use the one on the same network as the headset):
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
| Winking closes **both** avatar eyes | The avatar links its eyelids: open the avatar menu and turn off **Eye Sync** / "Link eyes". Also check that "Track Dominant Eye Only" is off in the headset. |
| Status says "no eye data at all right now" | Wear the headset and connect Steam Link. If SteamVR asked whether to share eye tracking data with PC applications, allow it. Run menu **1** again (it checks that Steam Link shares eye data on port 9015). |
| Status says "only the Steam Link gaze is arriving" | Gaze works, eyelids are guessed: SteamVR sends no eyelid values. Update SteamVR (2.18.2+) and the headset (SteamOS 0.4.3+). |
| Status says "Track Dominant Eye Only is on" | Both eyelids follow one eye, so winks cannot work. Turn it off in the headset: VR Settings > General (shown with advanced settings). |
| Status says "eye data is slow" | SteamVR runs the eye cameras at a low rate, so blinks can be missed. Check SteamVR's eye-tracking settings on the headset (`eyeTrackingRateMin`). |
| Eyes look half closed for the first minutes | The module is still learning your open level (it starts about 20 s after you put the headset on). If it stays like that, calibrate (menu **3**). |
| Blinks too shallow, winks weak | Menu **3** (calibrate). Some people squint the other eye when winking; the calibration accounts for that. |
| Status says "your settings file has an error" | The module ignored the broken `steamframe-config.json` and kept the previous settings. Fix it, restore `steamframe-config.json.bak`, or calibrate again (menu **3**). |
| Status says "started without its modules" | Close VRCFaceTracking and start it from Steam (menu **S**). |
| Status mentions the old SteamLink module / port 9015 | Menu **1** moves the stock SteamLink module aside (menu **6**, uninstall, puts it back). |
| VRCFaceTracking closes itself when you open **Module Registry** | Fixed in 0.2.1: run menu **1** once (it repairs the module's `module.json`). |
| With the headset part: eyelids stopped after a network change or after using another PC | Run **A** > **1** again on this PC: it points the headset here and keeps the rest. |

Logs (PC): `%TEMP%\steamframe-module.log` (only changes are logged, capped at 1 MB).

## What the menu does

| | |
|---|---|
| 1 Set up / update | `scripts\setup.ps1`: finds VRCFaceTracking in any Steam library, installs or updates the module (your calibration is kept), moves the stock SteamLink module aside, sets VRCFT's output to VRChat's port 9000, sets Steam Link to share eye data on port 9015 (only while SteamVR is closed, with a backup), checks the firewall rule (offers an admin prompt), restarts VRCFaceTracking. `-DryRun` shows what would change. |
| 2 Status | `scripts\doctor.ps1`: checks every part and speaks a summary. |
| 3 Calibrate (optional) | `tools\tune.py calibrate`: spoken prompts (open, close, winks, blinks), then measures your eyes and applies the result. A small panel in the middle of the view shows the raw left/right eyelid values while it records (`--no-meter` turns it off; `tune.py meter` shows only the panel). With `--gaze` it also shows a dot in the headset to look at, and reports how many degrees your gaze is off (a report only; nothing is corrected yet). |
| 4 Live view | raw tracker vs output eyelids, updated live. |
| 5 Settings folder | opens the folder with `steamframe-config.json`. |
| 6 Uninstall | removes the module (calibration saved and restored on reinstall) and restores the stock SteamLink module. |
| S Start VRCFaceTracking | starts it through Steam (starting the exe directly skips the modules). The menu says when it is not running. |
| A Advanced (headset part) | Not needed with SteamOS 0.4.3+ and SteamVR 2.18+. 1: `setup.ps1 -Headset user@host`: SSH key (password once), builds [frameeyeosc](https://github.com/konsti219/frameeyeosc) on the headset (a pinned revision plus a patch for SteamOS 0.4.3's eye-data format; Rust in `~/.cargo`, nothing outside your home folder), installs an auto-start service that sends to the PC address the headset sees. 2: `-HeadsetOff` turns it off again (stops it and its auto-start; the files stay). |

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
version 5 (the eye record moved by 5 bytes), so **A** > **1** builds it with a small patch that reads both.

```
Steam Frame (SteamOS)                                PC (Windows)
  eye tracking service -> /dev/shm/eye-server.mmap
        |                                              VRCFaceTracking
  frameeyeosc  ---- OSC/UDP :9020 (network) ------->   Steam Frame module  --> VRChat (OSC :9000)
                                                            ^
  Steam Link driver ---- OSC/UDP :9015 (this PC) -----------+   (used when frameeyeosc is not sending)
```

Tested with Windows 11 and VRCFaceTracking 5.4.5 (Steam). SteamVR's own eyelid values: SteamOS 0.4.3 beta, 0.4.5 and
0.5.8 beta with SteamVR 2.18.2 beta and 2.19.2 beta. frameeyeosc: SteamOS build 20260922 (eye shared-memory version 4)
and 0.4.5 (version 5, patched).

## Tuning by hand

The module reloads `steamframe-config.json` (menu **5** opens its folder) within a second, no restart needed.
Calibration (menu **3**, optional) writes the per-eye levels and blink/wink settings for you; these are the knobs:

| key | meaning |
|---|---|
| `lid.leftClosed/leftOpen/rightClosed/rightOpen` | fixed per-eye calibration in raw tracker units (null = adapt automatically) |
| `steamVrLid.leftClosed/leftOpen/rightClosed/rightOpen` | the same for the eyelid values SteamVR sends itself (units of 1 - closed; null = closed is 0 and the open level adapts) |
| `steamVrLid.learnOpen` | on by default (null = on for SteamVR's eyelids): the module learns each eye's open level while you wear the headset (the 40th percentile of open-eye samples, mostly the last ~10 minutes of wear, after a 20 s warm-up; it moves at most 0.02 a second and is saved in `steamframe-learned.json` for the next start), so you do not need to calibrate. `leftOpen/rightOpen` then only give the starting point (a new calibration restarts learning from them). `false` = use the calibrated levels as they are |
| `steamVrLid.learnMinutes` | how long the learned level remembers, in minutes of wear (default 10) |
| `steamVrLid.sleepyLevel` | half-closed lids: this level (0..1 of the closed-open range, both eyes) is shown as `targetSleepy` (0.55). Raise it if half-closed eyes look too open on the avatar, lower it if too closed. Not set by calibration (SteamVR's readings of a half-closed eye vary too much between sessions); null = straight line |
| `steamVrLid.leftSquint/leftSleepy/rightSquint/rightSleepy` | raw level while squinting and with sleepy (half-closed) eyes; they bend the output so those poses show as `targetSquint` (0.30) and `targetSleepy` (0.55), linear in between. null = straight line. SteamVR's eyelids only (calibration writes them) |
| `steamVrLid.filter` (+ `filterMinCutoff / filterBeta`) | One Euro filter: steadies a held, partly closed lid without slowing blinks. Off by default; calibration turns it on |
| `steamVrLid.sharpenBelow / asymHysteresis` | wink sharpening only while one eye is below this level, and keep a one-eye squint that hovers near `blink.asymClosed` from closing both eyes again and again. Calibration sets them |
| `lid.deadband` | fraction of the range at each end that reads fully closed / open |
| `wink.threshold / range / strength` | amplify left/right asymmetry (0 strength = off) |
| `wink.assist` (+ `assistOpen / assistClosed / assistMin / assistPersistMs / assistReleaseMs`) | closing one eye tightens the other, so the tracker reports it partly closed during a wink. When one eye is at its floor and the other stays above its own floor for `assistPersistMs`, show the other eye open. Off by default |
| `blink.holdMs / releasePerSec` | hold the lowest lid value briefly so fast blinks reach full depth |
| `blink.saturatedRaw / glitchMinMs` | the tracker often reports a blink as one lid closed while the other is pinned at its ceiling (raw ~1.0); while that lasts, both eyes close (0 = off; frameeyeosc data only) |
| `blink.coupleMs / asymClosed / asymOpen` | any other lopsided closure shorter than `coupleMs` is a blink and closes both eyes; longer is a wink |
| `gaze.scale / invertX / invertY`, `swapEyes` | gaze scaling and orientation fixes |
| `trace` | write 30 ms samples to `%TEMP%\steamframe-trace.csv` all the time; leave it off (the calibration tool turns the trace on by itself only while it needs it, without this setting) |

Command line: `python tools/tune.py calibrate | record | analyze [--apply] | watch | show | dot`. `calibrate --gaze` adds
the gaze check (a dot in the headset at 5 positions, +25 s, SteamVR must run); `dot` only shows the dot at each
position, to check that it is visible in the headset.

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
