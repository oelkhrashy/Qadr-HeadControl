"""Live test of the new pointer chain: steady when still, smooth when moving.

    .venv\\Scripts\\python tools\\live_pointer.py

1. Calibration that checks itself. Each step waits until it SEES the person
   do it, says so when they moved, turned the wrong way, or did not turn
   enough, and tries again. Nothing is accepted just because time passed.
2. Pointing: repeated camera frames are skipped, the head angles go through
   the Stabilizer (One Euro + sustained parking, thresholds from
   calibration), head position maps to pointer position on the MAIN screen
   only (comfortable left/right/up/down reach the edges), and the cursor
   glides between real pictures at 120 Hz.

Pointer only: this test never clicks.

Keys: hold Esc 0.5 s = stop and quit.   Ctrl+Alt+G = smooth / balanced /
direct glide.   Ctrl+Alt+R = re-centre on where you are looking now.
Moving the real mouse pauses head control for 1.5 s.

Each session's numbers (no images) go to %APPDATA%\\HeadMouse\\live-tests\\.
"""

from __future__ import annotations

import argparse
import ctypes
import math
import os
import statistics as st
import sys
import threading
import time
from ctypes import wintypes

import cv2

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)
from headmouse import storage, wincursor  # noqa: E402
from headmouse.stabilizer import (GLIDE_MODES, CrossCorrection,  # noqa: E402
                                  FrameGate, Glide, SmileHold, Stabilizer,
                                  still_stats)
from collections import deque  # noqa: E402
from headmouse.vision.mediapipe_tracker import MediaPipeTracker  # noqa: E402

DEG = 180.0 / math.pi
user32 = ctypes.WinDLL("user32", use_last_error=True)
VK_ESC, VK_CTRL, VK_ALT, VK_G, VK_R, VK_T = (0x1B, 0x11, 0x12, 0x47, 0x52,
                                             0x54)


def key(vk) -> bool:
    return bool(user32.GetAsyncKeyState(vk) & 0x8000)


def primary_monitor():
    """(left, top, width, height) of the MAIN screen."""
    class MONITORINFO(ctypes.Structure):
        _fields_ = [("cbSize", wintypes.DWORD), ("rcMonitor", wintypes.RECT),
                    ("rcWork", wintypes.RECT), ("dwFlags", wintypes.DWORD)]
    h = user32.MonitorFromPoint(wintypes.POINT(0, 0), 1)
    mi = MONITORINFO()
    mi.cbSize = ctypes.sizeof(MONITORINFO)
    user32.GetMonitorInfoW(h, ctypes.byref(mi))
    r = mi.rcMonitor
    return r.left, r.top, r.right - r.left, r.bottom - r.top


DIRS = [("left", "LEFT"), ("right", "RIGHT"), ("up", "UP"), ("down", "DOWN")]


