"""Score pointer filters the way the eye sees them: the cursor at 120 Hz.

    .venv\\Scripts\\python tools\\glide_test.py [recording folder]

Uses a recording from record_session.py and the pose cache from
replay_lab.py. Many webcams repeat frames (this laptop camera sends each
real picture three times in dim light: 30 "fps", 10 real). A cursor that
updates per frame then STEPS: it sits still for two frames and jumps on the
third. This compares:

  per-frame         today: the cursor moves on every frame it is given
  skip repeats      only real pictures count; cursor holds in between
  skip + glide      only real pictures count; the cursor glides between
                    them, one real-picture interval behind

All with One Euro + sustained parking from replay_lab.py. Scored on a 120 Hz
timeline: still wander, moving roughness (how much the cursor's speed
shakes, px/s), delay against a zero-delay reference, and reach.
"""

from __future__ import annotations

import glob
import math
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import replay_lab as L  # noqa: E402

HZ = 120.0


def new_frame_flags(folder):
    cache = os.path.join(folder, "newframes.npy")
    if os.path.exists(cache):
        return np.load(cache)
    cap = cv2.VideoCapture(os.path.join(folder, "video.avi"))
    flags, prev = [], None
    while True:
        ok, f = cap.read()
        if not ok:
            break
        g = cv2.cvtColor(cv2.resize(f, (160, 120)), cv2.COLOR_BGR2GRAY)
        g = g.astype(np.int16)
        flags.append(prev is None or float(np.abs(g - prev).mean()) > 0.3)
        prev = g
    flags = np.array(flags, dtype=bool)
    np.save(cache, flags)
    return flags


def run_filter(cal, samples):
    f = L.ParkedSustained(cal)
    out = []
    for t, y, p in samples:
        oy, op = f.step(t, y, p)
        out.append((t,) + L.to_px(cal, oy, op))
    return out


def hold(updates, grid):
    """What the screen shows if the cursor jumps at each update and sits
    still in between."""
    ts = np.array([u[0] for u in updates])
    idx = np.clip(np.searchsorted(ts, grid, side="right") - 1, 0, len(ts) - 1)
    return (np.array([updates[i][1] for i in idx]),
            np.array([updates[i][2] for i in idx]))


def glide(updates, grid, frac=1.0):
    """Render one update interval behind and move in a straight line
    between the last two real updates. The interval is measured live
    (median of recent gaps), so it adapts to any camera."""
    ts = np.array([u[0] for u in updates])
    xs = np.array([u[1] for u in updates])
    ys = np.array([u[2] for u in updates])
    gaps = np.diff(ts)
    X, Y = np.empty_like(grid), np.empty_like(grid)
    for k, t in enumerate(grid):
        i = np.searchsorted(ts, t, side="right") - 1
        lo = max(0, i - 8)
        d = float(np.median(gaps[lo:i])) if i - lo >= 2 else 0.1
        d = min(max(d, 1 / 60), 0.2)
        tt = t - frac * d
        j = np.searchsorted(ts, tt, side="right") - 1
        if j < 0:
            X[k], Y[k] = xs[0], ys[0]
        elif j >= len(ts) - 1 or ts[j + 1] > t:
            # past the newest real picture: predict from the last step,
            # never further than one interval
            jj = min(j, len(ts) - 1)
            jn = np.searchsorted(ts, t, side="right") - 1
            jj = jn
            if jj >= 1 and frac < 1.0:
                ahead = min(tt - ts[jj], d)
                vx = (xs[jj] - xs[jj - 1]) / max(ts[jj] - ts[jj - 1], 1e-6)
                vy = (ys[jj] - ys[jj - 1]) / max(ts[jj] - ts[jj - 1], 1e-6)
                if ahead > 0:
                    X[k], Y[k] = xs[jj] + vx * ahead, ys[jj] + vy * ahead
                else:
                    a = (tt - ts[jj - 1]) / max(ts[jj] - ts[jj - 1], 1e-6)
                    X[k] = xs[jj - 1] + a * (xs[jj] - xs[jj - 1])
                    Y[k] = ys[jj - 1] + a * (ys[jj] - ys[jj - 1])
            else:
                X[k], Y[k] = xs[min(j, len(ts) - 1)], ys[min(j, len(ts) - 1)]
        else:
            a = (tt - ts[j]) / max(ts[j + 1] - ts[j], 1e-6)
            X[k] = xs[j] + a * (xs[j + 1] - xs[j])
            Y[k] = ys[j] + a * (ys[j + 1] - ys[j])
    return X, Y


def smooth(t, v, half):
    return L.centred_smooth(t, v, half)


