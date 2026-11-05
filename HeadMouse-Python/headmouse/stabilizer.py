"""Steady when still, smooth when moving. Tested offline on a recorded
session first (tools/replay_lab.py, tools/glide_test.py).

Three pieces, each fitted to the person by calibration, none to one face:

  FrameGate   Many webcams repeat frames: this laptop camera, in a dim room,
              sends every real picture three times (30 "fps", 10 real). The
              face reading can only change on a real picture, so a cursor
              driven per frame sits for two frames and jumps on the third.
              Repeats are detected and dropped, which also saves two thirds
              of the face-model work, and the REAL picture rate is measured.

  Stabilizer  One Euro on the head angles (removes the face model's own
              small noise), plus sustained parking. Measured on a still
              hold, most of what moves is not model noise but the person:
              slow sway and breathing, 0.5-1 deg. No filter can tell that
              from movement by size, only by speed. So when the head moves
              slower than the person's own measured still-speed, the output
              holds; it lets go only when movement is SUSTAINED above it for
              three real pictures, or the head drifts further than three
              times their sway. On release the output continues from where it
              was held, and that small offset is paid back only while moving.
              Replayed: wander while still 33 px/s -> 1.5 px/s.

  Glide       Moves the cursor in a straight line between real pictures at
              the display rate, one real-picture interval behind, instead of
              jumping on each picture. Replayed: 9x smoother movement for
              about 90 ms more delay at 10 real fps (33 ms on a 30 fps
              camera). "balanced" renders half an interval behind and
              predicts the rest; "direct" does not glide at all.
"""

from __future__ import annotations

import math
import threading
from collections import deque

import numpy as np

from .filters import OneEuroFilter


# ---------------------------------------------------------------------------
class FrameGate:
    """True for a frame that is a new picture, False for a repeat."""

    def __init__(self, threshold: float = 0.05):
        self.threshold = threshold
        self._prev = None
        self._times: deque = deque(maxlen=40)
        self.delivered = 0
        self.real = 0

    def is_new(self, frame, t: float) -> bool:
        import cv2
        g = cv2.cvtColor(cv2.resize(frame, (160, 120)),
                         cv2.COLOR_BGR2GRAY).astype(np.int16)
        self.delivered += 1
        new = self._prev is None or \
            float(np.abs(g - self._prev).mean()) > self.threshold
        self._prev = g
        if new:
            self.real += 1
            self._times.append(t)
        return new

    @property
    def real_fps(self) -> float:
        if len(self._times) < 3:
            return 0.0
        return (len(self._times) - 1) / max(self._times[-1] - self._times[0],
                                             1e-6)


# ---------------------------------------------------------------------------
def still_stats(samples) -> dict:
    """Calibration of the still hold: [(t, yaw, pitch)] in degrees."""
    t = np.array([s[0] for s in samples])
    y = np.array([s[1] for s in samples])
    p = np.array([s[2] for s in samples])
    sp = []
    for i in range(len(t)):
        j = np.searchsorted(t, t[i] + 0.2)
        if j < len(t):
            sp.append(math.hypot(y[j] - y[i], p[j] - p[i]) / (t[j] - t[i]))
    sp = np.array(sp) if sp else np.array([0.0])
    return {
        "centre_yaw": float(np.median(y)), "centre_pitch": float(np.median(p)),
        "sway_std": float(math.hypot(y.std(), p.std())),
        "still_speed_p95": float(np.percentile(sp, 95)),
        "still_speed_max": float(sp.max()),
    }


