"""The guided movement test: Qadr's 18-task protocol, on this pointer.

    from headmouse.movement_test import MovementTest
    MovementTest(tk_root, runner)                    # the full 18 tasks
    MovementTest(tk_root, runner, rest_only=True)    # one 15 s still hold

Why it exists: every number in REPORT.md is from a synthetic rig. This is
the instrument that measures the real thing, with the same task list and
the same metric definitions as Omar's movement lab, so the two projects can
be compared on one scorecard instead of on argument.

The plan, matching Qadr's LabWindow.Plan():
    rest-start                 3 s still hold at the centre
    right/left x {0.55, 1.0}   twice each            8 strokes
    up/down    x {0.55, 1.0}                         4 strokes
    four diagonals at 0.75                           4 strokes
    rest-end                   a 15 s still hold     (Qadr's is 3 s; the
                               15 s hold is what F08 actually scores)
                                                     18 tasks

Each stroke: bring the pointer into the dark start circle and stay 0.8 s,
then move along the dashed line into the green target (64 reference px
across). Arrival is 0.5 s continuously inside it; 8 s without arriving is
recorded as a timeout, not retried. After arrival a 1.5 s hold is recorded
descriptively. Still holds record the WHOLE instructed duration even if
the pointer drifts out of the circle; losing tracking or touching a screen
edge fails the hold instead of restarting it, because rerunning until a
clean streak appears would erase exactly the failures we need to see.

Head control only. The runner's clicks are suspended for the whole test so
a click lock cannot fake a still hold. Escape stops and saves what was
done so far. Nothing here needs a click to advance.

The protocol logic (plan, state machine, scoring, saving) is the Tk-free
MovementProtocol, so it is tested without a screen.
"""

from __future__ import annotations

import collections
import csv
import html
import json
import math
import os
import platform
import sys
import time
from dataclasses import dataclass, field

from . import movement_metrics as mm
from .storage import data_dir

PROTOCOL_VERSION = "headmouse-guided-1 (Qadr LabWindow plan, schema 2 metrics)"

START_RADIUS_REF = 35.0      # 70 reference px across, as Qadr's origin circle
TARGET_RADIUS_REF = 32.0     # 64 reference px across, per F04
START_DWELL_S = 0.8
ARRIVE_DWELL_S = 0.5
TRAVEL_TIMEOUT_S = 8.0
START_TIMEOUT_S = 30.0
POST_ARRIVAL_HOLD_S = 1.5
REST_SETTLE_S = 0.4
EDGE_MARGIN_PX = 3.0

# Warm palette, used by the window and the HTML report.
BG = "#f6f1e7"
INK = "#2b2118"
MUTED = "#8d8272"
START_FILL = "#3a2f25"
TARGET_FILL = "#d8ecd9"
TARGET_LINE = "#2f7d4f"
TRAIL = "#c26d0c"
REFLINE = "#9c8b78"
FAIL = "#b3261e"


@dataclass(frozen=True)
class TaskPlan:
    id: str
    label: str
    kind: str              # "travel" | "rest"
    dx: float = 0.0        # fraction of the layout span, + is right
    dy: float = 0.0        # + is DOWN (screen coordinates)
    hold_s: float = 0.0    # rest tasks only


def plan_18() -> list[TaskPlan]:
    p = [TaskPlan("rest-start", "Rest at centre", "rest", hold_s=3.0)]
    for d in (0.55, 1.0):
        for rep in (1, 2):
            p.append(TaskPlan(f"right-{d}-{rep}", "Right", "travel", d, 0.0))
            p.append(TaskPlan(f"left-{d}-{rep}", "Left", "travel", -d, 0.0))
    for d in (0.55, 1.0):
        p.append(TaskPlan(f"up-{d}", "Up", "travel", 0.0, -d))
        p.append(TaskPlan(f"down-{d}", "Down", "travel", 0.0, d))
    p += [TaskPlan("diagonal-ne", "Up + right", "travel", 0.75, -0.75),
          TaskPlan("diagonal-sw", "Down + left", "travel", -0.75, 0.75),
          TaskPlan("diagonal-nw", "Up + left", "travel", -0.75, -0.75),
          TaskPlan("diagonal-se", "Down + right", "travel", 0.75, 0.75)]
    p.append(TaskPlan("rest-end", "Rest at centre (15 s)", "rest",
                      hold_s=15.0))
    return p


