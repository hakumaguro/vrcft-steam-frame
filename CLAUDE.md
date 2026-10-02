# vrcft-steam-frame: notes for Claude Code sessions

Eye tracking for the Steam Frame in VRChat via VRCFaceTracking (VRCFT). Read `README.md` for the user-facing docs and
`CLAUDE.local.md` (git-ignored, if present) for machine-specific details (headset address, SSH key, paths).

## Current state (2026-09-27)

- Latest release **v0.2.2** (2026-09-28: review fixes, calibration voice fix, pinned frameeyeosc), prebuilt-DLL zip. Working end to end on the author's setup: per-eye gaze, blinks, both
  winks, wink assist; frameeyeosc auto-starts on the headset and survived a real reboot; `doctor.ps1` reports "All good".
- `setup.ps1 -Headset` (menu 2) was run for real on 2026-09-27 against an already-set-up Frame: SSH login, target kept, build no-op,
  unit refreshed, service active, doctor "All good"; a second run changed nothing. Still never exercised: the first-time path on a fresh
  headset (Rust install + first build). Treat that as a test when someone new tries it. The key authorisation by password was tested
  on 2026-09-28 with a throwaway key against the real headset: it used to pipe the public key into `ssh` (stdin), which made Windows
  OpenSSH unable to read the password (reported by a user); now the key goes in the command line, password auth is forced and a
  duplicate key is not appended. Gotcha when testing: a key file under `%TEMP%` can have an orphaned-SID ACL, and Windows ssh then
  ignores it ("bad permissions"); use `icacls <key> /inheritance:r /grant:r "$env:USERNAME:F"`. The `!` box in Claude Code is bash
  without a TTY: password prompts need a real PowerShell window.
- Open ideas: verify the gaze scale (frameeyeosc +-1 == +-45 deg -> radians) against a reference; a first-run check in `Start Here.cmd`
  that VRCFT is running (users forget to start it from Steam each session; the doctor catches it).

## Review fixes in 0.2.2 (2026-09-27/28, from an external review; all verified against the code first; released)

