"""Signal filters for head-pose -> cursor control.

Stdlib only. Every filter is a stateful object with one method:

    y = f.filter(x, t)      # t is a monotonic timestamp in seconds

and a reset(), because after a face dropout you must NOT let stale state
snap the cursor across the screen.
"""

from __future__ import annotations

import math
from collections import deque


# --------------------------------------------------------------------------
# No filter (baseline)
# --------------------------------------------------------------------------
class NoFilter:
    name = "none"

    def __init__(self, **_kw):
        pass

    def filter(self, x: float, t: float) -> float:
        return x

    def reset(self) -> None:
        pass


# --------------------------------------------------------------------------
# Moving average (the naive fix: trades jitter for lag, 1:1)
# --------------------------------------------------------------------------
class MovingAverage:
    def __init__(self, n: int = 5, **_kw):
        self.n = int(n)
        self.name = f"movavg{self.n}"
        self.buf: deque[float] = deque(maxlen=self.n)

    def filter(self, x: float, t: float) -> float:
        self.buf.append(x)
        return sum(self.buf) / len(self.buf)

    def reset(self) -> None:
        self.buf.clear()


# --------------------------------------------------------------------------
# One Euro Filter
# Casiez, Roussel & Vogel, "1 Euro Filter: A Simple Speed-based Low-pass
# Filter for Noisy Input in Interactive Systems", CHI 2012.
#
# Idea: a first-order low-pass whose cutoff rises with the estimated speed
# of the signal. Slow signal -> low cutoff -> heavy smoothing (kills tremor).
# Fast signal -> high cutoff -> light smoothing (kills lag).
# --------------------------------------------------------------------------
class _LowPass:
    def __init__(self) -> None:
        self.y: float | None = None

    def __call__(self, x: float, alpha: float) -> float:
        if self.y is None:
            self.y = x
        else:
            self.y = alpha * x + (1.0 - alpha) * self.y
        return self.y

    def reset(self) -> None:
        self.y = None


class OneEuroFilter:
    def __init__(self, freq: float = 30.0, min_cutoff: float = 1.0,
                 beta: float = 0.3, d_cutoff: float = 1.0, **_kw):
        self.freq = float(freq)          # nominal rate, only used before 2nd sample
        self.min_cutoff = float(min_cutoff)   # Hz, cutoff when the head is still
        self.beta = float(beta)          # how fast cutoff opens up with speed
        self.d_cutoff = float(d_cutoff)  # Hz, cutoff for the speed estimate itself
        self.name = f"oneeuro(fc={min_cutoff:g},b={beta:g})"
        self._x = _LowPass()
        self._dx = _LowPass()
        self._t_prev: float | None = None
        self._x_prev: float | None = None

    @staticmethod
    def _alpha(cutoff: float, dt: float) -> float:
        tau = 1.0 / (2.0 * math.pi * cutoff)
        return 1.0 / (1.0 + tau / dt)

    def filter(self, x: float, t: float) -> float:
        if self._t_prev is None:
            dt = 1.0 / self.freq
        else:
            dt = t - self._t_prev
            if dt <= 0.0:
                dt = 1.0 / self.freq
        # speed estimate, low-passed
        dx = 0.0 if self._x_prev is None else (x - self._x_prev) / dt
        dx_hat = self._dx(dx, self._alpha(self.d_cutoff, dt))
        # adaptive cutoff
        cutoff = self.min_cutoff + self.beta * abs(dx_hat)
        y = self._x(x, self._alpha(cutoff, dt))
        self._t_prev = t
        self._x_prev = x
        return y

    def reset(self) -> None:
        self._x.reset()
        self._dx.reset()
        self._t_prev = None
        self._x_prev = None


# --------------------------------------------------------------------------
# Outlier rejection: median-of-3 + per-sample slew limit.
#
# This is NOT part of the original hypothesis, but a single-frame landmark
# glitch is a step, and a low-pass filter passes a fraction of a step
# straight through. Measured in the rig.
# --------------------------------------------------------------------------
class SpikeGuard:
    """Outlier gate + slew limit.

    A single bad landmark frame is a *step*, and a low-pass filter passes a
    good fraction of a step straight through (measured: ~1050 px of pointer
    throw with One Euro alone). A median-of-3 also fails, because glitches
    routinely last two frames.

    So: if a sample deviates from the running estimate by more than
    max_dev_deg, hold the previous value instead. If it keeps deviating for
    hold_frames in a row it was not a glitch, it was a genuinely fast head
    movement, so accept it and jump to it. Cost: a real fast flick is
    delayed by hold_frames (100 ms at 30 fps). Then a slew limit catches
    anything that gets through.
    """

    def __init__(self, max_deg_per_s: float = 220.0, max_dev_deg: float = 4.0,
                 hold_frames: int = 3, **_kw):
        self.max_deg_per_s = float(max_deg_per_s)
        self.max_dev_deg = float(max_dev_deg)
        self.hold_frames = int(hold_frames)
        self.name = f"gate({max_dev_deg:g}deg/{hold_frames}f,{max_deg_per_s:g}deg/s)"
        self._y_prev: float | None = None
        self._t_prev: float | None = None
        self._held = 0

    def filter(self, x: float, t: float) -> float:
        if self._y_prev is None or self._t_prev is None:
            self._y_prev, self._t_prev = x, t
            self._held = 0
            return x

        dt = max(t - self._t_prev, 1e-4)
        dev = x - self._y_prev

        if abs(dev) > self.max_dev_deg and self._held < self.hold_frames:
            # looks like a glitch: hold
            self._held += 1
            self._t_prev = t
            return self._y_prev

        self._held = 0
        max_step = self.max_deg_per_s * dt
        v = x
        if abs(dev) > max_step:
            v = self._y_prev + math.copysign(max_step, dev)
        self._y_prev, self._t_prev = v, t
        return v

    def reset(self) -> None:
        self._y_prev = None
        self._t_prev = None
        self._held = 0


class Chain:
    """Run several filters in series."""

    def __init__(self, *stages):
        self.stages = [s for s in stages if s is not None]
        self.name = "+".join(s.name for s in self.stages) or "none"

    def filter(self, x: float, t: float) -> float:
        for s in self.stages:
            x = s.filter(x, t)
        return x

    def reset(self) -> None:
        for s in self.stages:
            s.reset()