def plan_rest_only() -> list[TaskPlan]:
    return [TaskPlan("rest-15s", "Rest at centre (15 s)", "rest",
                     hold_s=15.0)]


def target_ref(plan: TaskPlan) -> tuple[float, float]:
    """Target centre in reference px. Same spans as Qadr: 38% of the width
    and 36% of the height per unit of plan offset."""
    cx, cy = mm.REF_W / 2.0, mm.REF_H / 2.0
    return (cx + plan.dx * mm.REF_W * 0.38, cy + plan.dy * mm.REF_H * 0.36)


@dataclass
class _Live:
    """State of the task in progress."""
    phase: str = "start"            # start | settle | travel | hold | done
    phase_t: float | None = None    # when the current phase began
    task_t0: float | None = None    # first sample seen for this task
    dwell_since: float | None = None
    start_ref: tuple = (mm.REF_W / 2.0, mm.REF_H / 2.0)
    target: tuple = (mm.REF_W / 2.0, mm.REF_H / 2.0)
    travel: list = field(default_factory=list)
    hold: list = field(default_factory=list)
    arrival_t: float | None = None
    arrival_xy: tuple | None = None
    interrupted: str = ""


# ---------------------------------------------------------------------------
class MovementProtocol:
    """Tk-free state machine. Feed it runner tick samples in time order.

    sample: dict with t, x, y (absolute virtual-desktop px), face, active,
    blocked, and optionally yaw/pitch/raw_yaw/raw_pitch.
    """

    def __init__(self, plan: list[TaskPlan], area: mm.Area,
                 bounds: tuple | None = None, synthetic: bool = False):
        self.plan = list(plan)
        self.area = area
        # (left, top, width, height) of the region the pointer is clamped
        # to, for edge-contact detection. Defaults to the test area.
        self.bounds = bounds or (area.left, area.top, area.width,
                                 area.height)
        self.synthetic = synthetic
        self.index = 0
        self.results: list[dict] = []
        self.rows: list[tuple] = []      # the full trace, for trace.csv
        self.finished = False
        self.end_reason = ""
        self.t_started: float | None = None
        self._live = _Live()
        self._begin_task()

    # -- accessors for a view -----------------------------------------------
    @property
    def current(self) -> TaskPlan | None:
        return self.plan[self.index] if self.index < len(self.plan) else None

    @property
    def phase(self) -> str:
        return "done" if self.finished else self._live.phase

    def view(self) -> dict:
        """Everything a window needs to draw, in reference px."""
        lv = self._live
        task = self.current
        return {
            "finished": self.finished,
            "index": self.index, "total": len(self.plan),
            "task": task, "phase": self.phase,
            "start": lv.start_ref if lv.phase != "start" else
            (mm.REF_W / 2.0, mm.REF_H / 2.0),
            "target": lv.target, "kind": task.kind if task else "",
            "instruction": self.instruction(),
        }

    def instruction(self) -> str:
        if self.finished:
            return "Test finished."
        task = self.current
        lv = self._live
        n = f"{self.index + 1}/{len(self.plan)}"
        if lv.phase == "start":
            return (f"{n}  Move the pointer into the dark circle and stay "
                    "there a moment.")
        if task.kind == "rest":
            if lv.phase == "settle":
                return f"{n}  Relax. Keep your head still."
            return (f"{n}  Keep your head still. Let the pointer drift if "
                    "it drifts; do not correct it.")
        if lv.phase == "travel":
            return (f"{n}  Move {task.label.lower()} along the dashed line "
                    "into the green circle.")
        return f"{n}  Hold still in the target."

    # -- feeding ---------------------------------------------------------------
    def feed(self, s: dict) -> None:
        if self.finished:
            return
        t = float(s["t"])
        if self.t_started is None:
            self.t_started = t
        valid = bool(s.get("face")) and bool(s.get("active", True)) \
            and not s.get("blocked")
        rx, ry = self.area.to_ref(float(s["x"]), float(s["y"]))
        edge = self._at_edge(float(s["x"]), float(s["y"]))
        task = self.current
        lv = self._live
        if lv.task_t0 is None:
            lv.task_t0 = t
        self.rows.append((t, s["x"], s["y"], round(rx, 2), round(ry, 2),
                          int(valid), int(bool(s.get("face"))),
                          int(bool(s.get("active", True))),
                          s.get("blocked") or "",
                          _r(s.get("yaw")), _r(s.get("pitch")),
                          _r(s.get("raw_yaw")), _r(s.get("raw_pitch")),
                          lv.phase, task.id))
        sample = mm.Sample(t, rx, ry, valid, edge)

        if lv.phase == "start":
            if t - lv.task_t0 > START_TIMEOUT_S:
                self._complete(False, "never reached the start circle")
                return
            inside = math.hypot(rx - mm.REF_W / 2.0,
                                ry - mm.REF_H / 2.0) <= START_RADIUS_REF
            if not (valid and inside):
                lv.dwell_since = None
                return
            if lv.dwell_since is None:
                lv.dwell_since = t
            if t - lv.dwell_since >= START_DWELL_S:
                lv.start_ref = (rx, ry)      # the ACTUAL start of the stroke
                lv.dwell_since = None
                if task.kind == "rest":
                    self._enter("settle", t)
                else:
                    self._enter("travel", t)
                    lv.travel.append(sample)
            return

        if lv.phase == "settle":             # rest tasks only
            if not valid:
                lv.phase_t = t               # wait for tracking to settle
                return
            if t - lv.phase_t >= REST_SETTLE_S:
                self._enter("hold", t)
                lv.hold.append(sample)
            return

        if lv.phase == "travel":
            lv.travel.append(sample)
            if t - lv.phase_t > TRAVEL_TIMEOUT_S:
                self._complete(False, "travel timed out")
                return
            inside = valid and math.hypot(
                rx - lv.target[0], ry - lv.target[1]) <= TARGET_RADIUS_REF
            if not inside:
                lv.dwell_since = None
                return
            if lv.dwell_since is None:
                lv.dwell_since = t
            if t - lv.dwell_since >= ARRIVE_DWELL_S:
                lv.arrival_t = t
                lv.arrival_xy = (rx, ry)
                self._enter("hold", t)
                lv.hold.append(sample)
            return

        if lv.phase == "hold":
            lv.hold.append(sample)
            if not valid:
                lv.interrupted = "tracking lost or input blocked during the hold"
                self._complete(True if task.kind == "travel" else False,
                               lv.interrupted)
                return
            need = task.hold_s if task.kind == "rest" else POST_ARRIVAL_HOLD_S
            if t - lv.phase_t >= need:
                self._complete(True, "hold recorded after settling"
                               if task.kind == "rest" else "arrived")

    def abort(self, reason: str = "stopped by Escape") -> None:
        """Stop now and keep everything so far. The task in progress is
        recorded as incomplete if it had started."""
        if self.finished:
            return
        lv = self._live
        if lv.task_t0 is not None and (lv.travel or lv.hold
                                       or lv.phase != "start"):
            self._complete(False, reason, advance=False)
        self.finished = True
        self.end_reason = reason

    # -- internals ---------------------------------------------------------------
    def _at_edge(self, x: float, y: float) -> bool:
        l, t, w, h = self.bounds
        m = EDGE_MARGIN_PX
        return (x <= l + m or y <= t + m or x >= l + w - 1 - m
                or y >= t + h - 1 - m)

    def _begin_task(self) -> None:
        self._live = _Live()
        task = self.current
        if task is not None and task.kind == "travel":
            self._live.target = target_ref(task)

    def _enter(self, phase: str, t: float) -> None:
        self._live.phase = phase
        self._live.phase_t = t
        self._live.dwell_since = None

    def _complete(self, completed: bool, reason: str,
                  advance: bool = True) -> None:
        task = self.current
        lv = self._live
        res = {"id": task.id, "label": task.label, "kind": task.kind,
               "completed": completed, "reason": reason,
               "start_ref": _pt(lv.start_ref), "target_ref": _pt(lv.target),
               "start_px": _pt(self.area.to_px(*lv.start_ref)),
               "target_px": _pt(self.area.to_px(*lv.target)),
               "travel": None, "hold": None}
        failed: list[str] = []
        if task.kind == "travel":
            path = [s for s in lv.travel
                    if lv.arrival_t is None or s.t <= lv.arrival_t]
            tr = mm.measure_travel(path, lv.start_ref, lv.target,
                                   lv.arrival_t)
            res["travel"] = tr
            ok, failed = mm.travel_verdict(tr) if lv.travel else (
                False, ["never started the stroke"])
            if lv.hold:
                res["hold"] = mm.measure_hold(lv.hold)   # descriptive only
            res["pass"] = ok and lv.arrival_t is not None
        else:
            if lv.hold:
                hd = mm.measure_hold(lv.hold)
                if lv.interrupted and lv.interrupted not in hd["invalid_reasons"]:
                    hd["invalid_reasons"].append(lv.interrupted)
                    hd["valid"] = False
                need = task.hold_s
                if hd["duration_s"] < need - 0.1 and completed is False \
                        and not lv.interrupted:
                    hd["invalid_reasons"].append("hold not finished")
                    hd["valid"] = False
                res["hold"] = hd
                res["hold"]["instructed_s"] = need
                ok, failed = mm.hold_verdict(hd)
            else:
                ok, failed = False, ["hold never started"]
            res["pass"] = ok and completed
        if not completed and reason not in failed:
            failed.insert(0, reason)
        res["failed_criteria"] = failed
        self.results.append(res)
        if advance:
            self.index += 1
            if self.index >= len(self.plan):
                self.finished = True
                self.end_reason = "completed"
            else:
                self._begin_task()

    # -- reporting ---------------------------------------------------------------
    def summary(self) -> dict:
        travel = [r for r in self.results if r["kind"] == "travel"]
        rest = [r for r in self.results if r["kind"] == "rest"]
        arrived = [r for r in travel if r["travel"] and r["travel"]["arrived"]]

        def mean(key, rows):
            v = [r["travel"][key] for r in rows
                 if r["travel"] and r["travel"].get(key) is not None]
            return round(sum(v) / len(v), 3) if v else None

        by_dir: dict[str, list] = collections.defaultdict(list)
        for r in travel:
            by_dir[r["label"]].append(r)
        return {
            "tasks_planned": len(self.plan),
            "tasks_recorded": len(self.results),
            "tasks_completed": sum(1 for r in self.results if r["completed"]),
            "tasks_passed": sum(1 for r in self.results if r.get("pass")),
            "travel_tasks": len(travel),
            "travel_arrived": len(arrived),
            "travel_passed": sum(1 for r in travel if r.get("pass")),
            "holds": len(rest),
            "holds_passed": sum(1 for r in rest if r.get("pass")),
            "mean_endpoint_error_px": mean("endpoint_error_px", arrived),
            "mean_cross_track_rms_px": mean("cross_track_rms_px", travel),
            "mean_path_efficiency": mean("path_efficiency", travel),
            "mean_acquisition_s": mean("acquisition_s", arrived),
            "cross_track_rms_by_direction": {
                k: mean("cross_track_rms_px", v) for k, v in by_dir.items()},
            "end_reason": self.end_reason or (
                "completed" if self.finished else "in progress"),
            "note": ("Observed on this build, this person, this room. A "
                     "finite run is evidence, not a guarantee. Head control "
                     "only; samples while the helper override or any block "
                     "was active are marked invalid."),
        }

    def save(self, environment: dict | None = None,
             folder: str | None = None) -> str:
        """Write report.json, trace.csv and report.html. Returns the folder."""
        if folder is None:
            stamp = time.strftime("%Y%m%d-%H%M%S")
            folder = os.path.join(data_dir(), "movement-tests", stamp)
            n = 1
            while os.path.exists(folder):
                n += 1
                folder = os.path.join(data_dir(), "movement-tests",
                                      f"{stamp}-{n}")
        os.makedirs(folder, exist_ok=True)
        env = dict(environment or {})
        env.setdefault("test_area_px", [self.area.left, self.area.top,
                                        self.area.width, self.area.height])
        env.setdefault("reference_px", [mm.REF_W, mm.REF_H])
        report = {
            "schema": 1,
            "protocol_version": PROTOCOL_VERSION,
            "created": time.strftime("%Y-%m-%dT%H:%M:%S"),
            "synthetic": self.synthetic,
            "mode": "cursor-and-head",
            "thresholds": mm.THRESHOLDS,
            "environment": env,
            "summary": self.summary(),
            "tasks": self.results,
        }
        with open(os.path.join(folder, "report.json"), "w",
                  encoding="utf-8") as fh:
            json.dump(report, fh, indent=2)
        with open(os.path.join(folder, "trace.csv"), "w", newline="",
                  encoding="utf-8") as fh:
            w = csv.writer(fh)
            w.writerow(["t", "x_px", "y_px", "ref_x", "ref_y", "valid",
                        "face", "active", "blocked", "yaw", "pitch",
                        "raw_yaw", "raw_pitch", "phase", "task"])
            w.writerows(self.rows)
        with open(os.path.join(folder, "report.html"), "w",
                  encoding="utf-8") as fh:
            fh.write(report_html(report, self.rows))
        return folder