class Stabilizer:
    def __init__(self, cal: dict, min_cutoff: float = 0.6, beta: float = 0.3,
                 v_k: float = 1.5, d_k: float = 2.0, sustain_s: float = 0.12,
                 payback: float = 0.15):
        self.fy = OneEuroFilter(min_cutoff, beta)
        self.fp = OneEuroFilter(min_cutoff, beta)
        # From his TYPICAL still speed (95th percentile), not the single
        # fastest twitch: the second live test set it from the maximum, one
        # twitch made it 4.5 deg/s, and slow starts stayed parked while the
        # head travelled 1-2 deg, then lurched (33 of 40 glitches).
        self.v_on = max(2.0, v_k * cal["still_speed_p95"])
        self.v_off = 0.5 * self.v_on
        self.d_on = max(0.8, d_k * cal["sway_std"])
        self.t_park = 0.3
        # sustained for a fixed TIME, not a number of pictures, so a 10 fps
        # camera does not wait three times longer than a 30 fps one
        self.sustain_s = sustain_s
        self._over_since = None
        # how much of the held-back distance is blended in per unit of
        # movement: small, so catching up never looks like a lurch
        self.payback = payback
        self.parked = False
        self._over_since = None
        self._slow_since = None
        self._hist: deque = deque()
        self._anchor = None
        self.out = None
        self._off = (0.0, 0.0)

    def reset(self) -> None:
        self.fy.reset()
        self.fp.reset()
        self._hist.clear()
        self.parked = False
        self._over_since = None
        self._slow_since = None
        self.out = None
        self._off = (0.0, 0.0)
        self._raw_prev = None

    def update(self, t: float, yaw: float, pitch: float):
        """Real pictures only. Returns (yaw, pitch, parked)."""
        fy, fp = self.fy.filter(yaw, t), self.fp.filter(pitch, t)
        raw_prev = getattr(self, "_raw_prev", None)
        self._raw_prev = (t, yaw, pitch)
        self._hist.append((t, fy, fp))
        while self._hist and t - self._hist[0][0] > 0.21:
            self._hist.popleft()
        a, b = self._hist[0], self._hist[-1]
        speed = (math.hypot(b[1] - a[1], b[2] - a[2]) / (b[0] - a[0])
                 if b[0] > a[0] else 0.0)
        if self.out is None:
            self.out = (fy, fp)
        # RAW speed over just the last picture, for an unmistakably fast
        # start. Raw, because the smoothing filter barely moves on the first
        # picture of a move; raw picture-to-picture noise is ~0.5 deg/s, a
        # real start is several times the release speed.
        if raw_prev is not None and t > raw_prev[0]:
            inst = math.hypot(yaw - raw_prev[1], pitch - raw_prev[2]) / (t - raw_prev[0])
        else:
            inst = 0.0
        if self.parked:
            if inst > 2.0 * self.v_on:
                # a clearly fast start: let go now, do not wait to see it
                # sustained (the third live test lost a whole picture
                # here, then the cursor had to jolt to catch up)
                self._over_since = t - self.sustain_s
            elif speed > self.v_on:
                self._over_since = self._over_since or t
            else:
                self._over_since = None
            sustained = (self._over_since is not None
                         and t - self._over_since >= self.sustain_s)
            drift = math.hypot(fy - self._anchor[0], fp - self._anchor[1])
            if sustained or drift > self.d_on:
                self.parked = False
                self._off = (self.out[0] - fy, self.out[1] - fp)
            else:
                return self.out[0], self.out[1], True
        if len(self._hist) >= 2:
            step = math.hypot(fy - self._hist[-2][1], fp - self._hist[-2][2])
            mag = math.hypot(*self._off)
            k = max(0.0, 1.0 - self.payback * step / mag) if mag > 1e-6 else 0.0
            self._off = (self._off[0] * k, self._off[1] * k)
        self.out = (fy + self._off[0], fp + self._off[1])
        if speed < self.v_off:
            self._slow_since = self._slow_since or t
            if t - self._slow_since >= self.t_park:
                self.parked = True
                self._over_since = None
                self._anchor = (fy, fp)
        else:
            self._slow_since = None
        return self.out[0], self.out[1], self.parked


# ---------------------------------------------------------------------------
GLIDE_MODES = {"smooth": 1.0, "balanced": 0.5, "direct": 0.0}


