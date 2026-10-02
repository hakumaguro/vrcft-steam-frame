#!/usr/bin/env python3
"""Tuning tool for the Steam Frame VRCFT module (stdlib only, Windows).

  tune.py calibrate         record + analyse + apply in one go, with spoken prompts and a spoken result
  tune.py record            guided calibration session only (spoken prompts + timestamped labels)
  tune.py analyze [DIR]     analyse a session, print findings and a recommended config
  tune.py analyze [DIR] --apply    ...and write the recommendation into the module's config (hot-reloaded)
  tune.py watch             live view of raw and output eyelid values
  tune.py show              print the module's current config
  tune.py dot               show the fixation dot in the headset at each calibration position (to check it is visible)

The module writes samples to %TEMP%\\steamframe-trace.csv only while `trace` is on in its config; this tool turns it
on for as long as it needs it and restores it afterwards.
While SteamVR runs, a recording shows a dot in the headset (tools/overlay.py) to look at; its known angles let analyze
report how far off the gaze is. Without the dot (SteamVR not running, or --no-dot) the recording uses spoken
directions only.
"""
import argparse
import csv
import glob
import json
import math
import os
import shutil
import statistics as st
import subprocess
import sys
import threading
import time
from datetime import datetime

TRACE = os.path.join(os.environ.get("TEMP", "."), "steamframe-trace.csv")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SESSIONS = os.path.join(ROOT, "sessions")

# (label, seconds, spoken instruction)
PROTOCOL = [
    ("open", 6, "Keep both eyes open and look straight ahead."),
    ("closed", 4, "Close both eyes."),
    ("open", 4, "Open."),
    ("wink_left", 4, "Wink your left eye only."),
    ("open", 3, "Open."),
    ("wink_right", 4, "Wink your right eye only."),
    ("open", 3, "Open."),
    ("closed", 4, "Close both eyes."),
    ("open", 3, "Open."),
    ("wink_left", 4, "Wink your left eye only."),
    ("open", 3, "Open."),
    ("wink_right", 4, "Wink your right eye only."),
    ("open", 3, "Open."),
    ("blinks", 12, "Blink normally, about once a second."),
    ("look", 8, "Look around. Left, right, up, down."),
    ("open", 2, "Done."),
]
# With the dot in the headset the "look" step becomes fixations at known angles (degrees, +x right, +y up), each
# position twice: analyze fits a correction on one pass and tests it on the other.
GAZE_TARGETS = [(0, 0), (-20, 0), (20, 0), (0, 15), (0, -15),
                (0, 0), (0, -15), (0, 15), (20, 0), (-20, 0)]
GAZE_SECS = 2.5
GAZE_NAMES = {(0, 0): "centre", (-20, 0): "left", (20, 0): "right", (0, 15): "up", (0, -15): "down"}
DOT_DIST = 3.0         # metres; far enough that the two eyes turn inwards by only ~0.6 degrees
IPD = 0.063            # assumed eye distance, for that inward angle
GAZE_SETTLE = 0.6      # seconds skipped after the dot jumps (reaction time + the eye movement)
GAZE_MIN_SAMPLES = 15
GUARD_START, GUARD_END = 1.3, 0.2
SPOKEN = []   # short sentences for the spoken result of analyze
MIN_LID_RANGE = 0.15   # raw open level must be at least this far above the closed level, per eye   # seconds skipped at each segment edge (prompt + reaction time)


# ---- helpers ---------------------------------------------------------------------------------------

def tod(s):
    """'HH:MM:SS.mmm' -> seconds since midnight."""
    h, m, x = s.split(":")
    return int(h) * 3600 + int(m) * 60 + float(x)


def stamp(t=None):
    t = t or time.time()
    lt = time.localtime(t)
    return time.strftime("%H:%M:%S", lt) + ".%03d" % int((t % 1) * 1000)