class Session:
    def __init__(self, camera: int):
        self.camera = camera
        self.ui = {"title": "Starting the camera...", "sub": "", "png": None,
                   "state": "start", "quit": False, "mode": "smooth",
                   "fps": 0.0, "parked": False, "face": False}
        self.cal = {}
        self.ranges = {}
        self.stab = None
        self.glide = Glide("smooth")
        self.mon = primary_monitor()
        self.lock = threading.Lock()
        self.latest = None          # newest (t, yaw, pitch, smile)
        self.xcorr = None
        self.smile_hold = None
        self.smile_rest = 0.0
        self.sweeps = {}
        self.push_hist = deque(maxlen=30)
        self._was_holding = False
        self.held_pos = None
        self.scr_off = (0.0, 0.0)     # screen offset blended out after a hold
        self.last_target = None
        self.t_face = 0.0
        self.trace = []
        self.drawn = []             # every cursor write: (t, x, y)
        # what headmouse.movement_test needs from a runner
        import types
        self.origin_x, self.origin_y = self.mon[0], self.mon[1]
        self.cfg = types.SimpleNamespace(screen_w=self.mon[2],
                                         screen_h=self.mon[3],
                                         mode="position + parking + glide")
        self.feed = types.SimpleNamespace(peek=lambda: None)
        self.suspend_clicks = True
        self._listeners = []
        self._llock = threading.Lock()
        self.filtered = (0.0, 0.0)

    def add_tick_listener(self, fn):
        with self._llock:
            self._listeners.append(fn)

    def remove_tick_listener(self, fn):
        with self._llock:
            if fn in self._listeners:
                self._listeners.remove(fn)

    # ---- camera + face model + calibration + mapping ------------------
    def vision(self):
        cap = cv2.VideoCapture(self.camera, cv2.CAP_MSMF)
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        cap.set(cv2.CAP_PROP_FPS, 30)
        if not cap.isOpened():
            self.say("The camera did not open",
                     "Close any other app using it, then start again.")
            return
        tr = MediaPipeTracker()
        tr.open()
        gate = FrameGate()
        cal_thread = threading.Thread(target=self.calibrate, daemon=True)
        cal_thread.start()
        n = 0
        while not self.ui["quit"]:
            ok, f = cap.read()
            t = time.monotonic()
            if not ok:
                continue
            n += 1
            if self.ui["state"] != "live" and n % 3 == 0:
                small = cv2.flip(cv2.resize(f, (320, 240)), 1)
                ok2, buf = cv2.imencode(".png", small)
                if ok2:
                    import base64
                    self.ui["png"] = base64.b64encode(buf.tobytes()).decode()
            if not gate.is_new(f, t):
                continue                       # a repeated picture
            self.ui["fps"] = gate.real_fps
            sig = tr.process(f, t)
            if sig is None:
                self.ui["face"] = t - self.t_face < 0.5
                continue
            self.t_face = t
            self.ui["face"] = True
            yaw, pitch = sig.yaw * DEG, sig.pitch * DEG
            smile = float(sig.gestures.get("smile", 0.0))
            with self.lock:
                self.latest = (t, yaw, pitch, smile)
            if self.ui["state"] == "live" and self.stab is not None:
                cyaw, cpitch = (self.xcorr.apply(yaw, pitch, smile)
                                if self.xcorr else (yaw, pitch))
                fy, fp, parked = self.stab.update(t, cyaw, cpitch)
                self.filtered = (fy - self.cal["centre_yaw"],
                                 fp - self.cal["centre_pitch"])
                x, y = self.to_screen(fy, fp)
                holding = (self.smile_hold.update(t, smile)
                           if self.smile_hold else False)
                px = py = None
                if holding:
                    if not self._was_holding:
                        # back to where he was pointing before the smile
                        back = [h for h in self.push_hist if h[0] <= t - 0.15]
                        self.held_pos = (back[-1][1:] if back else
                                         (self.push_hist[-1][1:]
                                          if self.push_hist else (x, y)))
                        self.glide.push(t, *self.held_pos, True)
                    px, py = self.held_pos
                else:
                    if self._was_holding and self.held_pos is not None:
                        # whatever moved during the smile is blended in
                        # gradually, never jumped to
                        self.scr_off = (self.held_pos[0] - x,
                                        self.held_pos[1] - y)
                    if self.last_target is not None:
                        stp = math.hypot(x - self.last_target[0],
                                         y - self.last_target[1])
                        mag = math.hypot(*self.scr_off)
                        k = (max(0.0, 1.0 - 0.15 * stp / mag)
                             if mag > 0.5 else 0.0)
                        self.scr_off = (self.scr_off[0] * k,
                                        self.scr_off[1] * k)
                    px, py = x + self.scr_off[0], y + self.scr_off[1]
                    self.glide.push(t, px, py, parked)
                    self.push_hist.append((t, px, py))
                self.last_target = (x, y)
                self._was_holding = holding
                self.ui["parked"] = parked
                self.ui["smiling"] = holding
                self.trace.append((t, yaw, pitch, smile, cyaw, cpitch, fy, fp,
                                   x, y, int(parked), int(holding), px, py))
        cap.release()
        tr.close()

    def to_screen(self, yaw, pitch):
        l, tp, w, h = self.mon
        dy = yaw - self.cal["centre_yaw"]
        dp = pitch - self.cal["centre_pitch"]
        rx = self.ranges["right"] if dy > 0 else self.ranges["left"]
        ry = self.ranges["up"] if dp > 0 else self.ranges["down"]
        x = w / 2 + max(-1.0, min(1.0, dy / rx)) * (w / 2 - 2)
        y = h / 2 - max(-1.0, min(1.0, dp / ry)) * (h / 2 - 2)
        return l + x, tp + y

    # ---- calibration that checks it was really done ------------------
    def samples_since(self, t0):
        out = []
        last = None
        while True:
            with self.lock:
                cur = self.latest
            if cur is not None and cur is not last and cur[0] >= t0:
                out.append(cur)
                last = cur
            yield out
            time.sleep(0.01)

    def say(self, title, sub=""):
        self.ui["title"], self.ui["sub"] = title, sub

    def calibrate(self):
        while self.latest is None and not self.ui["quit"]:
            self.say("Looking for your face...",
                     "Sit so your whole face is in the picture.")
            time.sleep(0.1)
        self.ui["state"] = "calibrating"
        # 1. hold still, and prove it
        while not self.ui["quit"]:
            self.say("Look at this window and hold still",
                     "Relax, like you normally sit. 5 seconds.")
            time.sleep(1.0)
            t0 = time.monotonic()
            moved = lost = False
            for s in self.samples_since(t0):
                el = time.monotonic() - t0
                self.say("Hold still", f"{max(0, 5 - el):.0f}")
                if not self.ui["face"]:
                    lost = True
                    break
                if len(s) > 5:
                    my = st.median(x[1] for x in s)
                    mp = st.median(x[2] for x in s)
                    if abs(s[-1][1] - my) > 3 or abs(s[-1][2] - mp) > 3:
                        moved = True
                        break
                if el >= 5:
                    break
            if lost:
                self.say("I lost your face", "Let's try again.")
            elif moved:
                self.say("You moved a little", "No problem, let's try again.")
            elif len(s) >= 15:
                cal = still_stats([(x[0], x[1], x[2]) for x in s])
                # Proof of stillness, not just "did not jump 3 degrees":
                # the fifth live test accepted a hold with 6 deg/s moments,
                # which set the release speed to 7.5 deg/s and made the
                # pointer stick.
                if cal["still_speed_p95"] > 3.0 or cal["sway_std"] > 1.2:
                    self.say("You were moving a little",
                             "Relax and let your head rest. Let's try again.")
                else:
                    self.cal = cal
                    self.smile_rest = st.median(x[3] for x in s)
                    break
            else:
                self.say("Too few pictures", "Let's try again.")
            time.sleep(1.5)
        # 2. each direction, as far as comfortable, held
        for d, word in DIRS:
            if self.ui["quit"]:
                return
            self.ranges[d] = self.measure_direction(d, word)
        # what each axis does while only the other one moves
        self.xcorr = CrossCorrection(self.cal["centre_yaw"],
                                     self.cal["centre_pitch"])
        self.xcorr.learn_turns(self.sweeps.get("left", [])
                               + self.sweeps.get("right", []))
        self.xcorr.learn_nods(self.sweeps.get("up", [])
                              + self.sweeps.get("down", []))
        self.calibrate_smile()
        self.stab = Stabilizer(self.cal)
        self.say("Done", "The pointer follows your head now.")
        time.sleep(1.0)
        self.ui["state"] = "live"

    def deviation(self, d, yaw, pitch):
        dy = yaw - self.cal["centre_yaw"]
        dp = pitch - self.cal["centre_pitch"]
        return {"left": -dy, "right": dy, "up": dp, "down": -dp}[d]

    def measure_direction(self, d, word):
        opposite = {"left": "right", "right": "left", "up": "down",
                    "down": "up"}
        for attempt in range(1, 4):
            self.say(f"Turn your head {word} as far as is comfortable",
                     "and hold it there." if d in ("left", "right") else
                     "and hold it there.")
            t0 = time.monotonic()
            held_since = None
            result = None
            start = None
            for s in self.samples_since(t0):
                el = time.monotonic() - t0
                if el > 12:
                    break
                if not s:
                    continue
                if start is None:
                    start = (s[0][1], s[0][2])
                t, yaw, pitch = s[-1][:3]
                dev = self.deviation(d, yaw, pitch)
                # how far he has moved in the asked direction SINCE THIS
                # STEP STARTED. Being off-centre already is not doing it:
                # the fifth live test accepted "right" from a still head.
                moved = self.deviation(d, yaw, pitch) - self.deviation(
                    d, start[0], start[1])
                wrong = self.deviation(opposite[d], yaw, pitch)
                recent = [x for x in s if x[0] >= t - 0.6]
                spread = max(max(x[1] for x in recent) - min(x[1] for x in recent),
                             max(x[2] for x in recent) - min(x[2] for x in recent))
                if wrong >= 4 and spread < 2.5:
                    self.say(f"That is the other way: please turn {word}", "")
                    held_since = None
                    continue
                if dev >= 3 and moved >= 4 and spread < 2.5:
                    held_since = held_since or t
                    self.say(f"Hold it there...", f"{dev:.0f} degrees")
                    if t - held_since >= 0.8:
                        result = st.median(self.deviation(d, x[1], x[2])
                                           for x in recent)
                        break
                else:
                    held_since = None
                    if dev > 1:
                        self.say(f"A little more {word}, if it is comfortable",
                                 f"{dev:.0f} degrees")
            if result is not None:
                self.sweeps[d] = list(s)
                self.say("Got it", "Now come back to the middle.")
                self.wait_centre()
                return max(3.0, 0.85 * result)
            if attempt < 3:
                self.say(f"I didn't see you turn {word}",
                         f"Let's try again ({attempt + 1} of 3).")
                time.sleep(1.5)
        # could not do it: use the other side, never block
        self.say(f"Skipping {word}", "Using your other side's range.")
        time.sleep(1.5)
        return self.ranges.get(opposite[d], 10.0)

    def calibrate_smile(self):
        """Smile three times with the head still. Learns how much a smile
        drags the head reading, and how big his smile is. Checked: the
        head must stay still and at least two smiles must be seen."""
        rest = self.smile_rest
        for attempt in range(1, 4):
            self.say("Now keep your head still and smile three times",
                     "smile, relax, smile, relax, smile.")
            time.sleep(1.5)
            t0 = time.monotonic()
            for s in self.samples_since(t0):
                el = time.monotonic() - t0
                self.say("Smile, relax... three times", f"{max(0, 8 - el):.0f}")
                if el >= 8:
                    break
            if len(s) < 20:
                continue
            my = st.median(x[1] for x in s)
            mp = st.median(x[2] for x in s)
            turned = max(max(abs(x[1] - my), abs(x[2] - mp) * 0.0) for x in s)
            peaks, up = 0, False
            for x in s:
                if not up and x[3] > rest + 0.25:
                    peaks, up = peaks + 1, True
                elif up and x[3] < rest + 0.12:
                    up = False
            if turned > 5:
                self.say("Keep your head still while smiling", "Let's try again.")
            elif peaks < 2:
                self.say("I didn't see the smiles", "Let's try again.")
            else:
                self.xcorr.learn_smile(s, rest)
                self.smile_hold = SmileHold(rest, self.xcorr.smile_peak)
                self.say("Got it", "")
                time.sleep(1.0)
                return
            time.sleep(1.5)
        self.smile_hold = SmileHold(rest, 0.4)

    def wait_centre(self):
        t_ok = None
        t_start = time.monotonic()
        for s in self.samples_since(time.monotonic()):
            if time.monotonic() - t_start > 10:
                return
            if self.ui["quit"] or not s:
                if self.ui["quit"]:
                    return
                continue
            t, yaw, pitch = s[-1][:3]
            near = (abs(yaw - self.cal["centre_yaw"]) < 3
                    and abs(pitch - self.cal["centre_pitch"]) < 3)
            t_ok = (t_ok or t) if near else None
            if t_ok and t - t_ok > 0.5:
                return

    # ---- 120 Hz cursor writer ----------------------------------------
    def render(self):
        esc_since = None
        g_was = r_was = False
        last_written = None
        hands_off_until = 0.0
        while not self.ui["quit"]:
            now = time.monotonic()
            if key(VK_ESC):
                esc_since = esc_since or now
                if now - esc_since > 0.5:
                    self.ui["quit"] = True
                    break
            else:
                esc_since = None
            mods = key(VK_CTRL) and key(VK_ALT)
            g, r = mods and key(VK_G), mods and key(VK_R)
            if mods and key(VK_T) and self.ui["state"] == "live":
                self.ui["open_test"] = True
            if g and not g_was:
                modes = list(GLIDE_MODES)
                self.glide.mode = modes[(modes.index(self.glide.mode) + 1)
                                        % len(modes)]
                self.ui["mode"] = self.glide.mode
            if r and not r_was and self.latest is not None and self.stab:
                _, yw, pt, _ = self.latest
                self.cal["centre_yaw"], self.cal["centre_pitch"] = yw, pt
                if self.xcorr is not None:
                    self.xcorr.cy, self.xcorr.cp = yw, pt
                self.stab.reset()
                self.glide.clear()
            g_was, r_was = g, r
            if self.ui["state"] == "live":
                actual = wincursor.get_cursor()
                if last_written and math.hypot(actual[0] - last_written[0],
                                               actual[1] - last_written[1]) > 3:
                    hands_off_until = now + 1.5
                    last_written = None
                pos = self.glide.at(now)
                live = bool(pos) and now >= hands_off_until and self.ui["face"]
                if live:
                    x, y = int(round(pos[0])), int(round(pos[1]))
                    if last_written != (x, y):
                        wincursor.set_cursor(x, y)
                        last_written = wincursor.get_cursor()
                        self.drawn.append((now, x, y))
                if self._listeners:
                    cur = last_written or actual
                    sample = {"t": now, "x": cur[0], "y": cur[1],
                              "yaw": self.filtered[0],
                              "pitch": self.filtered[1],
                              "raw_yaw": self.filtered[0],
                              "raw_pitch": self.filtered[1],
                              "face": bool(self.ui["face"]), "active": live,
                              "blocked": "" if live else (
                                  "no face" if not self.ui["face"]
                                  else "helper has the mouse")}
                    with self._llock:
                        ls = list(self._listeners)
                    for fn in ls:
                        try:
                            fn(sample)
                        except Exception:
                            pass
            time.sleep(1 / 120)

    def save(self):
        if not self.trace:
            return None
        folder = os.path.join(storage.data_dir(), "live-tests")
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, time.strftime("%Y%m%d-%H%M%S") + ".csv")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(f"# cal {self.cal} ranges {self.ranges} "
                     f"xcorr {self.xcorr.summary() if self.xcorr else None}\n")
            fh.write("t,yaw,pitch,smile,c_yaw,c_pitch,f_yaw,f_pitch,x,y,"
                     "parked,smile_hold,cursor_x,cursor_y\n")
            for r in self.trace:
                fh.write(",".join(f"{v:.4f}" for v in r) + "\n")
        with open(path.replace(".csv", "-drawn.csv"), "w",
                  encoding="utf-8") as fh:
            fh.write("t,x,y\n")
            for r in self.drawn:
                fh.write(f"{r[0]:.4f},{r[1]},{r[2]}\n")
        return path


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera", type=int, default=0)
    args = ap.parse_args(argv)
    wincursor.make_dpi_aware()
    s = Session(args.camera)
    threading.Thread(target=s.vision, daemon=True).start()
    threading.Thread(target=s.render, daemon=True).start()

    import tkinter as tk
    root = tk.Tk()
    root.title("Head pointer test")
    root.attributes("-topmost", True)
    root.configure(bg="#16130f")
    l, tp, w, h = s.mon
    root.geometry(f"+{l + w // 2 - 340}+{tp + h // 2 - 230}")
    placed = {"corner": False}
    tests = {"open": None, "last": None}
    title = tk.Label(root, text="", font=("Arial Black", 22), bg="#16130f",
                     fg="#f6f1e7", wraplength=620, padx=20, pady=10)
    title.pack()
    sub = tk.Label(root, text="", font=("Arial", 15), bg="#16130f",
                   fg="#c9bda8")
    sub.pack()
    pic = tk.Label(root, bg="#16130f")
    pic.pack(pady=6)
    info = tk.Label(root, text="", font=("Consolas", 10), bg="#16130f",
                    fg="#8d8272", pady=6)
    info.pack()

    def tick():
        u = s.ui
        if u["quit"]:
            root.destroy()
            return
        if u["state"] == "live":
            if not placed["corner"]:
                root.geometry(f"+{l + 30}+{tp + 30}")
                placed["corner"] = True
            title.config(text="POINTER LIVE" if u["face"]
                         else "I can't see your face", fg="#b8d44a"
                         if u["face"] else "#e0662d")
            sub.config(text=("smile: holding the pointer" if u.get("smiling")
                             else "holding still" if u["parked"] else "moving")
                       + f"   |   glide: {u['mode']}")
            pic.config(image="")
            pic.image = None
            info.config(text=f"real camera {u['fps']:.0f} fps  |  hold Esc = "
                             "stop  Ctrl+Alt+T = movement test  Ctrl+Alt+G = "
                             "glide  Ctrl+Alt+R = re-centre")
            if u.get("open_test") and tests.get("open") is None:
                u["open_test"] = False
                from headmouse.movement_test import MovementTest

                def done(folder):
                    tests["open"] = None
                    tests["last"] = folder
                    print(f"movement test saved: {folder}")
                tests["open"] = MovementTest(root, s, on_done=done)
            u["open_test"] = False
        else:
            title.config(text=u["title"], fg="#f6f1e7")
            sub.config(text=u["sub"])
            if u["png"]:
                img = tk.PhotoImage(data=u["png"])
                pic.config(image=img)
                pic.image = img
            info.config(text=f"real camera {u['fps']:.0f} fps   |   hold Esc "
                             "= stop")
        root.after(80, tick)

    root.protocol("WM_DELETE_WINDOW", lambda: s.ui.update(quit=True))
    tick()
    root.mainloop()
    s.ui["quit"] = True
    time.sleep(0.3)
    path = s.save()
    print(f"calibration: {s.cal}")
    print(f"ranges (deg to reach each edge): {s.ranges}")
    print(f"learned corrections: {s.xcorr.summary() if s.xcorr else None}")
    if path:
        print(f"trace: {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
