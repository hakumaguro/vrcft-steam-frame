#!/usr/bin/env python3
"""Fixation dot in the headset for tune.py (stdlib only, Windows): a small SteamVR overlay locked to the head.

tune.py runs this as a child process, so a fault in the SteamVR runtime cannot take a recording down with it.
The first line on stdout is `ready` or `error: <reason>` (SteamVR not running, runtime too old, ...).
Commands on stdin, one per line, each answered with `ok` or `error: <reason>`:

  show <x_deg> <y_deg> <metres>    put the dot at that angle from straight ahead (+x right, +y up), at that distance
  hide
  meter <left> <right>             show the two raw eyelid values (0 closed .. 1 open) as numbers and bars, low in view
  meterhide

End of input removes the dot. Angles are per axis, atan2(x, d) and atan2(y, d), the way frameeyeosc reports gaze.
"""
import ctypes
import json
import math
import os
import sys

OVERLAY_INTERFACE = b"FnTable:IVROverlay_028"
# positions in the IVROverlay_028 function table (openvr_capi.h)
CREATE, DESTROY, SET_WIDTH, SET_BOUNDS, SET_TRANSFORM_DEVICE, SHOW, HIDE, SET_RAW = 1, 3, 22, 30, 35, 43, 44, 62
APP_BACKGROUND = 3      # EVRApplicationType: never starts SteamVR, fails when it is not running
HMD = 0                 # tracked device index of the headset
DOT_DEG = 2.0           # diameter of the dot as seen from the eye
SIZE = 96               # texture pixels
# Eyelid meter: one overlay per eye. SteamVR refuses SetOverlayRaw after 200 uploads per process (error 23, measured
# 2026-10-09), so each eye's texture is an atlas of every value (0.00 .. 1.00 and "-") uploaded once, and an update only
# moves the texture bounds to the right cell.
CELL_W, CELL_H, COLS = 60, 40, 8
CELLS = 102                         # 0..100 = value in hundredths, 101 = no data
ROWS = (CELLS + COLS - 1) // COLS
METER_DEG, METER_DOWN, METER_DIST = 9.0, 0.0, 2.0      # both panels together: width, degrees below centre, metres
# 3 x 5 pixel font for the meter
FONT = {"0": "111101101101111", "1": "010110010010111", "2": "111001111100111", "3": "111001111001111",
        "4": "101101111001001", "5": "111100111001111", "6": "111100111101111", "7": "111001001001001",
        "8": "111101111101111", "9": "111101111001111", ".": "000000000000010", "L": "100100100100111",
        "R": "110101110101101", "-": "000000111000000", " ": "000000000000000"}


def runtime_dll():
    """openvr_api.dll of the installed SteamVR runtime (listed in openvrpaths.vrpath)."""
    try:
        with open(os.path.join(os.environ.get("LOCALAPPDATA", ""), "openvr", "openvrpaths.vrpath"), encoding="utf-8") as f:
            runtimes = json.load(f).get("runtime") or []
    except (OSError, ValueError):
        runtimes = []
    for r in runtimes:
        p = os.path.join(r, "bin", "win64", "openvr_api.dll")
        if os.path.exists(p):
            return p
    raise RuntimeError("SteamVR is not installed (openvr_api.dll not found)")


def bullseye():
    """RGBA pixels: white disc, black rim and black centre point, so it shows on any background."""
    buf = bytearray(SIZE * SIZE * 4)
    c = (SIZE - 1) / 2
    for y in range(SIZE):
        for x in range(SIZE):
            r = math.hypot(x - c, y - c) / (SIZE / 2)
            v = 0 if r < 0.16 or r > 0.78 else 255
            a = max(0.0, min(1.0, (1.0 - r) * SIZE / 2))        # one pixel of soft edge
            i = (y * SIZE + x) * 4
            buf[i:i + 4] = bytes((v, v, v, int(a * 255)))
    return bytes(buf)


