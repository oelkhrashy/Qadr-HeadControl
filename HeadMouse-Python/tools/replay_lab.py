"""Replay a recorded session through each pointer filter and score it.

    .venv\\Scripts\\python tools\\replay_lab.py [recording folder]

1. Runs MediaPipe over the recorded video with the original frame times
   (cached to pose.csv, so later runs are instant).
2. "Calibrates" from the recording itself, the way the app would on any
   face: the first still segment gives this person's noise, sway and
   still-speed; the movement segments give their range. Nothing is tuned
   to one face.
3. Runs every candidate filter and scores it:
     still segments  how far the cursor wanders (px), where 0 is perfect
     move segments   how rough the cursor is (px), how late it is (ms),
                     and how much of the real movement survives (%)
4. Saves a chart of the cursor for each candidate (lab.png).

Cursor pixels use position mapping onto a 1920x1080 main screen, with the
person's measured range filling the screen: the strictest mapping for
noise, since it magnifies the most.
"""

from __future__ import annotations

import csv
import glob
import json
import math
import os
import sys

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)
from headmouse import storage  # noqa: E402
from headmouse.filters import OneEuroFilter  # noqa: E402

SCREEN_W, SCREEN_H = 1920, 1080


# ---------------------------------------------------------------------------
def extract_pose(folder: str):
    cache = os.path.join(folder, "pose.csv")
    rows = list(csv.DictReader(open(os.path.join(folder, "frames.csv"))))
    if not os.path.exists(cache):
        from headmouse.vision.mediapipe_tracker import MediaPipeTracker
        tr = MediaPipeTracker()
        tr.open()
        cap = cv2.VideoCapture(os.path.join(folder, "video.avi"))
        with open(cache, "w", encoding="utf-8") as fh:
            fh.write("t,label,face,yaw,pitch,roll,smile\n")
            for r in rows:
                ok, f = cap.read()
                if not ok:
                    break
                s = tr.process(f, float(r["t"]))
                if s is None:
                    fh.write(f"{r['t']},{r['label']},0,,,,\n")
                else:
                    d = 180 / math.pi
                    fh.write(f"{r['t']},{r['label']},1,{s.yaw*d:.5f},"
                             f"{s.pitch*d:.5f},{s.roll*d:.5f},"
                             f"{s.gestures.get('smile', 0):.4f}\n")
        tr.close()
    out = []
    for r in csv.DictReader(open(cache)):
        if r["face"] == "1":
            out.append((float(r["t"]), r["label"], float(r["yaw"]),
                        float(r["pitch"])))
    seg = []
    for r in rows:
        seg.append(int(r["segment"]))
    return out, rows


def segments(rows):
    """[(label, t_start, t_end)] from frames.csv."""
    segs, cur = [], None
    for r in rows:
        k = (int(r["segment"]), r["label"])
        t = float(r["t"])
        if cur is None or cur[0] != k:
            if cur is not None:
                segs.append((cur[0][1], cur[1], cur[2]))
            cur = [k, t, t]
        cur[2] = t
    if cur is not None:
        segs.append((cur[0][1], cur[1], cur[2]))
    return segs


# ---------------------------------------------------------------------------
def calibrate(data, segs):
    """What the app's calibration would learn, from this recording only."""
    first_still = next(s for s in segs if s[0] == "still")
    a, b = first_still[1] + 0.5, first_still[2]
    st = [(t, y, p) for t, _, y, p in data if a <= t <= b]
    t = np.array([x[0] for x in st])
    y = np.array([x[1] for x in st])
    p = np.array([x[2] for x in st])
    centre = (float(np.median(y)), float(np.median(p)))
    # speed while still, over 0.1 s
    sp = []
    for i in range(len(t)):
        j = np.searchsorted(t, t[i] + 0.1)
        if j < len(t):
            sp.append(math.hypot(y[j] - y[i], p[j] - p[i]) / (t[j] - t[i]))
    sp = np.array(sp)
    sp2 = []
    for i in range(len(t)):
        j = np.searchsorted(t, t[i] + 0.2)
        if j < len(t):
            sp2.append(math.hypot(y[j] - y[i], p[j] - p[i]) / (t[j] - t[i]))
    sp2 = np.array(sp2)
    # fast noise: frame-to-frame differences
    f2f = np.hypot(np.diff(y), np.diff(p))
    moves = [(tt, yy, pp) for tt, lab, yy, pp in data if lab == "move"]
    yr = max(abs(m[1] - centre[0]) for m in moves) if moves else 15.0
    pr = max(abs(m[2] - centre[1]) for m in moves) if moves else 10.0
    return {
        "centre": centre,
        "still_speed_p50": float(np.median(sp)),
        "still_speed_p95": float(np.percentile(sp, 95)),
        "still_speed_max": float(sp.max()),
        "still_speed2_p95": float(np.percentile(sp2, 95)),
        "still_speed2_max": float(sp2.max()),
        "sway_std": float(math.hypot(y.std(), p.std())),
        "f2f_rms": float(math.sqrt(np.mean(f2f ** 2))),
        # the range he actually used fills the screen, with 10% spare
        "range_yaw": max(4.0, 0.9 * yr), "range_pitch": max(3.0, 0.9 * pr),
        "fps": float((len(t) - 1) / (t[-1] - t[0])),
    }


