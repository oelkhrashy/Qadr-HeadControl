"""Hold still, and measure how much the face reading wobbles.

    .venv\\Scripts\\python tools\\still_test.py [--seconds 30] [--camera 0]

Every camera frame goes through every pre-processing variant below, each
with its own MediaPipe instance, so all variants are judged on the SAME
frames at the same moment. The person sits still; whatever the readings
do is noise, and the variant with the least of it is the one to keep.

    640 (today)       the picture both apps use now (1280x720 shrunk to 640)
    1280 full         the camera's full 1280x720
    1280 crop         a fixed square around the face, from the 1280 picture
    crop+contrast     the crop, with local contrast evened out (CLAHE)
    crop+smooth       the crop, with edge-preserving smoothing (bilateral)
    crop+2-frame avg  the crop averaged with the previous frame

The crop is set once, from the first seconds, and never moves: a crop that
followed the face would add its own wobble.

Writes a JSON report and the raw per-frame readings to
%APPDATA%\\HeadMouse\\still-tests\\.
"""

from __future__ import annotations

import argparse
import base64
import json
import math
import os
import statistics as st
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor

import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)
from headmouse import storage  # noqa: E402
from headmouse.vision.mediapipe_tracker import MediaPipeTracker  # noqa: E402

DEG = 180.0 / math.pi
_CLAHE = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))


def contrast(img):
    ycc = cv2.cvtColor(img, cv2.COLOR_BGR2YCrCb)
    ycc[:, :, 0] = _CLAHE.apply(ycc[:, :, 0])
    return cv2.cvtColor(ycc, cv2.COLOR_YCrCb2BGR)


class Variant:
    def __init__(self, name, make):
        self.name = name
        self.make = make          # (frame, roi, prev_crop) -> (image, x0, y0, sx, sy)
        self.tracker = MediaPipeTracker()
        self.tracker.open()
        self.rows = []            # (t, yaw, pitch, roll, nose_x_px, nose_y_px)
        self.ms = []
        self.misses = 0

    def run(self, frame, t, roi, prev):
        img, x0, y0, sx, sy = self.make(frame, roi, prev)
        a = time.perf_counter()
        sig = self.tracker.process(np.ascontiguousarray(img), t)
        self.ms.append((time.perf_counter() - a) * 1000.0)
        if sig is None:
            self.misses += 1
            return None
        h, w = img.shape[:2]
        nx = x0 + sig.anchor[0] * w * sx
        ny = y0 + sig.anchor[1] * h * sy
        self.rows.append((t, sig.yaw * DEG, sig.pitch * DEG, sig.roll * DEG,
                          nx, ny))
        return sig


def make_variants(full_w, full_h):
    def low(f, roi, prev):
        w, h = 640, int(round(640 * full_h / full_w))
        return (cv2.resize(f, (w, h), interpolation=cv2.INTER_AREA), 0, 0,
                full_w / w, full_h / h)

    def full(f, roi, prev):
        return f, 0, 0, 1.0, 1.0

    def crop_of(f, roi):
        x0, y0, s = roi
        return f[y0:y0 + s, x0:x0 + s]

    def crop(f, roi, prev):
        return crop_of(f, roi), roi[0], roi[1], 1.0, 1.0

    def crop_contrast(f, roi, prev):
        return contrast(crop_of(f, roi)), roi[0], roi[1], 1.0, 1.0

    def crop_smooth(f, roi, prev):
        return (cv2.bilateralFilter(crop_of(f, roi), 5, 30, 5), roi[0], roi[1],
                1.0, 1.0)

    def crop_avg(f, roi, prev):
        c = crop_of(f, roi)
        if prev is not None and prev.shape == f.shape:
            c = cv2.addWeighted(c, 0.5, crop_of(prev, roi), 0.5, 0)
        return c, roi[0], roi[1], 1.0, 1.0

    return [Variant("640 (today)", low), Variant("1280 full", full),
            Variant("1280 crop", crop), Variant("crop+contrast", crop_contrast),
            Variant("crop+smooth", crop_smooth),
            Variant("crop+2-frame avg", crop_avg)]


