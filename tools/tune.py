#!/usr/bin/env python3
"""Tuning tool for the Steam Frame VRCFT module (stdlib only, Windows).

  tune.py record            guided calibration session (spoken prompts + timestamped labels)
  tune.py analyze [DIR]     analyse a session, print findings and a recommended config
  tune.py analyze [DIR] --apply    ...and write the recommendation into the module's config (hot-reloaded)
  tune.py watch             live view of raw and output eyelid values
  tune.py show              print the module's current config

The module appends samples to %TEMP%\\steamframe-trace.csv. `record` speaks instructions (so you can follow them
inside the headset), writes labels for every step, and stores trace + labels under sessions/<timestamp>/.
"""
import argparse
import csv
import glob
import json
import os
import shutil
import statistics as st
import subprocess
import sys
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
GUARD_START, GUARD_END = 1.3, 0.2   # seconds skipped at each segment edge (prompt + reaction time)


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


def beep(freq=880, ms=120):
    try:
        import winsound
        winsound.Beep(freq, ms)
    except Exception:
        print("\a", end="", flush=True)


def config_path():
    hits = glob.glob(os.path.join(os.environ.get("APPDATA", ""), "VRCFaceTracking", "CustomLibs", "*", "steamframe-config.json"))
    if hits:
        return max(hits, key=os.path.getmtime)
    dlls = glob.glob(os.path.join(os.environ.get("APPDATA", ""), "VRCFaceTracking", "CustomLibs", "*", "SteamFrameVRCFTModule.dll"))
    return os.path.join(os.path.dirname(dlls[0]), "steamframe-config.json") if dlls else None


def read_trace(path):
    """FRAME rows: time,FRAME,lx,ly,rx,ry,rawL,rawR,outL,outR,..."""
    rows = []
    with open(path, newline="", encoding="utf-8", errors="replace") as f:
        for p in csv.reader(f):
            if len(p) >= 10 and p[1] == "FRAME":
                try:
                    rows.append((tod(p[0]), float(p[6]), float(p[7]), float(p[8]), float(p[9])))
                except ValueError:
                    pass
    return rows


def pct(v, p):
    v = sorted(v)
    return v[min(len(v) - 1, max(0, int(round(p * (len(v) - 1)))))]


# ---- record ----------------------------------------------------------------------------------------

def cmd_record(a):
    if not os.path.exists(TRACE):
        sys.exit("No trace file. Is VRCFT running with the module and frameeyeosc sending?")
    size0 = os.path.getsize(TRACE)
    time.sleep(1.0)
    if os.path.getsize(TRACE) <= size0:
        sys.exit("The trace is not growing: no frameeyeosc data reaching the module. Fix that first.")
    out = os.path.join(SESSIONS, datetime.now().strftime("%Y%m%d-%H%M%S"))
    os.makedirs(out, exist_ok=True)
    total = sum(s for _, s, _ in PROTOCOL)
    print(f"Session {out}\n{len(PROTOCOL)} steps, about {total} s. Put the headset on now; you have {a.lead} s.")
    speak("Calibration starts soon. Put the headset on and keep your face relaxed.")
    time.sleep(a.lead)
    labels = []
    t_start = time.time()
    for label, secs, text in PROTOCOL:
        t0 = time.time()
        print(f"  {label:<11} {secs:>2}s  {text}")
        beep()
        speak(text)
        time.sleep(secs)
        labels.append((stamp(t0), stamp(time.time()), label))
    beep(660, 300)
    time.sleep(0.5)
    with open(os.path.join(out, "labels.csv"), "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["start", "end", "label"])
        w.writerows(labels)
    # keep only this session's part of the trace
    lo, hi = tod(stamp(t_start)) - 1, tod(stamp()) + 1
    with open(TRACE, newline="", encoding="utf-8", errors="replace") as src, open(os.path.join(out, "trace.csv"), "w", newline="") as dst:
        for line in src:
            try:
                if lo <= tod(line.split(",", 1)[0]) <= hi:
                    dst.write(line)
            except ValueError:
                pass
    print(f"\nSaved. Analyse with:  python tools/tune.py analyze \"{out}\"")


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


def cmd_analyze(a):
    d = a.dir
    if not d:
        subs = sorted(glob.glob(os.path.join(SESSIONS, "*")))
        if not subs:
            sys.exit("No sessions yet. Run: python tools/tune.py record")
        d = subs[-1]
    rows, g = load_session(d)
    need = ["open", "closed"]
    for n in need:
        if len(g.get(n, [])) < 10:
            sys.exit(f"Not enough '{n}' samples ({len(g.get(n, []))}). Redo the recording.")
    dt = st.median(b[0] - a_[0] for a_, b in zip(rows, rows[1:]) if 0 < b[0] - a_[0] < 0.5)
    print(f"Session {d}: {len(rows)} samples, median interval {dt * 1000:.0f} ms\n")

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
    for eye in ("left", "right"):
        if cal[eye + "Open"] - cal[eye + "Closed"] < 0.25:
            warn.append(f"{eye} eye: open and closed levels are only {cal[eye + 'Open'] - cal[eye + 'Closed']:.2f} apart, the tracker barely sees it close")

    nL = lambda v: (v - cal["leftClosed"]) / (cal["leftOpen"] - cal["leftClosed"])
    nR = lambda v: (v - cal["rightClosed"]) / (cal["rightOpen"] - cal["rightClosed"])
    rec = {"lid": {k: round(v, 3) for k, v in cal.items()}, "wink": {}, "blink": {}}

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
            plain = [e for e in asym if e not in sat]
            print(f"\nBlinks: {len(ev)} detected; {len(asym)} lopsided ({len(sat)} pinned at the ceiling, {len(plain)} plain)")
            if asym:
                print(f"  lopsided duration: median {st.median(len(e) * dt * 1000 for e in asym):.0f} ms, max {max(len(e) * dt * 1000 for e in asym):.0f} ms")
            if len(sat) >= max(2, len(asym) / 2):
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
                warn.append("most blinks are reported lopsided by the tracker; blink.saturatedRaw / coupleMs make them close both eyes")
        else:
            print("\nBlinks: none detected in the blink step")

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
    else:
        print("\n(not applied; add --apply to write it to the module config)")


# ---- watch / show ----------------------------------------------------------------------------------

def bar(v, w=24):
    n = int(round(max(0, min(1, v)) * w))
    return "#" * n + "." * (w - n)


def cmd_watch(a):
    print("Live eyelids (Ctrl+C to stop). raw = tracker, out = what VRCFT gets.")
    try:
        while True:
            with open(TRACE, "rb") as f:
                f.seek(max(0, os.path.getsize(TRACE) - 600))
                lines = f.read().decode("utf-8", "replace").splitlines()
            last = next((l for l in reversed(lines) if ",FRAME," in l), None)
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


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("record"); r.add_argument("--lead", type=int, default=8, help="seconds to put the headset on"); r.set_defaults(fn=cmd_record)
    an = sub.add_parser("analyze"); an.add_argument("dir", nargs="?"); an.add_argument("--apply", action="store_true"); an.set_defaults(fn=cmd_analyze)
    sub.add_parser("watch").set_defaults(fn=cmd_watch)
    sub.add_parser("show").set_defaults(fn=cmd_show)
    a = ap.parse_args()
    a.fn(a)


if __name__ == "__main__":
    main()