def speak(text):
    ps = ("Add-Type -AssemblyName System.Speech; "
          "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; $s.Rate = 1; $s.Speak('%s')" % text.replace("'", ""))
    return subprocess.Popen(["powershell", "-NoProfile", "-Command", ps], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


class Speaker:
    """One speech process for a whole recording. Starting PowerShell + System.Speech per prompt takes ~1.5 s on an idle PC
    (more while VRChat/SteamVR run), so prompts lagged their beeps and piled up. This starts the engine once (during the
    lead-in) and speaks each line immediately; a new line cuts off one that is still talking."""

    SCRIPT = (
        "Add-Type -AssemblyName System.Speech; "
        "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; $s.Rate = 1; $s.Volume = [int]$env:SF_TTS_VOLUME; "
        "$s.SetOutputToDefaultAudioDevice(); [Console]::Out.WriteLine('ready'); [Console]::Out.Flush(); "
        "while ($null -ne ($line = [Console]::In.ReadLine())) { "
        "  $s.SpeakAsyncCancelAll(); [void]$s.SpeakAsync($line); [Console]::Out.WriteLine('said'); [Console]::Out.Flush() } "
        "while ($s.State -eq 'Speaking') { Start-Sleep -Milliseconds 50 }"
    )

    def __init__(self, volume=100):
        env = dict(os.environ, SF_TTS_VOLUME=str(volume))
        self.p = subprocess.Popen(["powershell", "-NoProfile", "-NonInteractive", "-Command", self.SCRIPT],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                  text=True, encoding="utf-8", bufsize=1, env=env)
        self.ok = self.p.stdout.readline().strip() == "ready"

    def say(self, text):
        if not self.ok:                      # engine failed to start: fall back to the old one-shot way
            speak(text)
            return
        try:
            self.p.stdin.write(text.replace("\n", " ") + "\n")
            self.p.stdin.flush()
            self.p.stdout.readline()         # 'said': the line was handed to the engine
        except OSError:
            self.ok = False
            speak(text)

    def close(self, wait=True):
        try:
            self.p.stdin.close()
            if wait:
                self.p.wait(timeout=30)
        except Exception:
            self.p.kill()


class Overlay:
    """The fixation dot in the headset: tools/overlay.py as a child process, so a SteamVR fault cannot end a recording.
    `ok` is False (with `why`) when the dot cannot be shown; show() then returns False and the caller carries on."""

    def __init__(self, enabled=True):
        self.ok, self.why, self.p = False, "", None
        if not enabled:
            self.why = "turned off with --no-dot"
            return
        try:
            self.p = subprocess.Popen([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "overlay.py")],
                                      stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                      text=True, encoding="utf-8", bufsize=1)
        except OSError as e:
            self.why = str(e)
            return
        first = self._reply(8.0)
        self.ok = first == "ready"
        if not self.ok:
            self.why = first[7:] if first.startswith("error: ") else (first or "no answer from overlay.py")

    def _reply(self, timeout=2.0):
        box = []
        t = threading.Thread(target=lambda: box.append(self.p.stdout.readline().strip()), daemon=True)
        t.start()
        t.join(timeout)
        return box[0] if box else ""

    def _send(self, line):
        if not self.ok:
            return False
        try:
            self.p.stdin.write(line + "\n")
            self.p.stdin.flush()
            reply = self._reply()
        except (OSError, ValueError):
            reply = ""
        if reply != "ok":
            self.ok, self.why = False, reply or "overlay.py stopped answering"
        return self.ok

    def show(self, x_deg, y_deg):
        return self._send(f"show {x_deg} {y_deg} {DOT_DIST}")

    def hide(self):
        return self._send("hide")

    def close(self):
        if not self.p:
            return
        try:
            self.p.stdin.close()
            self.p.wait(timeout=5)
        except Exception:
            self.p.kill()


def beep(freq=880, ms=120):
    try:
        import winsound
        winsound.Beep(freq, ms)
    except Exception:
        print("\a", end="", flush=True)


def config_path():
    if os.environ.get("STEAMFRAME_CONFIG"):          # testing / non-standard installs
        return os.environ["STEAMFRAME_CONFIG"]
    hits = glob.glob(os.path.join(os.environ.get("APPDATA", ""), "VRCFaceTracking", "CustomLibs", "*", "steamframe-config.json"))
    if hits:
        return max(hits, key=os.path.getmtime)
    dlls = glob.glob(os.path.join(os.environ.get("APPDATA", ""), "VRCFaceTracking", "CustomLibs", "*", "SteamFrameVRCFTModule.dll"))
    return os.path.join(os.path.dirname(dlls[0]), "steamframe-config.json") if dlls else None


def _load_cfg(cp):
    return json.load(open(cp, encoding="utf-8-sig")) if os.path.exists(cp) else {}


def _save_cfg(cp, cfg):
    with open(cp, "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2)


class Tracing:
    """Turn the module's sample trace on while inside the `with` block, then put the setting back as it was."""

    def __enter__(self):
        self.cp = config_path()
        if not self.cp:
            speak_wait("The Steam Frame module is not installed. Run setup first.")
            sys.exit("The Steam Frame module is not installed (no config found). Run setup first.")
        cfg = _load_cfg(self.cp)
        self.had = "trace" in cfg
        self.prev = cfg.get("trace")
        if cfg.get("trace") is not True:
            cfg["trace"] = True
            _save_cfg(self.cp, cfg)
        return self

    def __exit__(self, *exc):
        cfg = _load_cfg(self.cp)
        if self.had:
            cfg["trace"] = self.prev
        else:
            cfg.pop("trace", None)
        _save_cfg(self.cp, cfg)


def wait_for_samples(timeout=4.0):
    """True once the trace grows, i.e. per-eye eyelid data is reaching the module with tracing on."""
    t_end = time.time() + timeout
    size0 = os.path.getsize(TRACE) if os.path.exists(TRACE) else 0
    while time.time() < t_end:
        time.sleep(0.3)
        if os.path.exists(TRACE) and os.path.getsize(TRACE) != size0:
            return True
    return False


def speak_wait(text):
    p = speak(text)
    try:
        p.wait(timeout=30)
    except Exception:
        pass


LID_SOURCE = {"FRAME": "frameeyeosc", "SLINK": "SteamVR"}
TRACE_TAG = "FRAME"    # which rows the last read_trace() used


def read_trace(path):
    """Rows: time,TAG,lx,ly,rx,ry,rawL,rawR,outL,outR,... -> (time, rawL, rawR, outL, outR, lx, ly, rx, ry).
    TAG is FRAME (frameeyeosc) or SLINK (eyelids sent by SteamVR, raw = 1 - closed, one gaze for both eyes). A session
    is analysed from one source only: the one with more rows; TRACE_TAG says which."""
    global TRACE_TAG
    by_tag, bad = {"FRAME": [], "SLINK": []}, 0
    with open(path, newline="", encoding="utf-8", errors="replace") as f:
        for p in csv.reader(f):
            if len(p) < 2 or p[1] not in by_tag:
                continue
            rows = by_tag[p[1]]
            if len(p) not in (12, 14):       # e.g. numbers written with a decimal comma split into extra columns
                bad += 1
                continue
            try:
                rows.append((tod(p[0]), float(p[6]), float(p[7]), float(p[8]), float(p[9]),
                             float(p[2]), float(p[3]), float(p[4]), float(p[5])))
            except ValueError:
                bad += 1
    if bad:
        print(f"warning: skipped {bad} malformed trace lines in {path}")
    TRACE_TAG = "SLINK" if len(by_tag["SLINK"]) > len(by_tag["FRAME"]) else "FRAME"
    return by_tag[TRACE_TAG]


def pct(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, max(0, int(round(p * (len(v) - 1)))))]