- `tune.py analyze` rejects a calibration whose per-eye open-closed range is < 0.15 or reversed (was a ZeroDivisionError or bad
  values), keeps the previous config, and says so. It now also recommends `wink.assist` + `assistMin` (other eye's both-closed p75 + 0.08).
- `tune.py record` checks first (status file) that VRCFT runs and frameeyeosc data is fresh, and speaks what is missing.
- `ModuleConfig.TryLoad` + `Validate`: a broken or invalid `steamframe-config.json` is not applied; the module keeps the last good
  config and reports `configError` (status file, log, doctor). Previously it silently fell back to defaults.
- Without fresh data from either source the module sets neutral eyes (open, centred) once, instead of replaying stale Steam Link values.
- Trace lines are written with `FormattableString.Invariant` (decimal-comma locales broke the CSV); `tune.py` skips malformed lines.
- `setup.ps1` sandbox mode (`-VrcftData`) no longer touches the real SteamVR settings (unless `-SteamVrSettings <file>`) or the firewall.
- Firewall: a *block* rule is handled separately (disable it + add allow, elevated); an allow rule alone does not beat a block rule.
- `headset-setup.sh` pins frameeyeosc to a reviewed commit (`REV`, override `FRAMEEYEOSC_REV`) and moves existing checkouts to it;
  `FRAMEEYEOSC_NO_BUILD=1` tests only the checkout logic (works locally with `HOME=<tmp>`).
- `Select-HeadsetTarget` only counts addresses of connected adapters (Windows lists a disconnected adapter's IP).
Follow-up review of f5ec145, also fixed in 0.2.2: `Validate` checks relationships (`maxFloor + minRange <= 1`, `minCeil > 0`) and the
adaptive clamp can no longer get a lower bound above 1 (fuzzed: 7269 valid configs x 200 samples through `LidCal.Map`, no throw, output 0..1);
a failed config read is retried every second (timestamp recorded only after success, each distinct error logged once; tested with a 3 s
exclusive lock); `tune.py analyze` maps with the configured `lid.deadband` and `wink.assistClosed` (`STEAMFRAME_CONFIG=<file>` overrides
the config path for tests). `settings_or_exit` validates those values (deadband 0..0.45, assistClosed 0..1, numbers only) with the
module's limits before any calculation, and `record`/`calibrate` run it before recording; broken files give a spoken error, nothing changes.
Calibration voice: `tune.py` used to start PowerShell + System.Speech for every prompt (~1.5 s idle, worse under VR load), so prompts
lagged their beeps and piled up; the user could not hear them (2026-09-28). `Speaker` keeps one engine for the whole recording (started in
the lead-in, 7-10 ms per prompt, a new prompt cancels a late one). Not caused by 0.2.2: the prompt code was unchanged since v0.2.1.
Not done yet: offline replay tests for the eyelid pipeline (the replays in this history were ad-hoc scripts).

## Eyelids from SteamVR (2026-10-02, uncommitted, deployed on the author's PC as a 0.2.3 dev build)

- `module/LidPipeline.cs`: the lid pipeline moved out of the module class (no VRCFT types), one instance per source. Checked against
  the committed code on 10 old sessions: identical output on every row, with fixed and with adaptive levels.
- Steam Link path: when `/sl/xrfb/facew/EyesClosedL/R` are fresh, raw = 1 - closed goes through the same pipeline with its own
  levels (`steamVrLid` in the config; without them closed = 0 and only the open level adapts) and **without** the glitch rule
  (`blink.saturatedRaw`): on this scale the open eye reads 1.0 during normal winks (seen for 170-240 ms in wink steps). Without the
  lid values (older SteamVR) the old guessed blink is used. Gaze stays one direction for both eyes.
- `/sl/eyeTrackedGazePoint` is a point at the distance the eyes converge (length 25 when parallel, 0.2-0.3 with both eyes
  closed, but also below 1.5 for up to 100 % of a fixation with open eyes when the estimates converge). So on the per-eye path the
  gaze is held only while both mapped lids are below 0.5, not by the vector length (a first version did both and would have frozen
  the gaze; found by replaying round 1's gaze steps, `replay_gaze.py`). The guessed-blink path still uses the length.
- Status file: `lids` = frameeyeosc / steamvr / guessed / none, `dominantEyeOnly` = true / false / null. Detection is in
  `OscReceiver` (L and R of the same frame, last 200 frames with a partly closed eye: >= 90 % equal -> on, <= 50 % -> off).
  The doctor reports `lids=steamvr` as OK and warns when Track Dominant Eye Only is on.
- Trace rows from this path have the tag `SLINK` (same columns as `FRAME`). `tune.py` reads whichever tag has more rows, writes the
  levels to `steamVrLid` for SLINK sessions and does not recommend `saturatedRaw` there.
- Tested by sending the recorded OSC (`sessions/20261002-dominant-eye/osc.csv`) to the installed module on 9015: `lids=steamvr`,
  `dominantEyeOnly` false -> true -> false across the three rounds, outputs equal to the offline replay. Left wink: left only closed
  59-78 % of the step. Right wink: mostly both half closed (the user's left lid follows), as before. The three settings files were
  unchanged (hashes). `tune.py analyze --apply` on a session built from that replay wrote `steamVrLid` to a copy of the config.
- The `wink` and `blink` sections are shared by both sources, so a calibration on SteamVR's lids also retunes the frameeyeosc
  path. For SLINK sessions `tune.py` counts every lopsided blink for `coupleMs` (the module has no ceiling rule there).
- Live on the headset the same evening: status `lids=steamvr`, doctor "All good", a spoken check (3 of 3 blinks with both eyes,
  both closed 100 %, gaze left -13 / right +25 / up +17 / down -19 deg, no freeze while looking at a near finger), then a real
  `tune.py calibrate` (session `20261002-184358`, applied: `steamVrLid` L 0.00-0.95, R 0.05-0.88, `wink.threshold` 0.144).
  Left wink OK twice (right eye squints to 0.6-0.75). Right wink: seen once (right 0.0-0.1, left 0.7), not seen once (right
  stayed at 0.6-0.9). Blinks: 4 of 7 lopsided with the same signature as on frameeyeosc (one lid 0, the other pinned at 1.0, for
  ~230 ms). Replayed with the applied config: glitch rule off -> one blink shows one eye for 53 ms, the rest are clean (coupleMs
  180 + holdMs 120 cover them); glitch rule on -> all clean, first left wink 72 % instead of 80 %. Kept off on this path, because
  SteamVR's scale puts a normally open eye at exactly 1.0 (this user's open median), so with the rule on a wink without a squint
  would be read as a blink.
- Pipeline fix found in that live data (both sources): during a left wink the squinting right eye hovered around `blink.asymOpen`
  (0.60), each dip below it for > 60 ms reset the lopsided timer and the next rise closed both eyes again for coupleMs + holdMs
  (live: both closed 40 % / 22 % of the two left-wink steps). Now the timer keeps running while the same eye stays closed and the
  other one is merely between `asymClosed` and `asymOpen`; it restarts when the closed eye changes sides or both were closed.
  Replays: that session with the same config 17 % / 10 % both closed (the wink onset and one real blink of the open eye), blinks
  unchanged; 6 old frameeyeosc sessions: nothing worse, right wink shown 46 -> 53 % and 56 -> 75 % in two of them, 0-39 rows
  differ per session. Live afterwards (`live_check.py winks`): left wink 61 %, right wink 57 % of the hold with the other eye open,
  3 of 3 blinks with both eyes. Right winks are still hit and miss: in 2 of 5 tries today the tracker itself kept the right lid at
  0.6-0.9, and the module cannot recover those.
- Gaze on this path, one session (`20261002-184358`): 4.2 deg from the dot, 1.1 with an offset + gain correction fitted on one pass
  and tested on the other (mostly vertical: +4.0 deg, gain 1.16). The "no module-side gaze correction" conclusion of 2026-10-01
  was for frameeyeosc; repeat the session before building one here.
- The user checked it in VRChat on 2026-10-02: works.
- frameeyeosc and shm version 5 (checked 2026-10-02): upstream konsti219 has no version 5 support (last commit 2026-10-01, lid
  remap). The sasaken1102r fork has it since v0.6.1 and describes the layout the same way (5 bytes at 0x152, record at 0x157, file
  0x4f21f). A 30-line patch over the pinned revision (`sessions/20261002-frameeyeosc-v5/frameeyeosc-shm-v5.patch`: record offset
  chosen by version, 4 and 5 accepted) was built in a copy on the headset (4.6 s, the installed checkout and service untouched)
  and run for 5 s against a local port: 90 samples/s, per-eye X, lids 0..1, eye service fine afterwards.
- Decision (user, 2026-10-02): SteamVR's values are the main path from now on (SteamVR may make frameeyeosc unnecessary); the
  headset part is optional, for per-eye gaze. `headset-setup.sh` now carries the patch (heredoc, `git apply`, idempotent, checkout
  with `--force` so a later revision change is not blocked by it) and `headset-install.sh` accepts versions 4 and 5. Tested with
  `FRAMEEYEOSC_NO_BUILD=1 HOME=<tmp>`: fresh, re-run, another revision and back; the patched source equals the one built on the
  headset. Not run through `setup.ps1 -Headset` on the real headset (the author's headset keeps the unpatched checkout, service
  disabled). When frameeyeosc sends, the module still prefers it (per-eye gaze, `lid` levels, glitch rule on), so that path needs
  its own calibration on 0.4.3.
- No release for now (user, 2026-10-02): this was only used with beta versions on both sides (SteamOS 0.4.3, SteamVR 2.18.2).
  Latest release stays v0.2.2; revisit when those versions are on the stable channels.
- **To do before that release** (asked by the user): a step that turns frameeyeosc off on the headset for people who already
  enabled it with an older version (`systemctl --user disable --now frameeyeosc`; needs SSH or the headset's terminal, so a
  setup option plus README update instructions), and a doctor hint for updaters. Reason: the old build restart-loops on
  shared-memory version 5, and a working one makes the module prefer frameeyeosc over SteamVR's values. The replay tool and the test scripts are in `sessions/20261002-steamvr-lids-dev/`.

## SteamOS 0.4.3 beta + SteamVR beta 2.18.2 (measured 2026-10-02)

- **frameeyeosc at the pinned revision does not start on SteamOS 0.4.3**: `/dev/shm/eye-server.mmap` is ABI version 5 (file 324127
  bytes, +5). The service loops on "unsupported eye shared-memory version 5; expected 4" and the module runs on the Steam Link fallback.
  Layout change: 5 new bytes at 0x152 (`00 ff ff ff ff` = Track Dominant Eye Only off, see below), the eye record follows unchanged at 0x157
  (was 0x152). Upstream konsti219 has no fix yet; the sasaken1102r fork has an open PR (#7) that skips the 5 bytes.
- Raw openness behaves differently on 0.4.3: both closed reads 0.000 (was L 0.14 / R 0.32), open about 0.85-1.0 (was 0.65-0.75).
  Fixed lid levels from older calibrations no longer fit.
- SteamVR 2.18.2 now sends per-eye closedness on the Steam Link OSC port (9015), ~160 Hz, 8-bit steps:
  `/sl/xrfb/facew/EyesClosedL`, `/sl/xrfb/facew/EyesClosedR` (0 open .. 1 closed; `/avatar/parameters/LeftEyeLid`/`RightEyeLid` carry
  the same values), `/tracking/eye/EyesClosedAmount` (= min of the two, 100 % of samples), plus `...EyeLidExpandedSqueeze`, `...SqueezeToggle`,
  `...WidenToggle`. Roughly closed = 1 - raw openness (raw 0.9 -> 0.11, 0.7 -> 0.30, 0.5 -> 0.47, 0 -> 1). Gaze on that port is still
  one combined direction (LeftEyeX == RightEyeX); per-eye gaze exists only in the shared memory.
- Winks on port 9015 (setting off, 3 spoken trials, closed L / R): left closed 1.00 / 0.29, 1.00 / 0.33, 1.00 / 0.45. Right closed
  1.00 / 0.93 (reads as both closed), 0.44 / 0.83, 0.59 / 0.79: separable from both-closed (1.00 / 1.00) in 2 of 3 trials, the
  left lid drags to about 0.4-0.6.
- Captures and scripts: `sessions/20261002-os043-steamvr-osc/` (`run_sniff.ps1` closes VRCFT, records 9015 + the headset shm
  with spoken prompts, restarts VRCFT).
- **Track Dominant Eye Only**, measured off -> on -> off with the dot (`sessions/20261002-dominant-eye/`, `dom_eye_test.py` +
  `analyze_dom.py`; one sitting, headset not taken off). The setting is `steamvr.eyeTrackingDominantEyeOnly` in the headset's
  `~/.config/openvr/config/steamvr.vrsettings` (written at once; key absent = off; `dominantEye` default 1 = right).
  - The 5 new shm bytes are the setting: off `00 ff ff ff ff`, on `01 01 00 00 00` (u8 flag + i32 dominant eye index, -1 when off).
  - On: `gaze_direction` of both eyes = the dominant eye's `pre_fusion_gaze` (L-R 0.14 deg, constant). `pre_fusion_gaze` of the
    other eye and the raw `openness` of both eyes are still written per eye (left closed: L 0.00, R 0.63).
  - On: SteamVR's OSC lids become identical (`EyesClosedL == EyesClosedR` in 100 % of samples, both follow the dominant eye), so
    winks cannot come from port 9015 while it is on. Off: equal in 0 % of partly closed samples.
  - Gaze error vs the dot: off 5.7, on 3.2, off 2.4 deg. The two off rounds differ more than on vs off, because the right eye's
    estimate shifted between round 1 and 2 (e.g. dot +20: 16.3 -> 21.0 deg) and stayed. No accuracy gain shown: on equals the
    dominant eye alone (3.0-3.2 in rounds 2-3, 6.0 in round 1), off round 3 fused both eyes to 2.4. Jitter 0.35-0.6 deg either way.
  - With the setting off the eye service already fuses: Y is shared. For X the likely rule (about 30 gaze steps, one sitting) is:
    made parallel when the two estimates diverge, kept per eye when they converge (round 1: up to 4.6 deg apart at dot left, read
    as crossing; rounds 2-3: L-R 0.1-0.3 deg). The per-eye X differences of 2026-10-01 (OS 0.3.0) are probably the same mechanism.
- Not re-checked on 0.4.3: the blink glitch rule (`blink.saturatedRaw`) assumes a lid pinned at raw ~1.0 is abnormal, but open eyes
  now read exactly 1.0 often. Recalibrate and replay before trusting winks through frameeyeosc on 0.4.3.

## Gaze check with a dot in the headset (2026-10-01, uncommitted; works on the Frame through Steam Link)

- First real run (session `20261001-182041`, user confirmed the dot is visible at all five positions): error now 2.7 deg, with a per-eye
  offset 2.0, offset+gain 2.1 (held-out), same dot twice differs by 1.2, jitter 0.46. Fitted gain 0.94-0.99, vertical offset +0.4 deg,
  horizontal offset L +2.3 / R +1.6: the +-45 deg scale and both axis directions are right, and a module-side gaze correction would gain
  only ~0.7 deg in this session. Looking down 15 deg the eyes read 7 deg apart horizontally (L +5.4, R -1.6). Lids barely change with
  pitch (down 0.95 of open). One session only; offsets may depend on how the headset sits.
- Second run 10 min later (`20261001-183006`): error now 4.1, offset 2.6, offset+gain 2.6, repeat 1.0. Horizontal fit is stable between
  the runs (offset ~+2, gain 0.92-0.96); the vertical offset moved +0.4 -> +2.4 and the vertical value drifts with horizontal position
  (left +4..5, right -2..-3). Run 1's fit applied to run 2 gives ~3.5 (hand-computed from the printed table), so a stored correction
  would gain ~0.6 deg. Conclusion so far: no module-side gaze correction. Lid levels run 1 -> 2: open L 0.80/0.76, R 0.65/0.66;
  closed L 0.15/0.25, R 0.28/0.23. `wink_right` FAIL in both (left lid drops to 0.70 / 0.55 of open).
- The -4..-23 deg vertical values in old "look straight ahead" steps were the user looking elsewhere, not a tracker offset.

- `tools/overlay.py`: head-locked SteamVR overlay (bullseye, 2 deg, 3 m) through `openvr_api.dll` + ctypes (`FnTable:IVROverlay_028`,
  table positions checked against pyopenvr). Child process of `tune.py` (class `Overlay`); if it cannot start (SteamVR not running ->
  "Not starting vrserver for background app") the recording uses the old spoken "look around" step. `tune.py dot` only shows the dot.
- With the dot, the `look` step becomes 10 `gaze` steps (centre, +-20 deg x, +-15 deg y, each twice, 2.5 s); `labels.csv` gains
  `tx,ty,dist,shown`. `gaze_check` in `analyze` is report-only (never in `rec`): error now (raw x `gaze.scale` x 45 deg, inverts applied)
  vs. error with a per-eye offset / offset+gain correction fitted on one pass and tested on the other, repeatability, lid level at
  up/centre/down. Also written to `<session>/gaze-report.json` and spoken in one sentence.
- Verified: 9 old sessions give byte-identical analyze output; a synthetic session (known offset/gain, blinks, saccade delay) is recovered.
- frameeyeosc sends the same Y for both eyes (ly == ry in 99.9 % of old samples); X is per eye.

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
When frameeyeosc is silent for 500 ms: Steam Link driver OSC on `127.0.0.1:9015` (one gaze for both eyes; per-eye lids from
SteamVR 2.18.2+ with SteamOS 0.4.3+, otherwise a heuristic blink).
Either port may fail to open; the module only refuses to start if both do (reason in `startError`).
frameeyeosc sends `/avatar/parameters/FT/v2/{EyeLeftX,EyeLeftY,EyeRightX,EyeRightY,EyeLidLeft,EyeLidRight,EyeX,EyeY}`
(+-1 == +-45 degrees, lids 0 = closed, 1 = open) and `/avatar/parameters/FT/EyeTrackingActive`.

## Facts established by measurement (trust these, re-verify only if hardware/software changed)

- Steam Link OSC eyelid (`/tracking/eye/EyesClosedAmount`) is always 0 and left == right (mirrored). Real per-eye lids exist only via frameeyeosc.
  (True before SteamOS 0.4.3 / SteamVR 2.18.2; see the 0.4.3 section above.)
- Steam Link driver settings (`driver_vrlink` in `Steam\config\steamvr.vrsettings`): `OSCOutPort` 9000 = direct to VRChat, 9015 = alt (for the
  module); `shareEyeTrackingData` must be true. Edit only with SteamVR closed (it rewrites the file on exit). `setup.ps1` does this.
- VRCFT outputs `EyeLid = openness * 0.75` (0.75 = normal open). VRCFT only sends a parameter when it changes, and sends OSC inside bundles.
- The module -> VRCFT chain keeps the eyes independent (synthetic sweep: L closed/R open -> `EyeLidLeft 0, EyeLidRight 0.75`, and reverse).
- Tracker levels differ per eye: closed about L 0.14 / R 0.32, open about L 0.65 / R 0.75 (calibrations 2026-09-26; before SteamOS 0.4.3).
- Many blinks are reported lopsided: one lid ~0 while the other is pinned at raw ~1.0, up to ~350 ms. Real winks keep the open eye ~0.7-0.85.
- Closing one eye tightens the other: during a right wink the left lid reads ~0.23-0.45 (vs ~0.14 when both are closed).
- The Frame can reach the PC over two paths: its own Wi-Fi AP (PC "Wi-Fi 2", 10.35.78.x) or the home network. Which one Steam Link
  uses can change between sessions; the headset's route to the AP subnet disappears when the PC's Wi-Fi 2 is disconnected, and packets to
  the AP address then go to the home router and vanish. Target the PC's home-network address (the `$SSH_CLIENT` address) when both exist.
- After a headset reboot the network comes up after the service starts; frameeyeosc exits with "Network is unreachable" and systemd
  `Restart=always` retries until it works (observed 2026-09-27). This is expected.
- Avatars can link the eyelids themselves: Haku_FT_Comfy has a Bool `FT/EyeSync` toggle; with it on, a wink closes both eyes. Check the
  avatar before debugging the module when "a wink closes both eyes".

## Wink/blink pipeline (`LidPipeline.Process`), in order

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
