"""Movement-quality measurements, defined so the numbers line up with Qadr's.

Pure functions, no Tk, no Windows. The definitions follow Omar's
APPLICATION-GOALS-AND-PERFORMANCE.md (the "Operating envelope and
measurement rules" section and F04-F08), so a run here and a run of Qadr's
movement lab can be compared without arguing about what a number means:

  * Every distance is in a 1920x1080 REFERENCE coordinate system. Both axes
    of the physical cursor position are scaled to it before anything is
    measured, so a 2560x1440 screen and a 1366x768 one are comparable.
  * Endpoint error   distance from the cursor to the target centre at the
                     arrival event.
  * Cross-track RMS  RMS perpendicular distance from the straight line
                     between the ACTUAL start and the target, over the
                     travel samples.
  * Path efficiency  straight displacement / travelled length, clamped 0..1.
  * Acquisition      seconds from leaving the start to the arrival event.
  * Hold total RMS   RMS radius around the hold's mean position. It
                     INCLUDES drift, on purpose.
  * Hold detrended   the same after subtracting a least-squares linear
                     trend per axis. A descriptive residual, not "pure
                     sensor noise".
  * Hold drift       distance between the mean of the first and the last
                     250 ms of the hold.

Rule carried over unchanged: a hold that loses tracking, or that is pinned
against a screen edge, is a FAILED hold. It is never scored as calm, because
a frozen pointer is the most motionless pointer there is.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

REF_W = 1920.0
REF_H = 1080.0

# Proposed acceptance thresholds, reference pixels / seconds.
THRESHOLDS = {
    "endpoint_error_px": 16.0,      # <=
    "cross_track_rms_px": 8.0,      # <=
    "path_efficiency": 0.90,        # >=
    "acquisition_s": 8.0,           # <=
    "hold_total_rms_px": 4.0,       # <=
    "hold_detrended_rms_px": 2.0,   # <=
    "hold_drift_px": 6.0,           # <=
}

GAP_S = 0.2           # a gap longer than this between samples is a loss
DRIFT_WINDOW_S = 0.25
MIN_HOLD_S = 0.5
MIN_HOLD_SAMPLES = 10


@dataclass
class Sample:
    """One cursor observation, already in REFERENCE pixels."""
    t: float
    x: float
    y: float
    valid: bool = True
    edge: bool = False          # pinned against a screen edge


@dataclass(frozen=True)
class Area:
    """The physical rectangle the reference system is mapped onto, in
    virtual-desktop pixels (the test window's monitor)."""
    left: float
    top: float
    width: float
    height: float

    def to_ref(self, x: float, y: float) -> tuple[float, float]:
        return ((x - self.left) * REF_W / max(self.width, 1e-9),
                (y - self.top) * REF_H / max(self.height, 1e-9))

    def to_px(self, rx: float, ry: float) -> tuple[float, float]:
        return (self.left + rx * self.width / REF_W,
                self.top + ry * self.height / REF_H)


def _usable(s: Sample) -> bool:
    return (s.valid and math.isfinite(s.t) and math.isfinite(s.x)
            and math.isfinite(s.y))


def _percentile(values, q: float) -> float:
    a = sorted(values)
    if not a:
        return 0.0
    i = min(max(int(math.ceil((len(a) - 1) * q)), 0), len(a) - 1)
    return a[i]


# ---------------------------------------------------------------------------
def measure_travel(samples: list[Sample], start: tuple[float, float],
                   target: tuple[float, float],
                   arrival_t: float | None = None) -> dict:
    """Travel metrics from `samples` (start of travel up to the arrival
    event, reference px). `arrival_t` None means the target was never
    acquired: endpoint is then the last usable sample and acquisition is
    reported as None."""
    sx, sy = start
    tx, ty = target
    ax, ay = tx - sx, ty - sy
    dist = math.hypot(ax, ay)
    ux, uy = (ax / dist, ay / dist) if dist > 0 else (0.0, 0.0)

    cross: list[float] = []
    length = 0.0
    backtrack = 0.0
    peak = 0.0
    losses = 0
    prev: Sample | None = None
    invalid_run = False
    usable = [s for s in samples if _usable(s)]
    for s in samples:
        if not _usable(s):
            if not invalid_run:
                losses += 1
            invalid_run = True
            prev = None
            continue
        invalid_run = False
        if prev is not None and s.t - prev.t > GAP_S:
            losses += 1
        cross.append(abs((s.x - sx) * uy - (s.y - sy) * ux))
        if prev is not None:
            dt = s.t - prev.t
            if 0 < dt <= GAP_S:
                dx, dy = s.x - prev.x, s.y - prev.y
                step = math.hypot(dx, dy)
                length += step
                backtrack += max(0.0, -dx * ux - dy * uy)
                peak = max(peak, step / dt)
        prev = s

    end = usable[-1] if usable else None
    if end is None:
        endpoint_err = dist
        displacement = 0.0
    else:
        endpoint_err = math.hypot(end.x - tx, end.y - ty)
        displacement = math.hypot(end.x - sx, end.y - sy)
    eff = min(max(displacement / length, 0.0), 1.0) if length > 0 else 0.0
    t0 = usable[0].t if usable else None
    acq = (arrival_t - t0) if (arrival_t is not None and t0 is not None) \
        else None
    return {
        "arrived": arrival_t is not None,
        "instructed_distance_px": round(dist, 3),
        "endpoint_error_px": round(endpoint_err, 3),
        "cross_track_rms_px": round(math.sqrt(sum(c * c for c in cross)
                                              / len(cross)), 3)
        if cross else 0.0,
        "cross_track_p95_px": round(_percentile(cross, 0.95), 3),
        "cross_track_max_px": round(max(cross), 3) if cross else 0.0,
        "path_length_px": round(length, 3),
        "path_efficiency": round(eff, 4),
        "backtracking_px": round(backtrack, 3),
        "peak_speed_px_s": round(peak, 1),
        "acquisition_s": None if acq is None else round(acq, 3),
        "valid_samples": len(usable),
        "invalid_samples": len(samples) - len(usable),
        "tracking_losses": losses,
    }


def travel_verdict(m: dict) -> tuple[bool, list[str]]:
    """All four criteria must pass; failing to arrive fails everything."""
    failed = []
    if not m.get("arrived"):
        failed.append("did not reach the target in time")
    if m["endpoint_error_px"] > THRESHOLDS["endpoint_error_px"]:
        failed.append("endpoint error")
    if m["cross_track_rms_px"] > THRESHOLDS["cross_track_rms_px"]:
        failed.append("cross-track RMS")
    if m["path_efficiency"] < THRESHOLDS["path_efficiency"]:
        failed.append("path efficiency")
    acq = m.get("acquisition_s")
    if acq is None or acq > THRESHOLDS["acquisition_s"]:
        if "did not reach the target in time" not in failed:
            failed.append("acquisition time")
    return (not failed), failed


# ---------------------------------------------------------------------------
def measure_hold(samples: list[Sample]) -> dict:
    """Steadiness over an instructed hold (reference px).

    Invalid if any sample lost tracking or was blocked, if the hold touched
    a screen edge, if there is a gap longer than GAP_S, or if it is too
    short to say anything. An invalid hold still reports what numbers it
    can, labelled, so the failure is visible rather than missing."""
    reasons: list[str] = []
    if any(not _usable(s) for s in samples):
        reasons.append("tracking lost or input blocked during the hold")
    if any(s.edge for s in samples):
        reasons.append("pointer pinned against a screen edge during the hold")
    use = [s for s in samples if _usable(s)]
    for a, b in zip(use, use[1:]):
        if b.t - a.t > GAP_S:
            reasons.append("gap in the samples during the hold")
            break
    dur = (use[-1].t - use[0].t) if len(use) > 1 else 0.0
    if len(use) < MIN_HOLD_SAMPLES or dur < MIN_HOLD_S:
        reasons.append("hold too short to measure")

    out = {"valid": not reasons, "invalid_reasons": reasons,
           "duration_s": round(dur, 3), "samples": len(use),
           "total_rms_px": None, "detrended_rms_px": None,
           "drift_px": None, "p95_radius_px": None, "max_radius_px": None}
    if len(use) < 2 or dur <= 0:
        return out

    n = len(use)
    mx = sum(s.x for s in use) / n
    my = sum(s.y for s in use) / n
    radii = [math.hypot(s.x - mx, s.y - my) for s in use]
    total = math.sqrt(sum(r * r for r in radii) / n)

    t0 = use[0].t
    ts = [s.t - t0 for s in use]
    mt = sum(ts) / n
    tt = sum((t - mt) ** 2 for t in ts)
    if tt > 0:
        bx = sum((t - mt) * (s.x - mx) for t, s in zip(ts, use)) / tt
        by = sum((t - mt) * (s.y - my) for t, s in zip(ts, use)) / tt
    else:
        bx = by = 0.0
    det = math.sqrt(sum((s.x - mx - bx * (t - mt)) ** 2
                        + (s.y - my - by * (t - mt)) ** 2
                        for t, s in zip(ts, use)) / n)

    first = [s for s in use if s.t <= use[0].t + DRIFT_WINDOW_S]
    last = [s for s in use if s.t >= use[-1].t - DRIFT_WINDOW_S]
    fx = sum(s.x for s in first) / len(first)
    fy = sum(s.y for s in first) / len(first)
    lx = sum(s.x for s in last) / len(last)
    ly = sum(s.y for s in last) / len(last)

    out.update(total_rms_px=round(total, 3), detrended_rms_px=round(det, 3),
               drift_px=round(math.hypot(lx - fx, ly - fy), 3),
               p95_radius_px=round(_percentile(radii, 0.95), 3),
               max_radius_px=round(max(radii), 3))
    return out


def hold_verdict(m: dict) -> tuple[bool, list[str]]:
    failed = list(m.get("invalid_reasons") or [])
    if m.get("total_rms_px") is None:
        if not failed:
            failed.append("no hold measured")
        return False, failed
    if m["total_rms_px"] > THRESHOLDS["hold_total_rms_px"]:
        failed.append("hold total RMS")
    if m["detrended_rms_px"] > THRESHOLDS["hold_detrended_rms_px"]:
        failed.append("hold detrended RMS")
    if m["drift_px"] > THRESHOLDS["hold_drift_px"]:
        failed.append("hold drift")
    return (not failed), failed