def _r(v):
    return "" if v is None else round(float(v), 4)


def _pt(p):
    return [round(float(p[0]), 2), round(float(p[1]), 2)]


def environment(runner=None, area: mm.Area | None = None) -> dict:
    env = {"python": sys.version.split()[0],
           "platform": platform.platform(),
           "build": _build_string()}
    if runner is not None:
        try:
            env["virtual_desktop_px"] = [runner.origin_x, runner.origin_y,
                                         runner.cfg.screen_w,
                                         runner.cfg.screen_h]
            env["mapping"] = getattr(runner.cfg, "mode", "")
            env["top_speed_px_s"] = getattr(runner.cfg, "max_speed_px_s", None)
        except Exception:
            pass
    try:
        import ctypes
        env["system_dpi"] = int(ctypes.windll.user32.GetDpiForSystem())
    except Exception:
        pass
    if area is not None:
        env["test_area_px"] = [area.left, area.top, area.width, area.height]
    return env


def _build_string() -> str:
    try:
        from . import __version__      # type: ignore[attr-defined]
        return str(__version__)
    except Exception:
        pass
    try:
        here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        mt = os.path.getmtime(os.path.join(here, "app.py"))
        return "dev " + time.strftime("%Y-%m-%d %H:%M", time.localtime(mt))
    except Exception:
        return "dev"