def meter_atlas(name):
    """RGBA atlas for one eye: per cell a dark translucent panel with the label + value (2 px per font pixel) and a bar."""
    W, H = CELL_W * COLS, CELL_H * ROWS
    buf = bytearray(bytes((0, 0, 0, 150)) * (W * H))

    def px(x, y, rgb):
        i = (y * W + x) * 4
        buf[i:i + 4] = bytes(rgb + (255,))

    for c in range(CELLS):
        ox, oy = (c % COLS) * CELL_W, (c // COLS) * CELL_H
        v = c / 100 if c <= 100 else None
        x0 = ox + 4
        for ch in name + " " + ("-" if v is None else f"{v:.2f}"):
            g = FONT.get(ch, FONT[" "])
            for j in range(15):
                if g[j] == "1":
                    for dy in range(2):
                        for dx in range(2):
                            px(x0 + (j % 3) * 2 + dx, oy + 5 + (j // 3) * 2 + dy, (255, 255, 255))
            x0 += 8
        bw = CELL_W - 8
        fill = int(round((v or 0.0) * bw))
        col = (90, 220, 90) if v is not None and v >= 0.5 else (240, 200, 60) if v is not None and v >= 0.1 else (230, 70, 70)
        for y in range(24, 34):
            for x in range(bw):
                px(ox + 4 + x, oy + y, col if x < fill else (70, 70, 70))
    return bytes(buf), W, H


class Dot:
    def __init__(self):
        self.lib = ctypes.CDLL(runtime_dll())
        self.lib.VR_InitInternal.restype = ctypes.c_void_p
        self.lib.VR_InitInternal.argtypes = [ctypes.POINTER(ctypes.c_int), ctypes.c_int]
        self.lib.VR_GetGenericInterface.restype = ctypes.c_void_p
        self.lib.VR_GetGenericInterface.argtypes = [ctypes.c_char_p, ctypes.POINTER(ctypes.c_int)]
        self.lib.VR_GetVRInitErrorAsEnglishDescription.restype = ctypes.c_char_p
        self.lib.VR_GetVRInitErrorAsEnglishDescription.argtypes = [ctypes.c_int]
        err = ctypes.c_int(0)
        self.lib.VR_InitInternal(ctypes.byref(err), APP_BACKGROUND)
        if err.value:
            raise RuntimeError(self._init_error(err.value))
        self.up = True
        table = self.lib.VR_GetGenericInterface(OVERLAY_INTERFACE, ctypes.byref(err))
        if err.value or not table:
            raise RuntimeError("this SteamVR has no " + OVERLAY_INTERFACE.decode() + ": " + self._init_error(err.value))
        self.table = ctypes.cast(table, ctypes.POINTER(ctypes.c_void_p))
        h = ctypes.c_uint64(0)
        self._call(CREATE, b"vrcft-steam-frame.fixation-dot", b"Steam Frame calibration dot", ctypes.byref(h),
                   types=[ctypes.c_char_p, ctypes.c_char_p, ctypes.POINTER(ctypes.c_uint64)])
        self.h = h
        px = bullseye()
        self._call(SET_RAW, self.h, px, SIZE, SIZE, 4,
                   types=[ctypes.c_uint64, ctypes.c_char_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_uint32])

    def _init_error(self, code):
        text = self.lib.VR_GetVRInitErrorAsEnglishDescription(code)
        return text.decode("utf-8", "replace") if text else f"error {code}"

    def _call(self, index, *args, types):
        fn = ctypes.CFUNCTYPE(ctypes.c_int, *types)(self.table[index])
        e = fn(*args)
        if e:
            raise RuntimeError(f"overlay call {index} failed with error {e}")

    def show(self, x_deg, y_deg, dist):
        x, y = dist * math.tan(math.radians(x_deg)), dist * math.tan(math.radians(y_deg))
        m = (ctypes.c_float * 4 * 3)((1, 0, 0, x), (0, 1, 0, y), (0, 0, 1, -dist))     # HmdMatrix34_t, -z is forward
        self._call(SET_WIDTH, self.h, 2 * dist * math.tan(math.radians(DOT_DEG / 2)), types=[ctypes.c_uint64, ctypes.c_float])
        self._call(SET_TRANSFORM_DEVICE, self.h, HMD, ctypes.byref(m),
                   types=[ctypes.c_uint64, ctypes.c_uint32, ctypes.POINTER(ctypes.c_float * 4 * 3)])
        self._call(SHOW, self.h, types=[ctypes.c_uint64])

    def hide(self):
        self._call(HIDE, self.h, types=[ctypes.c_uint64])

    def meter(self, left, right):
        if getattr(self, "m", None) is None:
            self.m = []
            panel = 2 * METER_DIST * math.tan(math.radians(METER_DEG / 2)) / 2       # width of one eye's panel
            y = -METER_DIST * math.tan(math.radians(METER_DOWN))
            for n, name in enumerate(("L", "R")):
                h = ctypes.c_uint64(0)
                self._call(CREATE, f"vrcft-steam-frame.lid-meter-{name}".encode(), f"Steam Frame eyelid meter {name}".encode(),
                           ctypes.byref(h), types=[ctypes.c_char_p, ctypes.c_char_p, ctypes.POINTER(ctypes.c_uint64)])
                self.m.append(h)
                t = (ctypes.c_float * 4 * 3)((1, 0, 0, (n - 0.5) * panel), (0, 1, 0, y), (0, 0, 1, -METER_DIST))
                self._call(SET_WIDTH, h, panel, types=[ctypes.c_uint64, ctypes.c_float])
                self._call(SET_TRANSFORM_DEVICE, h, HMD, ctypes.byref(t),
                           types=[ctypes.c_uint64, ctypes.c_uint32, ctypes.POINTER(ctypes.c_float * 4 * 3)])
                px, w, hh = meter_atlas(name)
                self._call(SET_RAW, h, px, w, hh, 4,
                           types=[ctypes.c_uint64, ctypes.c_char_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_uint32])
        for h, v in zip(self.m, (left, right)):
            c = 101 if v is None else max(0, min(100, int(round(v * 100))))
            u0, v0 = (c % COLS) / COLS, (c // COLS) / ROWS
            b = (ctypes.c_float * 4)(u0, v0, u0 + 1 / COLS, v0 + 1 / ROWS)       # VRTextureBounds_t uMin, vMin, uMax, vMax
            self._call(SET_BOUNDS, h, ctypes.byref(b), types=[ctypes.c_uint64, ctypes.POINTER(ctypes.c_float * 4)])
            self._call(SHOW, h, types=[ctypes.c_uint64])

    def meter_hide(self):
        for h in getattr(self, "m", None) or []:
            self._call(HIDE, h, types=[ctypes.c_uint64])

    def close(self):
        try:
            for h in [getattr(self, "h", None)] + (getattr(self, "m", None) or []):
                if h is not None:
                    self._call(DESTROY, h, types=[ctypes.c_uint64])
        except RuntimeError:
            pass
        if getattr(self, "up", False):
            self.lib.VR_ShutdownInternal()
            self.up = False


def main():
    def out(text):
        sys.stdout.write(text + "\n")
        sys.stdout.flush()

    dot = None
    try:
        try:
            dot = Dot()
        except (RuntimeError, OSError, AttributeError) as e:
            out(f"error: {e}")
            return 1
        out("ready")
        for line in sys.stdin:
            p = line.split()
            try:
                if p[:1] == ["show"] and len(p) == 4:
                    dot.show(float(p[1]), float(p[2]), float(p[3]))
                elif p == ["hide"]:
                    dot.hide()
                elif p[:1] == ["meter"] and len(p) == 3:
                    dot.meter(*(None if v == "-" else float(v) for v in p[1:]))
                elif p == ["meterhide"]:
                    dot.meter_hide()
                else:
                    raise ValueError("unknown command")
                out("ok")
            except (RuntimeError, ValueError) as e:
                out(f"error: {e}")
        return 0
    finally:
        if dot:
            dot.close()


if __name__ == "__main__":
    sys.exit(main())