def score(name, grid, X, Y, ref, segs):
    rx, ry = ref
    res = {"name": name}
    still, rough, lags, kept = [], [], [], []
    for lab, a, b in segs:
        m = (grid >= a + 0.7) & (grid <= b) if lab == "still" else \
            (grid >= a) & (grid <= b + 0.5)
        if m.sum() < 20:
            continue
        xs, ys, ts = X[m], Y[m], grid[m]
        if lab == "still":
            still.append((np.hypot(np.diff(xs), np.diff(ys)).sum() / (b - a - 0.7),
                          np.hypot(xs - xs[0], ys - ys[0]).max()))
            continue
        vx, vy = np.diff(xs) * HZ, np.diff(ys) * HZ
        svx, svy = smooth(ts[1:], vx, 0.08), smooth(ts[1:], vy, 0.08)
        rough.append(float(np.sqrt(np.mean((vx - svx) ** 2 + (vy - svy) ** 2))))
        best, lag = 1e18, 0
        for dl in range(0, 40):            # up to 330 ms, in 8 ms steps
            e = np.mean((xs[dl:] - rx[m][:len(xs) - dl]) ** 2
                        + (ys[dl:] - ry[m][:len(ys) - dl]) ** 2)
            if e < best:
                best, lag = e, dl
        lags.append(1000 * lag / HZ)
        kept.append(100 * max(np.ptp(xs), np.ptp(ys))
                    / max(np.ptp(rx[m]), np.ptp(ry[m]), 1))
    res["still_wander"] = np.mean([s[0] for s in still])
    res["still_max"] = max(s[1] for s in still)
    res["rough"] = np.mean(rough)
    res["lag"] = np.mean(lags)
    res["kept"] = np.mean(kept)
    return res


def main(argv=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    folder = argv[0] if argv else sorted(glob.glob(os.path.join(
        L.storage.data_dir(), "recordings", "*")))[-1]
    data, rows = L.extract_pose(folder)
    segs = L.segments(rows)
    cal = L.calibrate(data, segs)
    flags = new_frame_flags(folder)

    # pose per delivered frame, aligned with frames.csv order
    by_t = {round(t, 6): (y, p) for t, _, y, p in data}
    frames = [(float(r["t"]), int(r["frame"])) for r in rows]
    every, fresh = [], []
    first_seen = None
    for (t, i) in frames:
        yp = by_t.get(round(t, 6))
        if yp is None:
            continue
        every.append((t,) + yp)
        if i < len(flags) and flags[i]:
            fresh.append((t,) + yp)
    real_fps = len(fresh) / (frames[-1][0] - frames[0][0])
    print(f"{len(every)} frames delivered, {len(fresh)} real pictures "
          f"({real_fps:.1f} real fps)\n")

    t0, t1 = every[0][0], every[-1][0]
    grid = np.arange(t0, t1, 1 / HZ)
    # zero-delay reference from the real pictures, smoothed without delay
    rt = np.array([s[0] for s in fresh])
    rpx = [L.to_px(cal, s[1], s[2]) for s in fresh]
    rxs = np.interp(grid, rt, smooth(rt, np.array([p[0] for p in rpx]), 0.12))
    rys = np.interp(grid, rt, smooth(rt, np.array([p[1] for p in rpx]), 0.12))

    runs = []
    u = run_filter(cal, every)
    runs.append(("per-frame (today)",) + hold(u, grid))
    u = run_filter(cal, fresh)
    runs.append(("skip repeats",) + hold(u, grid))
    runs.append(("skip repeats + glide",) + glide(u, grid))
    runs.append(("glide half-behind + predict",) + glide(u, grid, 0.5))

    print(f"{'how the cursor is driven':26s} | {'STILL wander px/s':>17s} {'max px':>7s}"
          f" | {'MOVING rough px/s':>17s} {'delay ms':>8s} {'kept %':>7s}")
    results = []
    for name, X, Y in runs:
        r = score(name, grid, X, Y, (rxs, rys), segs)
        results.append((name, X, Y))
        print(f"{name:26s} | {r['still_wander']:>17.1f} {r['still_max']:>7.0f} | "
              f"{r['rough']:>17.0f} {r['lag']:>8.0f} {r['kept']:>7.0f}")

    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(1, 2, figsize=(14, 4.2))
    mv = [s for s in segs if s[0] == "move"]
    for ax, (seg, col, ttl) in zip(axes, ((mv[0], 1, "TURNING LEFT: cursor x"),
                                          (mv[2], 2, "LOOKING UP: cursor y"))):
        a, b = seg[1] + 0.9, seg[1] + 2.2
        m = (grid >= a) & (grid <= b)
        for (name, X, Y), c, lw in zip(results, ("#c9bda8", "#8a5a00", "#b3261e"),
                                       (1.0, 1.4, 2.2)):
            ax.plot(grid[m] - t0, (X if col == 1 else Y)[m], color=c, lw=lw,
                    label=name)
        ax.set_title(ttl + " (px), as shown 120 times a second", loc="left",
                     fontsize=10)
        ax.set_xlabel("seconds")
        ax.legend(fontsize=8)
    fig.tight_layout()
    out = os.path.join(folder, "glide.png")
    fig.savefig(out, dpi=85)
    print(f"\nchart: {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
