# vrcft-steam-frame: notes for Claude Code sessions

Eye tracking for the Steam Frame in VRChat via VRCFaceTracking (VRCFT). Read `README.md` for the user-facing docs and
`CLAUDE.local.md` (git-ignored, if present) for machine-specific details (headset address, SSH key, paths).

## Direction (agreed with the user, 2026-11-09)

- Priorities: **easy to use and stable** first; more expressions only if they need no risky setup.
- Default path = PC + SteamVR's own eye data only. No headset install, no manual calibration. The headset part
  (frameeyeosc) stays as an **advanced, optional** menu (existing v0.2.2 users still run it); not removed.
- Widen / squint / pupils / brows: parked. They need the eye-camera buffers on the headset (`dsp_service`, undocumented),
  as in sasaken1102r/frameeyeosc (vision on top of Valve's lids) or Project-Babble/Babble-Frame-Runtime (NN replaces
  Valve's lids). Revisit only after an A/B in the mirror shows clear value; then accept their output as an extra input
  and keep our blink/wink rules.
- Plan, in order: (1) measure once on SteamOS 0.5.8 beta (`tune.py record --gaze`, user in the headset): lopsided blinks
  still there? Valve's gaze error vs the dot? open-level stability? Also: status `eyeRate` >= 40 while worn and no "eye
  data is slow" log line (the 8-35/s band comes from a 2026-10-02 capture; if 0.5.8 gives fewer new values, retune it
  before shipping the warning). And a click through the new menu (2, then A, B). (2) zero-calibration eyelids: learn each eye's
  relaxed-open level (robust statistic, warm-up, slow follow, saved and used as the seed next wear); keep the wink/blink
  settings; validate on the old sessions (learned levels within ~0.05 of the calibrated ones, blink/wink not worse);
  spoken calibration becomes an optional "tune winks" tool. (3) stability (done, below). Then v0.3: merge
  `steamvr-eyelids`, remove the README beta box, release after the user's check in VRChat.
- Release gate: SteamVR 2.18 reached stable on 2026-10-21 with eye openness over OSC (web, not yet checked by us on the
  public branch: `lids=steamvr` there is the test). Valve says lids will also come through the OpenVR SDK later: then the
  Steam Link OSC port setup could go away.
- Day state: headset SteamOS 0.5.8 beta (build 20261106), PC SteamVR beta 2.19.2 (build 26496310). This morning's log
  showed per-eye lids from SteamVR working on these versions.

## v0.3.1 (2026-11-10, committed locally, NOT pushed, NOT released)