# ---- record ----------------------------------------------------------------------------------------

STATUS = os.path.join(os.environ.get("TEMP", "."), "steamframe-status.json")


def settings_or_exit():
    """The installed config values analyze depends on, checked with the module's own limits. Exits with a spoken
    message before any calculation (or recording) if the file is unreadable or a value is out of range."""
    cp = config_path()
    if not cp or not os.path.exists(cp):
        return {}, 0.06, 0.06
    problem = None
    try:
        cfg = _load_cfg(cp)
        if not isinstance(cfg, dict):
            raise ValueError("the file does not contain a JSON object")
    except (OSError, ValueError) as e:
        cfg, problem = {}, f"it cannot be read ({e})"
    db, a_closed = 0.06, 0.06
    if not problem:
        lid, wink = cfg.get("lid", {}), cfg.get("wink", {})
        if not isinstance(lid, dict) or not isinstance(wink, dict):
            problem = "the lid or wink section is missing or not an object"
        else:
            def num(v, lo, hi):
                return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) and lo <= v <= hi
            db, a_closed = lid.get("deadband", 0.06), wink.get("assistClosed", 0.06)
            if not num(db, 0.0, 0.45):
                problem = f"lid.deadband is {db!r}; it must be a number from 0 to 0.45"
            elif not num(a_closed, 0.0, 1.0):
                problem = f"wink.assistClosed is {a_closed!r}; it must be a number from 0 to 1"
    if problem:
        speak_wait("Your settings file has an error, so calibration cannot run. Nothing was changed. See the screen.")
        sys.exit(f"steamframe-config.json {problem}.\n  {cp}\n"
                 "Fix the value, restore steamframe-config.json.bak, or delete the file so the module writes the defaults. Nothing was changed.")
    return cfg, float(db), float(a_closed)


def ready_or_exit():
    """Before asking the user to sit through a recording, check VRCFT and fresh headset data; say what is missing."""
    msg = None
    try:
        stt = json.load(open(STATUS, encoding="utf-8"))
        age = time.time() * 1000 - stt.get("updated", 0)
    except Exception:
        stt, age = {}, 1e12
    if age > 5000:
        msg = ("VRCFaceTracking is not running, or the Steam Frame module is not active. Start VRCFaceTracking from Steam.",
               "VRCFaceTracking / the module is not running (no fresh status). Start VRCFaceTracking from Steam, then run the status check.")
    elif stt.get("source") != "frameeyeosc" and stt.get("lids") != "steamvr":
        msg = ("No eyelid data from the headset. Put the headset on, and run the status check.",
               f"No per-eye eyelid data (source: {stt.get('source')}, eyelids: {stt.get('lids')}). Wear the headset; "
               "run scripts\\doctor.ps1 if it persists.")
    if msg:
        speak_wait(msg[0])
        sys.exit(msg[1])
    settings_or_exit()   # a broken settings file would only fail after the 90 s recording


def cmd_record(a):
    ready_or_exit()
    with Tracing():
        if not wait_for_samples():
            speak_wait("No eye data from the headset, so calibration cannot start. Run the status check.")
            sys.exit("No per-eye eyelid data is reaching the module (headset off, or VRCFaceTracking not running).\n"
                     "Run the status check: powershell -ExecutionPolicy Bypass -File scripts\\doctor.ps1")
        return _record(a)


def protocol(with_dot):
    """The recording steps as (label, seconds, spoken text or None, dot position or None)."""
    steps = []
    for label, secs, text in PROTOCOL:
        if not with_dot:
            steps.append((label, secs, text, None))
        elif label == "look":
            steps += [("gaze", GAZE_SECS, None if i else "Follow the dot with your eyes.", t) for i, t in enumerate(GAZE_TARGETS)]
        else:
            steps.append((label, secs, text.replace("look straight ahead", "look at the dot"), None))
    return steps