# ---------------------------------------------------------------------------
class Raw:
    name = "raw (no filter)"

    def __init__(self, cal): pass

    def step(self, t, y, p): return y, p


class Euro:
    """The app's current filter, One Euro 0.6 / 0.3."""
    name = "One Euro (today's app)"

    def __init__(self, cal, mc=0.6, beta=0.3):
        self.fy, self.fp = OneEuroFilter(mc, beta), OneEuroFilter(mc, beta)

    def step(self, t, y, p):
        return self.fy.filter(y, t), self.fp.filter(p, t)


class Parked:
    """One Euro, plus parking: when his head is moving slower than his own
    still-speed, the output holds. It lets go when the head moves faster
    than that, or drifts further from where it parked than his sway.

    Everything comes from calibration: v_on from his still-speed, the drift
    limit from his sway. When it lets go, the output continues from where
    it was held (no jump), and that small offset is paid back only while he
    is moving, where it cannot be seen."""

    name = "One Euro + parking"

    def __init__(self, cal, mc=0.6, beta=0.3, adaptive=False):
        self.fy, self.fp = OneEuroFilter(mc, beta), OneEuroFilter(mc, beta)
        self.v_on = max(2.0, 1.3 * cal["still_speed_p95"])
        self.v_off = 0.6 * self.v_on
        self.d_on = max(0.8, 2.0 * cal["sway_std"])
        self.t_park = 0.3
        self.parked = False
        self.slow_since = None
        self.hist = []
        self.anchor = None          # input where it parked
        self.out = None
        self.off = (0.0, 0.0)       # output minus input after a release

    def step(self, t, y, p):
        fy, fp = self.fy.filter(y, t), self.fp.filter(p, t)
        self.hist.append((t, fy, fp))
        while self.hist and t - self.hist[0][0] > 0.12:
            self.hist.pop(0)
        if len(self.hist) >= 2 and self.hist[-1][0] > self.hist[0][0]:
            a, b = self.hist[0], self.hist[-1]
            speed = math.hypot(b[1] - a[1], b[2] - a[2]) / (b[0] - a[0])
        else:
            speed = 0.0
        if self.out is None:
            self.out = (fy, fp)
        if self.parked:
            drift = math.hypot(fy - self.anchor[0], fp - self.anchor[1])
            if speed > self.v_on or drift > self.d_on:
                self.parked = False
                self.off = (self.out[0] - fy, self.out[1] - fp)
            else:
                return self.out
        # moving: follow, and pay the offset back in proportion to motion
        if self.hist and len(self.hist) >= 2:
            step = math.hypot(fy - self.hist[-2][1], fp - self.hist[-2][2])
            k = max(0.0, 1.0 - 0.5 * step / max(math.hypot(*self.off), 1e-6)) \
                if math.hypot(*self.off) > 1e-6 else 0.0
            self.off = (self.off[0] * k, self.off[1] * k)
        self.out = (fy + self.off[0], fp + self.off[1])
        if speed < self.v_off:
            self.slow_since = self.slow_since or t
            if t - self.slow_since >= self.t_park:
                self.parked = True
                self.anchor = (fy, fp)
        else:
            self.slow_since = None
        return self.out