class Glide:
    """Cursor position at any moment from the real-picture updates that
    have ARRIVED so far (never one from the future)."""

    def __init__(self, mode: str = "smooth"):
        self.mode = mode
        self._ups: deque = deque(maxlen=16)
        self._lock = threading.Lock()

    def clear(self) -> None:
        with self._lock:
            self._ups.clear()

    def push(self, t: float, x: float, y: float, parked: bool) -> None:
        with self._lock:
            self._ups.append((t, x, y, parked))

    def at(self, now: float):
        with self._lock:
            ups = list(self._ups)
        if not ups:
            return None
        frac = GLIDE_MODES.get(self.mode, 1.0)
        if len(ups) < 2 or frac == 0.0:
            return ups[-1][1], ups[-1][2]
        gaps = sorted(b[0] - a[0] for a, b in zip(ups[-9:], ups[-8:]))
        d = min(max(gaps[len(gaps) // 2], 1 / 60), 0.2)
        tt = now - frac * d
        if tt >= ups[-1][0]:
            t1, x1, y1, parked = ups[-1]
            if frac < 1.0 and not parked:
                t0, x0, y0, _ = ups[-2]
                dt = max(t1 - t0, 1e-6)
                ahead = min(tt - t1, d)
                return x1 + (x1 - x0) / dt * ahead, y1 + (y1 - y0) / dt * ahead
            return x1, y1
        for i in range(len(ups) - 1, 0, -1):
            t0, x0, y0, _ = ups[i - 1]
            t1, x1, y1, _ = ups[i]
            if t0 <= tt <= t1:
                a = (tt - t0) / max(t1 - t0, 1e-6)
                return x0 + a * (x1 - x0), y0 + a * (y1 - y0)
        return ups[0][1], ups[0][2]


# ---------------------------------------------------------------------------
class CrossCorrection:
    """What one axis does when the person only moves the other, learned in
    calibration and subtracted while pointing.

    Measured on the first live test: turning 15 deg moved the up/down
    reading about 3 deg (150 px on his screen), and its shape changed
    between sessions with how he sat, so it cannot be a constant. The
    left/right steps record up/down all along the turn; the up/down steps
    record left/right along the nod. A smile step records how much a smile
    drags the reading (the face model fits head angle partly to cheek and
    jaw points, which move when smiling)."""

    def __init__(self, centre_yaw: float, centre_pitch: float):
        self.cy, self.cp = centre_yaw, centre_pitch
        self.pitch_vs_yaw = None
        self.yaw_vs_pitch = None
        self.smile_rest = 0.0
        self.k_smile_pitch = 0.0
        self.k_smile_yaw = 0.0
        self.smile_peak = 0.5

    @staticmethod
    def _table(a, b, bin_deg: float = 2.0, min_n: int = 3):
        a, b = np.asarray(a, float), np.asarray(b, float)
        if len(a) < 10:
            return None
        lo = math.floor(a.min() / bin_deg) * bin_deg
        xs, ys = [], []
        e = lo
        while e <= a.max():
            m = (a >= e) & (a < e + bin_deg)
            if m.sum() >= min_n:
                xs.append(float(a[m].mean()))
                ys.append(float(np.median(b[m])))
            e += bin_deg
        if len(xs) < 2:
            return None
        xs, ys = np.array(xs), np.array(ys)
        order = np.argsort(xs)
        xs, ys = xs[order], ys[order]
        ys = ys - float(np.interp(0.0, xs, ys))      # no correction at centre
        return xs, ys

    def learn_turns(self, samples) -> None:
        """samples: (t, yaw, pitch, ...) from the left and right steps."""
        self.pitch_vs_yaw = self._table([s[1] - self.cy for s in samples],
                                        [s[2] - self.cp for s in samples])

    def learn_nods(self, samples) -> None:
        self.yaw_vs_pitch = self._table([s[2] - self.cp for s in samples],
                                        [s[1] - self.cy for s in samples])

    def learn_smile(self, samples, rest: float) -> None:
        """samples: (t, yaw, pitch, smile) with the head still."""
        s = np.array([max(0.0, x[3] - rest) for x in samples])
        quiet = s < 0.05
        if quiet.sum() < 5 or (s > 0.2).sum() < 5:
            return
        base_y = float(np.median([x[1] for x, q in zip(samples, quiet) if q]))
        base_p = float(np.median([x[2] for x, q in zip(samples, quiet) if q]))
        dy = np.array([x[1] - base_y for x in samples])
        dp = np.array([x[2] - base_p for x in samples])
        den = float((s * s).sum())
        self.smile_rest = rest
        self.k_smile_pitch = float((s * dp).sum() / den)
        self.k_smile_yaw = float((s * dy).sum() / den)
        self.smile_peak = float(np.percentile(s, 95))

    def apply(self, yaw: float, pitch: float, smile: float = 0.0):
        dy, dp = yaw - self.cy, pitch - self.cp
        p_shift = (float(np.interp(dy, *self.pitch_vs_yaw))
                   if self.pitch_vs_yaw is not None else 0.0)
        y_shift = (float(np.interp(dp, *self.yaw_vs_pitch))
                   if self.yaw_vs_pitch is not None else 0.0)
        s = max(0.0, smile - self.smile_rest)
        return (yaw - y_shift - self.k_smile_yaw * s,
                pitch - p_shift - self.k_smile_pitch * s)

    def summary(self) -> dict:
        def at(tab, v):
            return round(float(np.interp(v, *tab)), 2) if tab is not None else None
        return {"pitch_shift_at_turn_-20": at(self.pitch_vs_yaw, -20),
                "pitch_shift_at_turn_+20": at(self.pitch_vs_yaw, 20),
                "yaw_shift_at_nod_+8": at(self.yaw_vs_pitch, 8),
                "yaw_shift_at_nod_-8": at(self.yaw_vs_pitch, -8),
                "smile_pitch_per_unit": round(self.k_smile_pitch, 2),
                "smile_yaw_per_unit": round(self.k_smile_yaw, 2)}


class SmileHold:
    """While a smile is on, the cursor holds where it was just BEFORE the
    smile started: the smile is a click, and the click belongs where he was
    pointing, not where the smile dragged the reading."""

    def __init__(self, rest: float, peak_excess: float):
        self.rest = rest
        self.on_at = rest + max(0.12, 0.45 * peak_excess)
        self.off_at = rest + max(0.08, 0.25 * peak_excess)
        self.holding = False
        self._below_since = None

    def update(self, t: float, smile: float) -> bool:
        if not self.holding:
            if smile >= self.on_at:
                self.holding = True
                self._below_since = None
        else:
            if smile < self.off_at:
                self._below_since = self._below_since or t
                if t - self._below_since >= 0.15:
                    self.holding = False
            else:
                self._below_since = None
        return self.holding