def detrend_rms(ts, xs, window=1.0):
    """RMS of what is left after removing a centred 1 s moving average: the
    fast wobble, separated from slow posture sway."""
    out = []
    j0 = j1 = 0
    n = len(xs)
    for i in range(n):
        while ts[j0] < ts[i] - window / 2:
            j0 += 1
        while j1 < n and ts[j1] <= ts[i] + window / 2:
            j1 += 1
        m = sum(xs[j0:j1]) / (j1 - j0)
        out.append(xs[i] - m)
    return math.sqrt(sum(v * v for v in out) / len(out)) if out else float("nan")


def summarise(v: Variant, t_start: float) -> dict:
    rows = [r for r in v.rows if r[0] >= t_start]
    if len(rows) < 20:
        return {"variant": v.name, "frames": len(rows), "error": "too few faces"}
    ts = [r[0] for r in rows]
    res = {"variant": v.name, "frames": len(rows), "misses": v.misses,
           "model_ms_median": round(st.median(v.ms), 1)}
    for k, i in (("yaw", 1), ("pitch", 2), ("roll", 3)):
        xs = [r[i] for r in rows]
        f2f = [b - a for a, b in zip(xs, xs[1:])]
        res[f"{k}_wobble_deg"] = round(detrend_rms(ts, xs), 3)
        res[f"{k}_f2f_deg"] = round(math.sqrt(sum(d * d for d in f2f) / len(f2f)), 3)
        res[f"{k}_spread_deg"] = round(st.pstdev(xs), 3)
    for k, i in (("nose_x", 4), ("nose_y", 5)):
        xs = [r[i] for r in rows]
        res[f"{k}_wobble_px"] = round(detrend_rms(ts, xs), 2)
    return res


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--seconds", type=float, default=30.0)
    ap.add_argument("--settle", type=float, default=4.0)
    ap.add_argument("--no-window", action="store_true")
    args = ap.parse_args(argv)

    cap = cv2.VideoCapture(args.camera, cv2.CAP_MSMF)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
    cap.set(cv2.CAP_PROP_FPS, 30)
    ok, frame = cap.read()
    if not ok:
        print("camera did not open (is another app using it?)")
        return 1
    fh, fw = frame.shape[:2]
    print(f"camera gives {fw}x{fh}")

    variants = make_variants(fw, fh)
    finder = MediaPipeTracker()
    finder.open()
    ui = {"text": "Get ready: look at the screen", "sub": "", "done": False,
          "png": None}
    result = {}

    def worker():
        roi = None
        prev = None
        t0 = time.monotonic()
        t_hold = None
        frames = 0
        boxes = []
        pool = ThreadPoolExecutor(max_workers=len(variants))
        while True:
            ok, f = cap.read()
            t = time.monotonic()
            if not ok:
                continue
            frames += 1
            el = t - t0
            if roi is None:
                s = finder.process(f, t)
                if s is not None and any(s.face_box):
                    boxes.append(s.face_box)
                ui["text"] = "Get ready: look at the screen"
                ui["sub"] = f"finding your face... {max(0, args.settle - el):.0f}"
                if el >= args.settle and boxes:
                    b = boxes[len(boxes) // 2]
                    cx, cy = (b[0] + b[2]) / 2 * fw, (b[1] + b[3]) / 2 * fh
                    side = int(min(max((b[2] - b[0]) * fw, (b[3] - b[1]) * fh)
                                   * 2.4, fh))
                    x0 = int(min(max(cx - side / 2, 0), fw - side))
                    y0 = int(min(max(cy - side / 2, 0), fh - side))
                    roi = (x0, y0, side)
                    t_hold = t
                    result["roi"] = roi
                    result["face_width_px_full"] = round((b[2] - b[0]) * fw)
                continue
            futs = [pool.submit(v.run, f, t, roi, prev) for v in variants]
            for fu in futs:
                fu.result()
            prev = f
            left = args.seconds - (t - t_hold)
            ui["text"] = "HOLD STILL"
            ui["sub"] = f"{max(0, left):.0f} s   (look at the screen, relax)"
            if frames % 6 == 0:
                x0, y0, s = roi
                small = cv2.resize(f[y0:y0 + s, x0:x0 + s], (220, 220))
                ok2, buf = cv2.imencode(".png", small)
                if ok2:
                    ui["png"] = base64.b64encode(buf.tobytes()).decode()
            if left <= 0:
                result["t_hold"] = t_hold
                break
        pool.shutdown()
        ui["done"] = True

    th = threading.Thread(target=worker, daemon=True)
    th.start()

    if not args.no_window:
        import tkinter as tk
        root = tk.Tk()
        root.title("Still test")
        root.attributes("-topmost", True)
        root.configure(bg="#16130f")
        lab = tk.Label(root, text="", font=("Arial Black", 30), bg="#16130f",
                       fg="#f6f1e7", padx=30, pady=10)
        lab.pack()
        sub = tk.Label(root, text="", font=("Arial", 16), bg="#16130f",
                       fg="#c9bda8", pady=6)
        sub.pack()
        pic = tk.Label(root, bg="#16130f")
        pic.pack(pady=8)
        foot = tk.Label(root, text="The small picture is the zoomed crop "
                        "sent to the face model.", font=("Arial", 10),
                        bg="#16130f", fg="#8d8272")
        foot.pack(pady=(0, 10))

        def tick():
            lab.config(text=ui["text"])
            sub.config(text=ui["sub"])
            if ui["png"]:
                img = tk.PhotoImage(data=ui["png"])
                pic.config(image=img)
                pic.image = img
            if ui["done"]:
                root.destroy()
                return
            root.after(100, tick)

        tick()
        root.mainloop()
    th.join()
    cap.release()

    t_start = result.get("t_hold", 0) + 0.5
    rows = [summarise(v, t_start) for v in variants]
    hold = [r for r in variants[1].rows if r[0] >= t_start]
    fps = (len(hold) - 1) / (hold[-1][0] - hold[0][0]) if len(hold) > 2 else 0
    report = {"when": time.strftime("%Y-%m-%d %H:%M:%S"),
              "camera": f"{fw}x{fh}", "processed_fps": round(fps, 1),
              "roi": result.get("roi"),
              "face_width_px_full": result.get("face_width_px_full"),
              "seconds": args.seconds, "variants": rows}
    folder = os.path.join(storage.data_dir(), "still-tests")
    os.makedirs(folder, exist_ok=True)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    with open(os.path.join(folder, f"{stamp}.json"), "w", encoding="utf-8") as fh_:
        json.dump(report, fh_, indent=2)
    with open(os.path.join(folder, f"{stamp}-raw.csv"), "w",
              encoding="utf-8") as fh_:
        fh_.write("variant,t,yaw,pitch,roll,nose_x,nose_y\n")
        for v in variants:
            for r in v.rows:
                fh_.write(v.name + "," + ",".join(f"{x:.5f}" for x in r) + "\n")

    print(f"\nprocessed {fps:.1f} fps, face {report['face_width_px_full']} px "
          f"wide in the full picture, crop {report['roi']}")
    print(f"{'variant':18s} {'yaw wobble':>11s} {'pitch wobble':>13s} "
          f"{'yaw f2f':>8s} {'pitch f2f':>10s} {'nose px':>8s} {'ms':>5s}")
    for r in rows:
        if "error" in r:
            print(f"{r['variant']:18s} {r['error']}")
            continue
        print(f"{r['variant']:18s} {r['yaw_wobble_deg']:>9.3f}d "
              f"{r['pitch_wobble_deg']:>11.3f}d {r['yaw_f2f_deg']:>7.3f}d "
              f"{r['pitch_f2f_deg']:>9.3f}d "
              f"{math.hypot(r['nose_x_wobble_px'], r['nose_y_wobble_px']):>7.2f} "
              f"{r['model_ms_median']:>5.1f}")
    print(f"\nsaved {os.path.join(folder, stamp + '.json')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