class ParkedSustained(Parked):
    """Parking that lets go only on SUSTAINED movement: speed measured over
    0.2 s must stay above his still-speed for 3 frames in a row, or the
    head must drift further than 3x his sway. Sway has short speed spikes;
    a real move keeps going."""
    name = "One Euro + sustained parking"

    def __init__(self, cal):
        super().__init__(cal)
        self.v_on = max(2.0, 1.2 * cal["still_speed2_max"])
        self.v_off = 0.5 * self.v_on
        self.d_on = max(1.0, 3.0 * cal["sway_std"])
        self.need = 3
        self.over = 0
        self.unparks = []

    def step(self, t, y, p):
        fy, fp = self.fy.filter(y, t), self.fp.filter(p, t)
        self.hist.append((t, fy, fp))
        while self.hist and t - self.hist[0][0] > 0.21:
            self.hist.pop(0)
        a, b = self.hist[0], self.hist[-1]
        speed = (math.hypot(b[1] - a[1], b[2] - a[2]) / (b[0] - a[0])
                 if b[0] > a[0] else 0.0)
        if self.out is None:
            self.out = (fy, fp)
        if self.parked:
            self.over = self.over + 1 if speed > self.v_on else 0
            drift = math.hypot(fy - self.anchor[0], fp - self.anchor[1])
            if self.over >= self.need or drift > self.d_on:
                self.parked = False
                self.unparks.append(t)
                self.off = (self.out[0] - fy, self.out[1] - fp)
            else:
                return self.out
        if len(self.hist) >= 2:
            step = math.hypot(fy - self.hist[-2][1], fp - self.hist[-2][2])
            mag = math.hypot(*self.off)
            k = max(0.0, 1.0 - 0.5 * step / mag) if mag > 1e-6 else 0.0
            self.off = (self.off[0] * k, self.off[1] * k)
        self.out = (fy + self.off[0], fp + self.off[1])
        if speed < self.v_off:
            self.slow_since = self.slow_since or t
            if t - self.slow_since >= self.t_park:
                self.parked = True
                self.over = 0
                self.anchor = (fy, fp)
        else:
            self.slow_since = None
        return self.out


class ParkedStrong(Parked):
    """Parking, with a stronger One Euro at low speed (more smoothing
    while aiming slowly, still light when moving fast)."""
    name = "One Euro (strong) + parking"

    def __init__(self, cal):
        super().__init__(cal, mc=0.25, beta=0.6)


CANDIDATES = [Raw, Euro, Parked, ParkedSustained]


# ---------------------------------------------------------------------------
def to_px(cal, y, p):
    cy, cp = cal["centre"]
    x = SCREEN_W / 2 + (y - cy) / cal["range_yaw"] * SCREEN_W / 2
    yy = SCREEN_H / 2 - (p - cp) / cal["range_pitch"] * SCREEN_H / 2
    return x, yy


def centred_smooth(t, v, half=0.15):
    out = np.empty_like(v)
    j0 = j1 = 0
    for i in range(len(t)):
        while t[j0] < t[i] - half:
            j0 += 1
        while j1 < len(t) and t[j1] <= t[i] + half:
            j1 += 1
        out[i] = v[j0:j1].mean()
    return out