def report_html(report: dict, rows: list | None = None) -> str:
    def n(v, unit="", pct=False):
        if v is None:
            return "n/a"
        if pct:
            return f"{100.0 * v:.1f}%"
        return f"{v:.2f}{unit}"

    s = report["summary"]
    b = [
        "<!doctype html><html><head><meta charset='utf-8'>",
        "<meta name='viewport' content='width=device-width,initial-scale=1'>",
        "<title>Movement test</title><style>",
        f"body{{font:15px/1.5 system-ui,sans-serif;margin:24px auto;"
        f"max-width:1100px;padding:0 16px;color:{INK};background:{BG}}}",
        "table{border-collapse:collapse;width:100%;font-variant-numeric:"
        "tabular-nums}td,th{padding:7px 8px;border-bottom:1px solid #d9cfbf;"
        "text-align:left}th{font-weight:600}",
        f".fail{{color:{FAIL};font-weight:600}}.pass{{color:{TARGET_LINE};"
        "font-weight:600}",
        f"small{{color:{MUTED}}}svg{{background:#efe7d8;width:100%;"
        "max-height:220px}article{margin:22px 0}",
        "</style></head><body>",
        "<h1>Movement test</h1>",
        f"<p>{html.escape(report['created'])} &middot; "
        f"{html.escape(report['protocol_version'])} &middot; "
        f"{'synthetic' if report['synthetic'] else 'live'} &middot; "
        f"build {html.escape(str(report['environment'].get('build', '')))}"
        "</p>",
        f"<p><b>{s['tasks_passed']}</b> of {s['tasks_planned']} tasks passed "
        f"&middot; {s['travel_arrived']} of {s['travel_tasks']} targets "
        f"reached &middot; {s['holds_passed']} of {s['holds']} holds steady "
        f"enough &middot; ended: {html.escape(s['end_reason'])}</p>",
        "<p><small>Distances are in 1920x1080 reference pixels. Thresholds: "
        "endpoint &le;16, cross-track RMS &le;8, efficiency &ge;90%, "
        "acquisition &le;8 s; holds: total RMS &le;4, detrended &le;2, "
        "drift &le;6. A lost-tracking or edge-pinned hold is a failed "
        "hold.</small></p>",
        "<table><tr><th>Task</th><th>Result</th><th>Endpoint</th>"
        "<th>Cross-track RMS</th><th>Efficiency</th><th>Time</th>"
        "<th>Hold RMS</th><th>Detrended</th><th>Drift</th><th>Why</th></tr>",
    ]
    for r in report["tasks"]:
        tr = r.get("travel") or {}
        hd = r.get("hold") or {}
        ok = r.get("pass")
        b.append(
            f"<tr><td>{html.escape(r['id'])}</td>"
            f"<td class='{'pass' if ok else 'fail'}'>"
            f"{'pass' if ok else 'fail'}</td>"
            f"<td>{n(tr.get('endpoint_error_px'), ' px')}</td>"
            f"<td>{n(tr.get('cross_track_rms_px'), ' px')}</td>"
            f"<td>{n(tr.get('path_efficiency'), pct=True)}</td>"
            f"<td>{n(tr.get('acquisition_s'), ' s')}</td>"
            f"<td>{n(hd.get('total_rms_px'), ' px')}</td>"
            f"<td>{n(hd.get('detrended_rms_px'), ' px')}</td>"
            f"<td>{n(hd.get('drift_px'), ' px')}</td>"
            f"<td><small>{html.escape(', '.join(r.get('failed_criteria') or []))}"
            "</small></td></tr>")
    b.append("</table>")

    # one small path drawing per stroke, from the trace
    if rows:
        by_task: dict[str, list] = collections.defaultdict(list)
        for row in rows:
            if row[13] == "travel" and row[5]:
                by_task[row[14]].append((row[3], row[4]))
        for r in report["tasks"]:
            pts = by_task.get(r["id"])
            if r["kind"] != "travel" or not pts or len(pts) < 2:
                continue
            sx, sy = r["start_ref"]
            tx, ty = r["target_ref"]
            xs = [p[0] for p in pts] + [sx, tx]
            ys = [p[1] for p in pts] + [sy, ty]
            x0, y0 = min(xs) - 40, min(ys) - 40
            w, h = max(max(xs) - x0 + 40, 80), max(max(ys) - y0 + 40, 80)
            poly = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
            b.append(
                f"<article><h3>{html.escape(r['id'])} &middot; "
                f"{html.escape(r['label'])}</h3>"
                f"<svg viewBox='{x0:.1f} {y0:.1f} {w:.1f} {h:.1f}' role='img' "
                f"aria-label='Recorded pointer path'>"
                f"<line x1='{sx:.1f}' y1='{sy:.1f}' x2='{tx:.1f}' y2='{ty:.1f}' "
                f"stroke='{REFLINE}' stroke-dasharray='6 5' stroke-width='2' "
                f"vector-effect='non-scaling-stroke'/>"
                f"<circle cx='{tx:.1f}' cy='{ty:.1f}' r='{TARGET_RADIUS_REF}' "
                f"fill='{TARGET_FILL}' stroke='{TARGET_LINE}' stroke-width='2' "
                f"vector-effect='non-scaling-stroke'/>"
                f"<polyline fill='none' stroke='{TRAIL}' stroke-width='2' "
                f"vector-effect='non-scaling-stroke' points='{poly}'/>"
                "</svg></article>")
    b.append("<p><small>Head control only. Helper mouse input during the "
             "test shows up as blocked samples and invalidates holds. "
             "Cursor positions are measured, not inferred.</small></p>")
    b.append("</body></html>")
    return "".join(b)