- GitHub issue #1 (halobtw, 2026-10-28): Advanced > 1 failed with "cannot log in ... with the key" although their SSH
  works. Root cause in `setup.ps1` section 8: it tried only its own key (`IdentitiesOnly`) and authorised it with
  `PubkeyAuthentication=no` (password only), so key-only / passwordless headsets could never pass, and the final test
  hid ssh's stderr (`2>$null`). Fix: first authorise through the existing login (BatchMode, default keys/agent), then the
  password; on failure print ssh's last line plus a hint (host key changed, permission denied, no answer).
  The reply was posted on the issue (v0.3.0 needs no SSH). Failure path tested on `steamos@127.0.0.1`; the key-reuse and
  password paths still need a run against the headset (it was off). Release notes: `dist/release-notes-0.3.1.md`. 2026-11-10 afternoon: user on SteamVR **stable** (build 25998080, BetaKey public) +
  SteamOS 0.5.8 beta: `lids=steamvr`, dominantEyeOnly false; `live_check.py winks` x2: blinks 3/3 both, left wink 67 / 94 %,
  right wink 36 / 15 % (tracker kept the left lid at raw 0.5; known hit-and-miss). Key-reuse path tested on the real
  headset with a throwaway key + a temporary copy of the working key as `~/.ssh/id_ed25519` (both removed afterwards,
  the throwaway line removed from the headset's authorized_keys): "authorised ... no password needed", `-HeadsetOff` ran
  end to end. Password path unchanged code, not tested (needs a typed password). Mirror check 2026-11-10 14:00 (VRChat running, stable SteamVR): 4 right-wink attempts with
  VRChat window captures (`scratchpad/wink_mirror.py`; `tools/capture.ps1` works, the window shows the third-person camera
  and the in-world mirror): right-only closed 64 / 70 / 96 / 86 % of the hold (earlier runs 36 / 15 % were bad attempts,
  the tracker held the left lid at raw 0.5), left eye shown 0.68-0.89 open; screenshots show a clean wink, other eye open. The user confirmed the avatar's RIGHT eye closed when winking right. Menu A / B / Q driven with keystrokes through cmd (stdin): Advanced screen shows, B returns to the main menu, Q exits 0. SSH password path tested on the real headset
  (throwaway key, real PowerShell window, user typed the password): accepted, login works, `-HeadsetOff` ran, key
  removed again. All three setup login paths (reuse, password, failure text) are now verified.
- v0.3.0 was released 2026-11-10 (https://github.com/hakumaguro/vrcft-steam-frame/releases/tag/v0.3.0).

## v0.3.0 prep (2026-11-09/10; merged to master and released as v0.3.0 on 2026-11-10)

- 2026-11-10: the user tested a full day plus a long VR-sleep session on the 0.3.0 build ("really good"), then asked
  for an audit (code-review skill, two agents) before the merge. Fixed from it: uninstall/reinstall now keep
  `steamframe-learned.json` (`disabled-modules\steamframe-learned.backup.json`, sandbox-tested), the doctor no longer
  prints the "old headset part restart-loops" note on every run, README troubleshooting uses the doctor's exact wording,
  the "90 %" wink claim was dropped. Left as is (not blocking): learner constants (warm-up, step, percentile) are not
  config keys and `tune.py` does not measure the learner (checked with the replay tool instead); squint/sleepy
  machinery is larger than 0.3 needs; learned file beats a config edited while the module is stopped (a running module
  reseeds on a calibration); `eyeTrackingRateMin` 90 advice is from sasaken's docs only.
- Release notes (how-to + what changed, "no manual calibration") are in `dist/release-notes-0.3.0.md` (git-ignored).
  Publish only after: the SteamVR **stable** check below and the user's explicit go-ahead.

- Version 0.3.0 (csproj + common.ps1), README rewritten for the release: no beta box, "What you need" (SteamOS 0.4.3+,
  SteamVR 2.18.2+), "New in 0.3", menu numbers fixed everywhere, headset part = Advanced, calibration optional,
  troubleshooting rewritten (SteamVR 2.19.1+ asks whether to share eye data with PC apps: allow). Zip
  `dist/vrcft-steam-frame-0.3.0.zip` built (DLL 0.3.0.0), setup `-DryRun` from the unzipped copy picks the prebuilt DLL.
- SteamVR stable is 2.18.x (web, 2026-11-09); 2.19 is beta only. Gate before merge/push/release: switch SteamVR to the
  public branch (Steam > SteamVR > Properties > Betas > None), status must show `lids=steamvr`, then a wink check.
  master fast-forwards into the branch (master is an ancestor).
- Gotcha: Git Bash `sed -i` strips CR from CRLF files (`Start Here.cmd`, `.ps1`); use the Edit tool or re-add CR.

## Stage 2: learned open level (2026-11-09, uncommitted, deployed; not yet checked by the user in VRChat)

- `module/OpenLevelLearner.cs` (no VRCFT types; in the replay project too): per eye a 200-bin histogram of raw values,
  only new camera frames (gaze changed) with both eyes >= 0.45, decayed with `learnMinutes` (10) of **worn** time
  (no forgetting over gaps), 20 s warm-up after a 5 s gap, after a >= 5 min break the old weight is capped to a seed
  (1500 samples ~ 30 s) so a new wear takes over in ~1-2 min; level = **p40**, moves <= 0.02/s, clamp 0.5..1.
  Saved to `steamframe-learned.json` next to the config once a minute (and on Teardown); loaded as the seed at start,
  else the config's `steamVrLid.left/rightOpen`; a config change of those (calibration) reseeds.
- `steamVrLid.learnOpen` (null = on for SteamVR's lids, off for frameeyeosc), `learnMinutes`. `LidPipeline.Process`
  takes the learned levels (closed = configured or 0). Status `openFrom` (learned / calibrated / adaptive),
  `learnedOpen`; doctor prints it. With learning off or no level yet, the old code path runs unchanged.
- Why p40, not the median (replay `sweep` over fixed levels): left winks hardly depend on the levels; right winks depend
  on the LEFT open level (the left eye squints during a right wink): 20261109-122256 L open 0.70 / 0.775 / 0.85 ->
  right wink 61 / 48 / 25 %. So err low. Natural wear (trace 04:51-05:44, 0.5.8, gaze median -11 deg, so maybe not
  typical) gives p40 L 0.73 / R 0.92, p50 L 0.82 / R 0.97, vs that day's calibration L 0.79 / R 0.89. Replaying today's two
  sessions with the p40 levels: right wink 45 -> 60 % and 52 -> 54 %, left wink 68 -> 68 % and 78 -> 74 %, blinks same.
  Replay modes added: `learn`, `sweep`, `learnsynth` (sessions/20261009-squint-dev/replay; `LEARN_Q` env = percentile).
  Caveat: comparing learner runs on 80 s sessions mostly measures the warm-up (the first wink_right of 122256 falls
  before the learner has a level), not the level.
- Slow-camera warning fixed after a live false alarm: a headset that is not worn sends changing noise (~32/s, one eye
  "closed"). Now band 5-25/s on all changes AND >= 60 % of them with both eyes open (`GazeChangesOpen`).
- Rollback: `sessions/20261109-learn-open/SteamFrameVRCFTModule-before-learn.dll` (+ config copy), or set
  `steamVrLid.learnOpen` false (hot reload).
- Evening check (2026-11-09, ~7 min worn): learned L 0.95 / R 1.00 (rose from the 0.88 / 0.81 seed in ~4 min; tonight
  open eyes read raw 1.00 flat, so p40 = 1.0 is right). `live_check.py winks`: right wink 90 % (best so far; left eye raw
  0.24, held open by wink assist), left wink 42 % + 21 % both closed (tracker noisy: raw L jumps 0 <-> 0.8, raw R 0.4 <-> 1.0
  inside the hold), blinks 3/3 both eyes. Open steps right after a wink read out 0.57-0.62 (raw 0.76-0.83): reopening
  late or a sleepy look; ask the mirror.
- To do: the user wears it 3+ min in VRChat, then status `learnedOpen` and `sessions/20261009-squint-dev/live_check.py
  winks` (compare 76 / 72 % from 10-09 and today's 0.80 / 0.60-0.68) and the mirror. The fake-stream test (fake9015.py)
  was not re-run after the slow-band change (the headset was sending). tune.py still writes leftOpen/rightOpen on
  `--apply` (they become the seed); the spoken calibration is now optional ("tune winks").

## Stage 1 measured on SteamOS 0.5.8 beta + SteamVR 2.19.2 beta (2026-11-09)

- Sessions `20261109-121919` (dot covered by the meter, gaze rows useless) and `20261109-122256` (`--no-meter`, dot seen).
- Eye service on the headset: shm v5, same size; the sequence counter runs at **45/s** (was ~90), no `eyeTrackingRateMin`
  in the headset's steamvr.vrsettings, so it is the new default. Module `eyeRate` 44-45 while worn, not flagged (good).
- Lopsided blinks: 2 of 6 and 1 of 7 (96 ms) vs 10 of 11 on 2026-10-09: Valve's self-calibration probably helped.
  Winks OK in both sessions (left: other eye 0.80-0.81; right: other eye 0.60-0.68).
- Open levels: L 0.79 / 0.78, R 0.88 / 0.89 today vs L 0.898 / R 0.787 on 10-09 (the more open eye swapped sides):
  fixed levels go stale between days, stable within a day -> stage 2 (learn the open level) is worth it.
- Gaze vs the dot (`122256`): 4.7 deg (3.5 without the first late "left" step), offset correction 4.4, offset+gain 6.3,
  jitter 1.6, vertical gain ~1.2. Same as October: **no gaze correction / no dot calibration needed**. The analyzer's
  "not reliable" line came from the repeat difference (3.8, the late first "left").
- Bug found: during `record --gaze` the eyelid meter (centre, 2 m) covered the dot (3 m), so the user saw no dot.
  `Meter.pause()` now hides it during gaze steps (record loop). The "dot visible" test: all three overlays seen alone.
- frameeyeosc on the headset was found enabled and running, sending to the user's second PC.
  Turned off on request with `setup.ps1 -Headset ... -HeadsetOff -Yes`: the first end-to-end run of that path; it
  worked (disabled, inactive, files kept), the PC part changed only module.json, the three settings files unchanged.

## Stability changes (2026-11-09, uncommitted, deployed)

- Trace: `tune.py` (`Tracing`) no longer edits the config; it touches `%TEMP%\steamframe-trace.on` every second and the
  module traces while that file is < 3 s old (or config `trace` is true, by hand). Cause of the "trace always on": a
  cut-off run left `trace: true`, and every later run "restored" it (the `.bak` had it too). The user's config was set
  to `trace: false` (old file kept as `steamframe-config.json.before-trace-off`). `live_check.py` now uses `Tracing`.
- Eye-camera rate: `OscReceiver.GazeChanges` counts gaze messages whose value changed (normal median 87/s, min 44, from
  `sessions/20261002-dominant-eye/osc.csv`; SteamVR repeats values, so message counts don't show it). Status `eyeRate`
  (median of the last 10 s) and `eyeRateSlow` (8 of 10 seconds in 8-35/s on, <= 2 off; per second because an average
  passes through the band whenever the rate changes). Not worn reads 0-2/s, not flagged. Doctor warns, and names
  SteamVR's `eyeTrackingRateMin` (SteamOS 0.5.7+ can run the cameras at 15/s; from sasaken's docs, not seen by us yet).
- Data gaps: "eye data: none" is logged only after 5 s; shorter gaps are counted (`dropoutsLastMin`, a log line at most
  once a minute) and the eyelid / dominant-eye lines are not repeated. Doctor warns at >= 3 per minute. Behaviour is
  unchanged (neutral eyes after 1 s without data).
- Tested with a fake 9015 stream (`fake9015.py` in that session's scratchpad: 90/15/1 changes a second, gaps, heartbeat)
  and the doctor on hand-written status files. Not tested with the headset yet.
- Menu: 1 setup, 2 status, 3 calibrate, 4 live view, 5 folder, 6 uninstall, S start VRCFT (the menu says when it is not
  running), A advanced (1 headset set up, 2 `-HeadsetOff`). README, doctor and setup messages follow the new numbers.

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
- **Done 2026-10-09, uncommitted:** `setup.ps1 -Headset user@host -HeadsetOff` runs `headset-install.sh --off` (disable --now, pkill; unit,
  checkout and linger stay), doctor prints an info line (not a warning) when `lids=steamvr`, README has an "Updating from an older
  version" paragraph. Tested: dry run, `--off` on the real headset (already off: idempotent), syntax. Not tested: `-HeadsetOff` end to
  end through setup.ps1 (only the dry run). Checked the same day: SteamOS 0.4.5 stable keeps shm v5 and the same layout; SteamVR stable
  (build 25330290, BetaKey public) still sends NO lids (`lids=guessed`), so the branch waits for SteamVR 2.18.x on stable.
- **SteamOS 0.4.5 stable, tested 2026-10-09 (SteamVR beta 2.18.2 still):** shm v5 and layout unchanged. `headset-install.sh` on the real
  headset built the patched frameeyeosc (b9f0c01 + v5 patch), service active, no restart loop, module `source=frameeyeosc`. Calibrated
  on that path (session in `sessions/20261009-*`): closed reads 0.000 on both eyes, open L 0.82 / R 0.76, both winks OK, 5 of 8 blinks
  lopsided (glitch rule kept), gaze 3.2 deg from the dot (1.5 with offset+gain). The user checked blink/wink in VRChat: fine. Then
  frameeyeosc was turned off again (`--off`) and a second calibration on SteamVR's lids: winks OK (left 0.00/0.71, right 0.62/0.11),
  10 of 11 blinks lopsided (median 192 ms, max 352 ms; only `coupleMs` 180 covers them, there is no glitch rule on this path), gaze
  3.5 deg (1.1 with offset+gain). It wrote `steamVrLid` 0.898/0.787, `wink.threshold` 0.35, `range` 0.4, `coupleMs` 180. The `wink`
  section came out identical from both sources. SteamVR **stable** (build 25330290) still sends no lids (`lids=guessed`).
- **(was) To do before that release** (asked by the user): a step that turns frameeyeosc off on the headset for people who already
  enabled it with an older version (`systemctl --user disable --now frameeyeosc`; needs SSH or the headset's terminal, so a
  setup option plus README update instructions), and a doctor hint for updaters. Reason: the old build restart-loops on
  shared-memory version 5, and a working one makes the module prefer frameeyeosc over SteamVR's values. The replay tool and the test scripts are in `sessions/20261002-steamvr-lids-dev/`.

## Squint / sleepy eyelids (2026-10-09, uncommitted, not deployed; SteamVR's eyelids only)

- Goal (user, grilled 2026-10-09): partly closed lids shown at the right level (symptom a), and a one-eye squint not
  pushed into a wink (symptom d). Openness only, no EyeSquint expression. Agreed design in this order:
  - `steamVrLid.left/rightSquint/Sleepy` (raw) bend the output through knots at `targetSquint` 0.30 / `targetSleepy` 0.55.
    The bend is the **last** step (`LidCal.Curve`, after peak hold): every blink/wink rule still sees the linear level it
    was tuned on. Knots that are not strictly inside (0, 1) and in order after the linear map + deadband are skipped.
  - `sharpenBelow`: wink sharpening only while the lower eye is below it. `asymHysteresis`: a lopsided closure stays the
    same closure while the closed eye is below `asymClosed` + this. Without it, a one-eye squint hovering at asymClosed
    restarted the timer and pulled the open eye down again and again (synthetic test).
  - `filter` (One Euro, `filterMinCutoff` 1.5 Hz, `filterBeta` 3) on the linear level, before the rules. Without it, peak
    hold pins a noisy held lid to its noise minimum (synthetic: a 0.30 squint showed as 0.23-0.26).
  - These settings are per source (in `LidLevels`), all off by default. `tune.py analyze` turns them on for SLINK sessions
    only. For FRAME sessions it only prints the levels.
- `tune.py`: protocol + sleepy/squint x2 (3 s each), "open" steps 3 -> 2 s, gaze check (dot) only with `--gaze`
  (look-around step dropped without it): about 76 s. Point dropped (and said) when the two rounds differ > 0.10 raw, it is
  < 0.05 from a neighbour, or squint/sleepy read the same. SLINK sessions without these steps write the four levels as null.
  `sharpenBelow` = midpoint of the wink-closed p75 and the squint's linear level when they are >= 0.10 apart, else null.
  `levels_problem` mirrors the new `Validate` limits.
- Checked: 17 old sessions, all settings off: 0 rows differ from the committed pipeline (fixed and adaptive). 9 old
  sessions analyze identically (SLINK ones gain the four null keys). 6 SLINK sessions with filter + gate 0.2 + hysteresis
  0.15: blinks unchanged (one one-eye-only blink gone); winks within about 2 %, except a right-wink step where the tracker
  lowered the LEFT lid (20261002-234454): left-only 4 -> 10 %. Synthetic sessions (`sessions/20261009-squint-dev`,
  `make_synth.py`): levels recovered, squint 0.29-0.30 / sleepy 0.55 out, both drop rules work. Broken values are rejected
  by both `Validate` and `tune.py`.
- Not done: the headset run. Calibrate, then `sessions/20261009-squint-dev/live_check.py squint` (pass: within 0.10 of the
  target, sd < 0.03, one-eye squint keeps the other eye > 0.8), then `live_check.py winks` (not below 61 / 57 %), then the
  user checks in the VRChat mirror. Replay tool: `sessions/20261009-squint-dev/replay` (`same`, `steps`, `synth`).
- First live calibration (session `20261009-215907`, new DLL deployed, old DLL/config kept in
  `sessions/20261009-squint-dev/`):
  - SteamVR's lids read a squint as fully closed (L 0.00, R 0.04-0.09), so it cannot be a mid level.
  - Left sleepy was not repeatable (0.44 / 0.18).
  - 2 s "open" steps were too short: the user reopens about 1 s after the prompt, and once stayed closed through a whole
    open step. So the open levels came out at 0.74 / 0.60 (vs 0.898 / 0.787).
  - Decisions (user): squint step removed, open steps back to 3 s, sleepy 4 s x2 (76 s total); the open levels were set
    back to 0.898 / 0.787 by hand, with the mid levels null and filter + asymHysteresis left on.
  - `tune.py` now drops a point whose linear level is outside 0.15-0.90, and a point measured on one eye only.
  - Second try (`20261009-220525`): sleepy not repeatable again (L 0.00 / 0.15, R 0.14 / 0.44; in round 1 the lids drifted
    shut, in round 2 L 0.15 vs R 0.45 for one pose), and the user asked how far to close. Decisions (user):
    - The pose is explained during the open step before it, with a visible cue ("lower your lids until the top of the
      picture goes dark"). The pose step itself only says "Now. Hold." Total 81 s.
    - SteamVR's open level is now p50 of the open steps (was p20, pulled down by late reopening). FRAME stays p20; old
      FRAME analyze output is identical.
    - If the next try is still not repeatable: no more mid-level measuring. Tune one shared curve live instead, with the
      user in front of the VRChat mirror saying more / less, and the config edited by hand (hot reload).
- **Outcome (2026-10-09 evening), what is deployed:**
  - Third calibration (`224212`): sleepy repeatable, but L 0.64 vs R 0.20. A live check right after read the same pose as
    L 0.75 / R 0.72, so per-eye points do not carry over between sessions. Winks in that live check: left 76 %, right 72 %
    (were 61 / 57), blinks 3/3.
  - Decisions (user): Q25 one shared curve tuned at the mirror; Q26 `steamVrLid.sleepyLevel` (linear, both eyes, overrides
    the raw points, survives calibration). The user found linear 0.80 -> 0.55 "just right" at the mirror; installed config
    has `sleepyLevel` 0.8.
  - Calibration has no sleepy/squint steps any more (68 s). It still writes filter + asymHysteresis 0.15 for SteamVR
    sessions, and nulls the raw points.
  - Replay: raw points 0.728/0.740 vs `sleepyLevel` 0.8, max |diff| 0.0008 on 3 sessions.
  - Side effect of p50 open levels: old SLINK sessions' analyze calls `wink_right` FAIL more often, because the other eye's
    normalised level drops below 0.60. The label and the spoken text only; the recommended wink values are the same.
- Eyelid meter (user request, 2026-10-09): `overlay.py` command `meter <l> <r>` shows one head-locked overlay per eye (3x5
  pixel font, bars red < 0.1 < yellow < 0.5 < green). Together 9 deg wide, in the centre of view (user; was 14 deg below), 2 m.
  `tune.py` class `Meter` sends the newest trace row's raw values about 10 times a second ("-" when older than 0.5 s). It is on
  during every recording (`--no-meter`), and `tune.py meter` shows it alone. `Overlay.lock` serialises the meter thread and the
  dot.
  - **SteamVR limit:** `SetOverlayRaw` fails with error 23 (RequestFailed) after 200 uploads per process. Destroying and
    recreating the overlay does not help. The first version re-uploaded 10 times a second and froze about 20 s into the
    calibration, as the user saw. Now each eye's texture is an atlas of all 102 states, uploaded once; an update only calls
    SetOverlayTextureBounds (table index 30).
  - Tested in the headset: 1000 updates, and a silent run of the first 10 protocol steps (`Speaker(volume=0)`): 405 sends,
    no failure.
- Gaze hold on this path uses the level before the curve (`LidPipeline.LinearL/R`), so the curve does not change when the
  gaze freezes. A both-eye squint at linear < 0.5 still freezes it, as before.
- Prompts: the TTS takes 4.7-4.9 s for the long "as if ..." sentences (measured to a wav), so they were cut to "Sleepy eyes,
  half closed." / "Squint, like bright sun." (about 2.9 s with padding) in 4 s steps for round 1, and "Sleepy eyes." /
  "Squint." in 3 s steps for round 2. Total 78 s (103 s with `--gaze`).
- With the curve on (replay of 4 SLINK sessions with levels 0.35/0.6 and 0.3/0.55), a wink's non-assisted other eye
  shows lower: it squints in reality (linear 0.74 -> 0.57). So "other eye > 0.6" wink percentages drop (e.g. 56 -> 49 %)
  for a measurement reason, not because of a regression. Compare winks with the curve off, or agree on a new baseline. The
  assisted eye shows 0.92 instead of 0.95.

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
- `Start Here.cmd` user menu (must stay CRLF): 1 setup, 2 status, 3 calibrate, 4 live view, 5 config folder, 6 uninstall,
  S start VRCFT, A advanced (headset part).
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
shown open) -> wink sharpening -> peak hold (`blink.holdMs`). Optional, per source (off by default): a One Euro filter right
after the calibration, a `sharpenBelow` gate on wink sharpening, `asymHysteresis` for the lopsided timer, and the
squint/sleepy output curve as the very last step.
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

- The headset runs OpenSSH 10.x, which refuses a source address for a minute or so after several failed logins in a row
  (PerSourcePenalties): "Connection closed by <ip> port 22" / "banner exchange" before any password prompt. Seen 2026-11-10
  when a test script made 3 extra failing probes on top of setup's own 2. Wait ~3 minutes; when testing, do not probe with
  keys that are expected to fail. (Possible later hint in setup.ps1 for "Connection closed": wait and retry.)

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
`%TEMP%\steamframe-module.log` (state changes only, rotated to `.old` at 1 MB) and `%TEMP%\steamframe-trace.csv` (only while
`%TEMP%\steamframe-trace.on` is fresh or config `trace` is true; 30 ms samples `time,FRAME,lx,ly,rx,ry,rawL,rawR,outL,outR,loL,hiL,loR,hiR`, rotated at 20 MB).
`tune.py record` saves labelled sessions under `sessions/` (git-ignored). Headset: `journalctl --user -u frameeyeosc`.

## Conventions

- Keep tunables in `ModuleConfig` (hot-reloaded) rather than constants, and teach `tune.py analyze` to measure them.
- Don't push to the public repo until a change has been tested on the headset. Never commit IPs, SSH keys or personal paths
  (scan tracked files before pushing).
- Commits as the repo owner: `git -c user.name=Hakumaguro -c user.email=257888523+hakumaguro@users.noreply.github.com commit ...`.
- frameeyeosc is cloned and built on the headset, not vendored (credit: konsti219, MIT).
