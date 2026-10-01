#!/usr/bin/env python3
"""Fixation dot in the headset for tune.py (stdlib only, Windows): a small SteamVR overlay locked to the head.

tune.py runs this as a child process, so a fault in the SteamVR runtime cannot take a recording down with it.
The first line on stdout is `ready` or `error: <reason>` (SteamVR not running, runtime too old, ...).
Commands on stdin, one per line, each answered with `ok` or `error: <reason>`:

  show <x_deg> <y_deg> <metres>    put the dot at that angle from straight ahead (+x right, +y up), at that distance
  hide

End of input removes the dot. Angles are per axis, atan2(x, d) and atan2(y, d), the way frameeyeosc reports gaze.
"""
import ctypes
import json
import math
import os
import sys

OVERLAY_INTERFACE = b"FnTable:IVROverlay_028"
# positions in the IVROverlay_028 function table (openvr_capi.h)
CREATE, DESTROY, SET_WIDTH, SET_TRANSFORM_DEVICE, SHOW, HIDE, SET_RAW = 1, 3, 22, 35, 43, 44, 62
APP_BACKGROUND = 3      # EVRApplicationType: never starts SteamVR, fails when it is not running
HMD = 0                 # tracked device index of the headset
DOT_DEG = 2.0           # diameter of the dot as seen from the eye
SIZE = 96               # texture pixels


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

    def close(self):
        try:
            if getattr(self, "h", None) is not None:
                self._call(DESTROY, self.h, types=[ctypes.c_uint64])
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