# ---------------------------------------------------------------------------
class MovementTest:
    """The window. Tk must run on the main thread; the runner's tick
    listener only appends to a deque, which is safe from the control
    thread, and the window drains it with after()."""

    POLL_MS = 15

    def __init__(self, root, runner, rest_only: bool = False,
                 on_done=None, synthetic: bool = False):
        import tkinter as tk
        self.tk = tk
        self.root = root
        self.runner = runner
        self.on_done = on_done
        self.folder: str | None = None
        self._closed = False
        self._q: collections.deque = collections.deque(maxlen=50000)

        self.win = tk.Toplevel(root)
        self.win.title("Movement test")
        self.win.configure(bg=BG)
        self.win.attributes("-topmost", True)
        sw, sh = root.winfo_screenwidth(), root.winfo_screenheight()
        self.win.geometry(f"{sw}x{sh}+0+0")
        try:
            self.win.attributes("-fullscreen", True)
        except Exception:
            pass
        self.area = mm.Area(0.0, 0.0, float(sw), float(sh))
        self.canvas = tk.Canvas(self.win, bg=BG, highlightthickness=0,
                                width=sw, height=sh)
        self.canvas.pack(fill="both", expand=True)
        self.win.bind("<Escape>", lambda e: self.close())
        self.win.protocol("WM_DELETE_WINDOW", self.close)

        bounds = None
        try:
            bounds = (runner.origin_x, runner.origin_y,
                      runner.cfg.screen_w, runner.cfg.screen_h)
        except Exception:
            pass
        self.protocol = MovementProtocol(
            plan_rest_only() if rest_only else plan_18(), self.area,
            bounds=bounds, synthetic=synthetic)
        self._trail: list[float] = []
        self._last_task = -1

        self._prev_suspend = getattr(runner, "suspend_clicks", False)
        try:
            runner.suspend_clicks = True
        except Exception:
            pass
        runner.add_tick_listener(self._on_tick)
        self._draw_static()
        self.win.after(self.POLL_MS, self._poll)

    # called on the CONTROL thread: append only, never touch Tk here
    def _on_tick(self, sample: dict) -> None:
        self._q.append(sample)

    def _px(self, ref):
        x, y = self.area.to_px(*ref)
        return x - self.area.left, y - self.area.top

    def _r(self, ref_r):
        return (ref_r * self.area.width / mm.REF_W,
                ref_r * self.area.height / mm.REF_H)

    def _draw_static(self) -> None:
        c = self.canvas
        c.delete("all")
        v = self.protocol.view()
        sw = self.area.width
        c.create_text(sw / 2, 46, text=v["instruction"], fill=INK,
                      font=("Segoe UI", 22, "bold"), tags="instr",
                      width=sw - 80)
        c.create_text(sw / 2, 88, text=self._progress_text(), fill=MUTED,
                      font=("Segoe UI", 13), tags="progress")
        c.create_text(sw / 2, self.area.height - 30,
                      text="Head control only. Clicks are paused during the "
                           "test. Escape stops and saves.",
                      fill=MUTED, font=("Segoe UI", 12))
        if v["finished"]:
            return
        task = v["task"]
        centre = (mm.REF_W / 2.0, mm.REF_H / 2.0)
        if task.kind == "travel":
            tx, ty = self._px(v["target"])
            rx, ry = self._r(TARGET_RADIUS_REF)
            sx, sy = self._px(v["start"] if v["phase"] != "start" else centre)
            c.create_line(sx, sy, tx, ty, fill=REFLINE, dash=(6, 5), width=2,
                          tags="refline")
            c.create_oval(tx - rx, ty - ry, tx + rx, ty + ry,
                          fill=TARGET_FILL, outline=TARGET_LINE, width=4,
                          tags="target")
        cx, cy = self._px(centre)
        rx, ry = self._r(START_RADIUS_REF)
        if v["phase"] == "start":
            c.create_oval(cx - rx, cy - ry, cx + rx, cy + ry,
                          fill=START_FILL, outline=START_FILL, tags="start")
        elif task.kind == "rest":
            c.create_oval(cx - rx, cy - ry, cx + rx, cy + ry,
                          outline=START_FILL, width=3, tags="start")
        self._trail = []

    def _progress_text(self) -> str:
        p = self.protocol
        done = len(p.results)
        passed = sum(1 for r in p.results if r.get("pass"))
        return (f"{done} of {len(p.plan)} recorded, {passed} passed  ·  "
                f"phase: {p.phase}")

    def _poll(self) -> None:
        if self._closed:
            return
        changed = False
        last = None
        before = (self.protocol.index, self.protocol.phase)
        while self._q:
            s = self._q.popleft()
            self.protocol.feed(s)
            last = s
            if (self.protocol.index, self.protocol.phase) != before:
                changed = True
                before = (self.protocol.index, self.protocol.phase)
        if changed:
            self._draw_static()
        if last is not None and self.protocol.phase == "travel":
            x = float(last["x"]) - self.area.left
            y = float(last["y"]) - self.area.top
            self._trail += [x, y]
            if len(self._trail) >= 4:
                self.canvas.delete("trail")
                self.canvas.create_line(*self._trail[-4000:], fill=TRAIL,
                                        width=2, tags="trail")
        if last is not None:
            self.canvas.itemconfigure("instr",
                                      text=self.protocol.instruction())
            self.canvas.itemconfigure("progress", text=self._progress_text())
        if self.protocol.finished:
            self._finish()
            return
        self.win.after(self.POLL_MS, self._poll)

    def _finish(self) -> None:
        if self.folder is None:
            try:
                self.folder = self.protocol.save(
                    environment(self.runner, self.area))
            except Exception as e:          # never lose the window over IO
                self.folder = f"(could not save: {type(e).__name__}: {e})"
        s = self.protocol.summary()
        self.canvas.delete("all")
        self.canvas.create_text(
            self.area.width / 2, self.area.height / 2 - 30,
            text=(f"Saved. {s['tasks_passed']} of {s['tasks_planned']} tasks "
                  f"passed, {s['travel_arrived']} of {s['travel_tasks']} "
                  f"targets reached."),
            fill=INK, font=("Segoe UI", 24, "bold"),
            width=self.area.width - 80)
        self.canvas.create_text(self.area.width / 2,
                                self.area.height / 2 + 30,
                                text=str(self.folder), fill=MUTED,
                                font=("Segoe UI", 13),
                                width=self.area.width - 80)
        self._detach()
        self.win.after(6000, self._destroy)

    def _detach(self) -> None:
        try:
            self.runner.remove_tick_listener(self._on_tick)
        except Exception:
            pass
        try:
            self.runner.suspend_clicks = self._prev_suspend
        except Exception:
            pass

    def _destroy(self) -> None:
        if self._closed:
            return
        self._closed = True
        self._detach()
        try:
            self.win.destroy()
        except Exception:
            pass
        if self.on_done:
            try:
                self.on_done(self.folder)
            except Exception:
                pass

    def close(self, save: bool = True) -> None:
        """Escape: stop now, save what was done, and close."""
        if self._closed:
            return
        if save and not self.protocol.finished:
            # drain whatever already arrived so the partial save is complete
            while self._q:
                self.protocol.feed(self._q.popleft())
            self.protocol.abort("stopped by Escape")
            if self.protocol.results:
                try:
                    self.folder = self.protocol.save(
                        environment(self.runner, self.area))
                except Exception as e:
                    self.folder = f"(could not save: {type(e).__name__}: {e})"
        self._destroy()