def cmd_dot(a):
    dot = Overlay()
    if not dot.ok:
        speak_wait("The dot cannot be shown. Start Steam V R and put the headset on, then try again.")
        sys.exit(f"The dot cannot be shown: {dot.why}")
    voice = Speaker()
    try:
        voice.say("You should see a dot. It moves to the centre, left, right, up and down.")
        dot.show(0, 0)
        time.sleep(5)
        for t in GAZE_TARGETS[:5] + [(0, 0)]:
            if not dot.show(*t):
                sys.exit(f"The dot stopped working: {dot.why}")
            print(f"  dot {GAZE_NAMES[t]}")
            voice.say(GAZE_NAMES[t].capitalize() + ".")
            time.sleep(a.secs)
    finally:
        voice.close()
        dot.close()


def _record(a):
    out = os.path.join(SESSIONS, datetime.now().strftime("%Y%m%d-%H%M%S"))
    os.makedirs(out, exist_ok=True)
    t_lead = time.time()
    voice = Speaker()                        # started once, during the lead-in
    voice.say("Calibration starts soon. Put the headset on and keep your face relaxed.")
    dot = Overlay(not a.no_dot)
    steps = protocol(dot.ok)
    total = sum(s[1] for s in steps)
    if not dot.ok:
        print(f"(no dot in the headset: {dot.why}; spoken directions only, so no gaze check)")
    print(f"Session {out}\n{len(steps)} steps, about {total:.0f} s. Put the headset on now; you have {a.lead} s.")
    time.sleep(max(0.0, a.lead - (time.time() - t_lead)))
    labels = []
    t_start = time.time()
    try:
        for label, secs, text, target in steps:
            shown = dot.show(*(target or (0, 0)))        # the dot rests in the centre between the gaze steps
            t0 = time.time()
            print(f"  {label:<11} {secs:>4}s  {text or 'dot ' + GAZE_NAMES[target]}")
            beep()
            if text:
                voice.say(text)
            time.sleep(max(0.0, secs - (time.time() - t0)))
            tx, ty = target or ("", "")
            labels.append((stamp(t0), stamp(time.time()), label, tx, ty, DOT_DIST if target else "", int(shown) if target else ""))
        beep(660, 300)
    finally:
        voice.close()
        dot.close()
    time.sleep(0.5)
    with open(os.path.join(out, "labels.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["start", "end", "label", "tx", "ty", "dist", "shown"])
        w.writerows(labels)
    # keep only this session's part of the trace
    lo, hi = tod(stamp(t_start)) - 1, tod(stamp()) + 1
    with open(os.path.join(out, "trace.csv"), "w", newline="") as dst:
        for path in (TRACE + ".old", TRACE):          # the module rotates the trace at 20 MB
            if not os.path.exists(path):
                continue
            with open(path, newline="", encoding="utf-8", errors="replace") as src:
                for line in src:
                    try:
                        if lo <= tod(line.split(",", 1)[0]) <= hi:
                            dst.write(line)
                    except ValueError:
                        pass
    print(f"\nSaved. Analyse with:  python tools/tune.py analyze \"{out}\"")
    return out


# ---- analyze ---------------------------------------------------------------------------------------

def load_session(d):
    rows = read_trace(os.path.join(d, "trace.csv"))
    groups = {}
    with open(os.path.join(d, "labels.csv"), newline="") as f:
        for r in csv.DictReader(f):
            s, e = tod(r["start"]) + GUARD_START, tod(r["end"]) - GUARD_END
            seg = [x for x in rows if s <= x[0] <= e]
            groups.setdefault(r["label"], []).extend(seg)
    return rows, groups


def fixation(seg, blink):
    """The steady part of one dot step: (median, spread) of lx, ly, rx, ry, or None when too little is left.
    Blinks (and the 150 ms around them), values pinned at the +-1 limit and outliers (2.5 x IQR) are dropped."""
    bt = [x[0] for x in seg if blink(x)]
    keep = [x for x in seg if max(abs(v) for v in x[5:9]) < 0.999 and not any(abs(x[0] - b) <= 0.15 for b in bt)]
    if len(keep) >= GAZE_MIN_SAMPLES:
        lim = []
        for i in range(5, 9):
            v = [x[i] for x in keep]
            lim.append((st.median(v), 2.5 * max(pct(v, 0.75) - pct(v, 0.25), 0.004)))     # the trace has 0.001 steps
        keep = [x for x in keep if all(abs(x[i + 5] - m) <= w for i, (m, w) in enumerate(lim))]
    if len(keep) < GAZE_MIN_SAMPLES:
        return None
    return [st.median(x[i] for x in keep) for i in range(5, 9)], [st.pstdev(x[i] for x in keep) for i in range(5, 9)]


def gaze_check(d, rows, nL, nR, cfg, warn):
    """Report only: compare the gaze the module passes on with the known angles of the dot, and estimate what a per-eye
    offset (+ gain) correction would leave. Nothing here goes into the recommended config. All angles in degrees."""
    gz = cfg.get("gaze") if isinstance(cfg.get("gaze"), dict) else {}
    scale = gz.get("scale", 1.0)
    unit = [45.0 * scale * (-1 if gz.get(k) else 1) for k in ("invertX", "invertY", "invertX", "invertY")]   # lx, ly, rx, ry
    blink = lambda x: min(nL(x[1]), nR(x[2])) < 0.30
    steps, skipped = [], 0
    with open(os.path.join(d, "labels.csv"), newline="") as f:
        for r in csv.DictReader(f):
            if r["label"] != "gaze" or r.get("shown") != "1":     # spoken directions alone are not a known angle
                continue
            tx, ty, dist = float(r["tx"]), float(r["ty"]), float(r["dist"])
            seg = [x for x in rows if tod(r["start"]) + GAZE_SETTLE <= x[0] <= tod(r["end"]) - 0.1]
            fx = fixation(seg, blink)
            if not fx:
                skipped += 1
                continue
            x = dist * math.tan(math.radians(tx))
            # the left eye sits IPD/2 to the left of the point the dot is placed from, so for it the dot is further right
            true = [math.degrees(math.atan2(x + IPD / 2, dist)), ty, math.degrees(math.atan2(x - IPD / 2, dist)), ty]
            steps.append({"target": (tx, ty), "true": true, "meas": [m * u for m, u in zip(fx[0], unit)],
                          "sd": [s * abs(u) for s, u in zip(fx[1], unit)],
                          "lid": (st.median(nL(v[1]) for v in seg), st.median(nR(v[2]) for v in seg),
                                  st.median(v[3] for v in seg), st.median(v[4] for v in seg))})
    if not steps:
        if skipped:
            print(f"\nGaze check: no usable fixation in any of the {skipped} dot steps (blinking, or the eyes kept moving)")
        return

    def fit(pts, gain):
        """Per channel (offset, gain) with measured = offset + gain * true, by least squares; gain 1 when not fitted."""
        out = []
        for c in range(4):
            t, m = [p["true"][c] for p in pts], [p["meas"][c] for p in pts]
            var = sum((v - st.mean(t)) ** 2 for v in t)
            g = sum((a - st.mean(t)) * (b - st.mean(m)) for a, b in zip(t, m)) / var if gain and var > 1 else 1.0
            out.append((st.mean(m) - g * st.mean(t), g if abs(g) > 0.05 else 1.0))
        return out

    def errors(pts, model):
        """Per point and eye: distance in degrees between the (corrected) gaze and the dot."""
        e = []
        for p in pts:
            c = [(p["meas"][i] - model[i][0]) / model[i][1] - p["true"][i] for i in range(4)]
            e += [math.hypot(c[0], c[1]), math.hypot(c[2], c[3])]
        return e

    none = [(0.0, 1.0)] * 4
    print("\nGaze check against the dot (degrees, + = right / up; what the module passes on now):")
    print("  dot            left eye x      y     right eye x      y     off by  L      R")
    for p in steps:
        m, (el, er) = p["meas"], errors([p], none)
        print(f"  {GAZE_NAMES.get(p['target'], str(p['target'])):<7}{p['target'][0]:>4.0f}{p['target'][1]:>4.0f}   {m[0]:>8.1f} {m[1]:>6.1f}   {m[2]:>10.1f} {m[3]:>6.1f}   {el:>10.1f} {er:>6.1f}")
    if skipped:
        print(f"  ({skipped} dot steps had no usable fixation and are left out)")
    now = st.mean(errors(steps, none))
    report = {"steps": len(steps), "skipped": skipped, "errorNow": round(now, 2),
              "jitter": round(st.mean(s for p in steps for s in p["sd"]), 2)}
    print(f"  average distance from the dot now: {now:.1f}")

    # each position was shown twice: fit on one pass, test on the other, both ways round
    first, second, seen = [], [], set()
    for p in steps:
        (second if p["target"] in seen else first).append(p)
        seen.add(p["target"])
    both = {p["target"] for p in first} & {p["target"] for p in second}
    if len(both) >= 4:
        for name, key, gain in (("offset only", "errorOffset", False), ("offset + gain", "errorOffsetGain", True)):
            e = errors(second, fit(first, gain)) + errors(first, fit(second, gain))
            report[key] = round(st.mean(e), 2)
            print(f"  with a per-eye {name} correction: {st.mean(e):.1f}   (fitted on one pass, tested on the other)")
        a, b = {p["target"]: p["meas"] for p in first}, {p["target"]: p["meas"] for p in second}
        rep = [math.hypot(a[t][i] - b[t][i], a[t][i + 1] - b[t][i + 1]) for t in both for i in (0, 2)]
        report["repeat"] = round(st.mean(rep), 2)
        print(f"  the same dot looked at twice differs by {st.mean(rep):.1f}; no correction can do better than about that")
        best = min(report["errorOffset"], report["errorOffsetGain"])
        said = f"Gaze check: your eyes point {now:.1f} degrees off now. A correction would leave {best:.1f}."
    else:
        print("  too few positions seen twice to test a correction")
        said = f"Gaze check: your eyes point {now:.1f} degrees off."
    model = fit(steps, True)
    # "shown" only means SteamVR accepted the dot. Eyes that did not follow it give a flat or unrepeatable result.
    if skipped > len(steps) or report.get("repeat", 0) > 3 or max(abs(model[0][1]), abs(model[2][1])) < 0.3:
        report["doubtful"] = True
        print("  ! these numbers are not reliable: the dot was probably not visible, or the eyes did not follow it")
        said = "The gaze check did not work. The dot was probably not visible, or your eyes did not follow it."
    SPOKEN.append(said)
    names = ("left x", "left y", "right x", "right y")
    print("  fitted on all steps:  " + "   ".join(f"{n} {o:+.1f} x{g:.2f}" for n, (o, g) in zip(names, model)) + "   (offset, gain)")
    print(f"  jitter while looking at one dot: {report['jitter']:.2f}")
    report["fit"] = {n.replace(" ", "").replace("left", "l").replace("right", "r"): {"offset": round(o, 2), "gain": round(g, 3)}
                     for n, (o, g) in zip(names, model)}
    for axis, i, key in (("horizontal", 0, "invertX"), ("vertical", 1, "invertY")):
        if model[i][1] < 0 and model[i + 2][1] < 0:
            warn.append(f"the {axis} gaze runs the wrong way round: toggle gaze.{key}")
    if all(abs(p["meas"][1] - p["meas"][3]) < 0.05 for p in steps):
        print("  (the tracker reports one shared vertical angle for both eyes)")

    # the lids at the three heights: looking down lowers the upper lid, which the lid calibration reads as closing
    rows_ = {t[1]: [p["lid"] for p in steps if p["target"] == t] for t in ((0, 15), (0, 0), (0, -15))}
    if all(rows_.values()):
        lid = {ty: [st.mean(v[i] for v in ps) for i in range(4)] for ty, ps in rows_.items()}
        print("  eyelids while looking     up   centre   down    (1 = the calibrated open level; in brackets what the module sends)")
        for eye, i in (("left", 0), ("right", 1)):
            print(f"    {eye:<6}" + " " * 13 + "  ".join(f"{lid[ty][i]:.2f} ({lid[ty][i + 2]:.2f})" for ty in (15, 0, -15)))
        report["lid"] = {name: {"left": round(lid[ty][0], 3), "right": round(lid[ty][1], 3),
                                "leftOut": round(lid[ty][2], 3), "rightOut": round(lid[ty][3], 3)}
                         for name, ty in (("up", 15), ("centre", 0), ("down", -15))}
        low = min(lid[-15][2], lid[-15][3])
        if low < 0.8:
            warn.append(f"looking down 15 degrees lowers the eyelid the module sends to {low:.2f} of open")
    with open(os.path.join(d, "gaze-report.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)


def cmd_analyze(a):
    d = a.dir
    if not d:
        subs = sorted(glob.glob(os.path.join(SESSIONS, "*")))
        if not subs:
            sys.exit("No sessions yet. Run: python tools/tune.py record")
        d = subs[-1]
    cfg_now, db, a_closed = settings_or_exit()
    rows, g = load_session(d)
    need = ["open", "closed"]
    for n in need:
        if len(g.get(n, [])) < 10:
            speak_wait("Not enough data in that recording. Please calibrate again.")
            sys.exit(f"Not enough '{n}' samples ({len(g.get(n, []))}). Redo the recording.")
    dt = st.median(b[0] - a_[0] for a_, b in zip(rows, rows[1:]) if 0 < b[0] - a_[0] < 0.5)
    steamvr = TRACE_TAG == "SLINK"
    print(f"Session {d}: {len(rows)} samples, median interval {dt * 1000:.0f} ms, eyelids from {LID_SOURCE[TRACE_TAG]}\n")

    L = lambda seg: [x[1] for x in seg]
    R = lambda seg: [x[2] for x in seg]
    cal = {
        "leftOpen": pct(L(g["open"]), 0.20), "rightOpen": pct(R(g["open"]), 0.20),
        "leftClosed": pct(L(g["closed"]), 0.80), "rightClosed": pct(R(g["closed"]), 0.80),
    }
    print("Raw lid levels (0 = closed, 1 = open):")
    print(f"  left : open p20 {cal['leftOpen']:.2f} (median {st.median(L(g['open'])):.2f})   closed p80 {cal['leftClosed']:.2f} (median {st.median(L(g['closed'])):.2f})")
    print(f"  right: open p20 {cal['rightOpen']:.2f} (median {st.median(R(g['open'])):.2f})   closed p80 {cal['rightClosed']:.2f} (median {st.median(R(g['closed'])):.2f})")
    warn = []
    bad = [eye for eye in ("left", "right") if cal[eye + "Open"] - cal[eye + "Closed"] < MIN_LID_RANGE]
    if bad:
        which = " and ".join(bad)
        print(f"\nCalibration rejected: the {which} eye's open and closed readings are too close together or reversed "
              f"(need open at least {MIN_LID_RANGE} above closed). Nothing was changed; your previous settings are kept.")
        speak_wait(f"Calibration failed for the {which} eye, so nothing was changed. Keep the eye fully open and fully closed when asked, and try again.")
        sys.exit(2)
    for eye in ("left", "right"):
        if cal[eye + "Open"] - cal[eye + "Closed"] < 0.25:
            warn.append(f"{eye} eye: open and closed levels are only {cal[eye + 'Open'] - cal[eye + 'Closed']:.2f} apart, the tracker barely sees it close")

    nL = lambda v: (v - cal["leftClosed"]) / (cal["leftOpen"] - cal["leftClosed"])
    nR = lambda v: (v - cal["rightClosed"]) / (cal["rightOpen"] - cal["rightClosed"])
    # the two sources have their own raw units, so their levels are stored separately
    rec = {"steamVrLid" if steamvr else "lid": {k: round(v, 3) for k, v in cal.items()}, "wink": {}, "blink": {}}

    print("\nWinks (normalised: 0 = closed, 1 = open):")
    sep = []
    for name, want_low in (("wink_left", "L"), ("wink_right", "R")):
        seg = g.get(name, [])
        if len(seg) < 10:
            print(f"  {name}: no samples")
            continue
        l, r = st.median(nL(x[1]) for x in seg), st.median(nR(x[2]) for x in seg)
        lo, hi = (l, r) if want_low == "L" else (r, l)
        ok = lo < 0.30 and hi > 0.60
        print(f"  {name:<11} left {l:5.2f}  right {r:5.2f}   -> {'OK' if ok else 'FAIL'}")
        SPOKEN.append(("Left" if want_low == "L" else "Right") + " wink " + ("works." if ok else "is weak; the other eye follows."))
        if ok:
            sep.append(hi - lo)
        elif want_low == "L" and r < l - 0.3:
            warn.append("left wink lowered the RIGHT lid: the eyes may be swapped (set swapEyes true)")
        elif want_low == "R" and l < r - 0.3:
            warn.append("right wink lowered the LEFT lid: the tracker mixes the eyes on this wink, or the eyes are swapped")
        elif not ok:
            other = hi if lo < 0.30 else lo
            warn.append(f"{name}: the other eye follows ({other:.2f}), tracker cross-talk")
    op = [abs(nL(x[1]) - nR(x[2])) for x in g["open"]]
    base = pct(op, 0.95)
    rec["wink"]["threshold"] = round(max(0.08, min(0.35, base * 1.5)), 3)
    rec["wink"]["range"] = round(max(0.10, min(0.40, (st.median(sep) if sep else 0.4) * 0.7)), 3)
    print(f"  open-eye L/R mismatch p95 {base:.2f} -> wink.threshold {rec['wink']['threshold']}, wink.range {rec['wink']['range']}")

    # Wink assist: does the other eye squint while one eye is closed? Compare it (in the module's mapped units) with that eye's
    # level when both eyes are closed; if the two separate, the assist can tell a wink from a real closure.
    # db / a_closed: the settings the module will run with (validated in settings_or_exit; applying keeps them)
    mapped = lambda n: max(0.0, min(1.0, (n - db) / (1 - 2 * db)))
    mL = lambda x: mapped(nL(x[1]))
    mR = lambda x: mapped(nR(x[2]))
    # per eye, its level while both eyes are closed (p75: the both-closed step also contains ceiling-pinned glitch frames)
    closed_lvl = {"L": pct([mL(x) for x in g["closed"]], 0.75), "R": pct([mR(x) for x in g["closed"]], 0.75)}
    squint, mins = [], []
    for name, winking, other, oe in (("wink_left", mL, mR, "R"), ("wink_right", mR, mL, "L")):
        seg = [x for x in g.get(name, []) if winking(x) < a_closed]
        if len(seg) < 10:
            continue
        med = st.median(other(x) for x in seg)
        if 0.05 < med < 0.85:
            squint.append(name)
            if med > closed_lvl[oe] + 0.10:
                mins.append(closed_lvl[oe] + 0.08)
            else:
                warn.append(f"{name}: the other eye squints almost as much as when both eyes are closed; wink assist cannot separate them")
    if mins:
        rec["wink"].update({"assist": True, "assistMin": round(min(0.3, max(0.03, max(mins))), 3)})
        print(f"  the other eye squints during {', '.join(squint)} -> wink.assist on, assistMin {rec['wink']['assistMin']}")
        SPOKEN.append("Wink assist is on, so the open eye stays open while you wink.")

    bl = g.get("blinks", [])
    if len(bl) >= 30:
        # A blink shows up as any lid closing (the tracker often reports it lopsided: one lid ~0, the other ~1).
        ev, cur, gap = [], [], 0
        for x in bl:
            l, r = nL(x[1]), nR(x[2])
            if min(l, r) < 0.30:
                cur.append((l, r, x[1], x[2])); gap = 0
            elif cur:
                gap += 1
                if gap > 2:
                    ev.append(cur); cur, gap = [], 0
        if cur:
            ev.append(cur)
        ev = [e for e in ev if len(e) * dt < 0.8]                # longer closures are not blinks
        if ev:
            asym = [e for e in ev if max(max(l, r) for l, r, _, _ in e) > 0.6 and sum(1 for l, r, _, _ in e if abs(l - r) > 0.4) >= len(e) / 2]
            # Glitch signature: the "open" eye is pinned at its raw ceiling while the other lid is closed.
            SAT = 0.985
            sat = [e for e in asym if sum(1 for l, r, rl, rr in e if (rr >= SAT and l < 0.35) or (rl >= SAT and r < 0.35)) >= len(e) / 2]
            # the module has no ceiling rule for SteamVR's eyelids, so there every lopsided blink needs the time limit
            plain = asym if steamvr else [e for e in asym if e not in sat]
            print(f"\nBlinks: {len(ev)} detected; {len(asym)} lopsided ({len(sat)} pinned at the ceiling, {len(asym) - len(sat)} plain)")
            if asym:
                print(f"  lopsided duration: median {st.median(len(e) * dt * 1000 for e in asym):.0f} ms, max {max(len(e) * dt * 1000 for e in asym):.0f} ms")
            if len(sat) >= max(2, len(asym) / 2) and not steamvr:
                rec["blink"]["saturatedRaw"] = SAT
                print(f"  -> blink.saturatedRaw {SAT}: those close both eyes for as long as they last, however long")
            durs = sorted(len(e) * dt * 1000 for e in plain)
            # only the plain lopsided blinks need the time limit; long values would delay real winks
            rec["blink"]["coupleMs"] = int(max(100, min(180, (durs[-1] * 1.3 + 30) if durs else 154)))
            sym = [e for e in ev if e not in asym]
            if sym:
                print(f"  both-eye blinks: {len(sym)}, median duration {st.median(len(e) * dt * 1000 for e in sym):.0f} ms")
            rec["blink"]["holdMs"] = int(max(60, min(120, st.median(len(e) for e in ev) * dt * 1000 * 0.6 + 40)))   # long holds delay wink release
            if len(asym) > len(ev) / 2:
                warn.append("most blinks are reported lopsided by the tracker; "
                            + ("blink.coupleMs makes" if steamvr else "blink.saturatedRaw / coupleMs make") + " them close both eyes")
        else:
            print("\nBlinks: none detected in the blink step")

    gaze_check(d, rows, nL, nR, cfg_now, warn)

    print("\nRecommended config changes:")
    print(json.dumps(rec, indent=2))
    for w in warn:
        print("  ! " + w)
    if a.apply:
        cp = config_path()
        if not cp:
            sys.exit("Module config not found (is the module installed?)")
        cfg = json.load(open(cp, encoding="utf-8-sig")) if os.path.exists(cp) else {}
        shutil.copy(cp, cp + ".bak") if os.path.exists(cp) else None
        for sect, vals in rec.items():
            cfg.setdefault(sect, {}).update(vals)
        with open(cp, "w", encoding="utf-8") as f:
            json.dump(cfg, f, indent=2)
        print(f"\nApplied to {cp} (backup .bak). The module reloads it within a second.")
        if a.speak:
            speak_wait("Calibration applied. " + " ".join(SPOKEN))
    else:
        print("\n(not applied; add --apply to write it to the module config)")


# ---- watch / show ----------------------------------------------------------------------------------

def bar(v, w=24):
    n = int(round(max(0, min(1, v)) * w))
    return "#" * n + "." * (w - n)


def cmd_watch(a):
    with Tracing():
        _watch()


def _watch():
    print("Live eyelids (Ctrl+C to stop). raw = tracker, out = what VRCFT gets.")
    if not wait_for_samples():
        print("(no headset data yet; waiting)")
    try:
        while True:
            if not os.path.exists(TRACE):
                time.sleep(0.3)
                continue
            with open(TRACE, "rb") as f:
                f.seek(max(0, os.path.getsize(TRACE) - 600))
                lines = f.read().decode("utf-8", "replace").splitlines()
            last = next((l for l in reversed(lines) if ",FRAME," in l or ",SLINK," in l), None)
            if last:
                p = last.split(",")
                rl, rr, ol, orr = (float(p[i]) for i in (6, 7, 8, 9))
                sys.stdout.write(f"\rL raw {bar(rl)} {rl:4.2f} out {ol:4.2f} | R raw {bar(rr)} {rr:4.2f} out {orr:4.2f}  ")
                sys.stdout.flush()
            time.sleep(0.1)
    except KeyboardInterrupt:
        print()


def cmd_show(a):
    cp = config_path()
    print(cp or "module not installed")
    if cp and os.path.exists(cp):
        print(open(cp, encoding="utf-8-sig").read())


def cmd_calibrate(a):
    out = cmd_record(a)
    a.dir, a.apply, a.speak = out, True, True
    cmd_analyze(a)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("calibrate"); c.add_argument("--lead", type=int, default=10, help="seconds to put the headset on"); c.set_defaults(fn=cmd_calibrate)
    r = sub.add_parser("record"); r.add_argument("--lead", type=int, default=8, help="seconds to put the headset on"); r.set_defaults(fn=cmd_record)
    for p in (c, r):
        p.add_argument("--no-dot", action="store_true", help="spoken directions only: no dot in the headset, no gaze check")
    an = sub.add_parser("analyze"); an.add_argument("dir", nargs="?"); an.add_argument("--apply", action="store_true"); an.add_argument("--speak", action="store_true", help="speak the result"); an.set_defaults(fn=cmd_analyze)
    sub.add_parser("watch").set_defaults(fn=cmd_watch)
    sub.add_parser("show").set_defaults(fn=cmd_show)
    dt = sub.add_parser("dot"); dt.add_argument("--secs", type=float, default=3, help="seconds per position"); dt.set_defaults(fn=cmd_dot)
    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