def score(cal, data, segs, cand):
    f = cand(cal)
    T, X, Y = [], [], []
    for t, lab, y, p in data:
        oy, op = f.step(t, y, p)
        x, yy = to_px(cal, oy, op)
        T.append(t); X.append(x); Y.append(yy)
    T, X, Y = np.array(T), np.array(X), np.array(Y)
    # reference: the raw pose smoothed WITHOUT delay (offline, centred)
    rx, ry = zip(*[to_px(cal, y, p) for _, _, y, p in data])
    rx = centred_smooth(T, np.array(rx)); ry = centred_smooth(T, np.array(ry))
    res = {"name": cand.name, "still": [], "move": []}
    for lab, a, b in segs:
        if lab == "still":
            m = (T >= a + 0.7) & (T <= b)          # let it settle first
            if m.sum() < 5:
                continue
            xs, ys = X[m], Y[m]
            path = float(np.hypot(np.diff(xs), np.diff(ys)).sum())
            exc = float(np.hypot(xs - xs[0], ys - ys[0]).max())
            res["still"].append((path / (b - a - 0.7), exc))
        elif lab == "move":
            m = (T >= a) & (T <= b + 0.5)
            if m.sum() < 5:
                continue
            xs, ys, ts = X[m], Y[m], T[m]
            # roughness: how much the cursor's per-frame step shakes
            # around its own smooth version (px per frame)
            vx, vy = np.diff(xs), np.diff(ys)
            svx = centred_smooth(ts[1:], vx, 0.1); svy = centred_smooth(ts[1:], vy, 0.1)
            rough = float(np.sqrt(np.mean((vx - svx) ** 2 + (vy - svy) ** 2)))
            # lag: shift that best lines the output up with the reference
            best, lag = 1e18, 0
            for d in range(0, 15):
                e = np.mean((xs[d:] - rx[m][:len(xs) - d]) ** 2
                            + (ys[d:] - ry[m][:len(ys) - d]) ** 2)
                if e < best:
                    best, lag = e, d
            lag_ms = 1000 * lag / cal["fps"]
            ref_span = max(np.ptp(rx[m]), np.ptp(ry[m]))
            out_span = max(np.ptp(xs), np.ptp(ys))
            res["move"].append((rough, lag_ms, 100 * out_span / max(ref_span, 1)))
    res["T"], res["X"], res["Y"] = T, X, Y
    return res


def chart(results, segs, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    n = len(results)
    fig, axes = plt.subplots(n, 1, figsize=(14, 2.6 * n), sharex=True)
    t0 = results[0]["T"][0]
    for ax, r in zip(axes, results):
        for lab, a, b in segs:
            if lab == "move":
                ax.axvspan(a - t0, b - t0, color="#e0662d", alpha=0.12)
        ax.plot(r["T"] - t0, r["X"] - SCREEN_W / 2, color="#b3261e", lw=1,
                label="cursor x")
        ax.plot(r["T"] - t0, r["Y"] - SCREEN_H / 2, color="#8a5a00", lw=1,
                label="cursor y")
        ax.set_title(r["name"], loc="left", fontsize=10)
        ax.set_ylabel("px from centre")
        ax.legend(loc="upper right", fontsize=8)
    axes[-1].set_xlabel("seconds (shaded = moving)")
    fig.tight_layout()
    fig.savefig(path, dpi=90)


def main(argv=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    if argv:
        folder = argv[0]
    else:
        folder = sorted(glob.glob(os.path.join(storage.data_dir(),
                                               "recordings", "*")))[-1]
    data, rows = extract_pose(folder)
    segs = segments(rows)
    cal = calibrate(data, segs)
    print(f"recording {os.path.basename(folder)}: {len(data)} frames with a "
          f"face, {cal['fps']:.1f} fps")
    print(f"calibration from the recording: still-speed p95 "
          f"{cal['still_speed_p95']:.2f} deg/s (max {cal['still_speed_max']:.2f})"
          f", sway {cal['sway_std']:.2f} deg, frame noise "
          f"{cal['f2f_rms']:.3f} deg, range used +/-{cal['range_yaw']:.1f} "
          f"yaw, +/-{cal['range_pitch']:.1f} pitch")
    print(f"  -> 1 deg of turning = {SCREEN_W / 2 / cal['range_yaw']:.0f} px, "
          f"1 deg of nodding = {SCREEN_H / 2 / cal['range_pitch']:.0f} px\n")
    results = [score(cal, data, segs, c) for c in CANDIDATES]
    print(f"{'filter':30s} | {'STILL: wander px/s':>18s} {'max px':>7s} | "
          f"{'MOVING: rough px':>16s} {'lag ms':>7s} {'kept %':>7s}")
    for r in results:
        sw = np.mean([s[0] for s in r["still"]]); sm = max(s[1] for s in r["still"])
        mr = np.mean([m[0] for m in r["move"]]); ml = np.mean([m[1] for m in r["move"]])
        mk = np.mean([m[2] for m in r["move"]])
        print(f"{r['name']:30s} | {sw:>18.1f} {sm:>7.0f} | {mr:>16.1f} "
              f"{ml:>7.0f} {mk:>7.0f}")
    png = os.path.join(folder, "lab.png")
    chart(results, segs, png)
    with open(os.path.join(folder, "calibration.json"), "w") as fh:
        json.dump(cal, fh, indent=2)
    print(f"\nchart: {png}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
