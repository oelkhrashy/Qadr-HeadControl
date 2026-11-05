"""Record a guided one-minute session for offline testing of pointer filters.

    .venv\\Scripts\\python tools\\record_session.py

A window gives the instructions (hold still, turn slowly left and back, and
so on). The camera video and, for every frame, its capture time and the
instruction on screen at that moment are saved to
%APPDATA%\\HeadMouse\\recordings\\<timestamp>\\. Nothing is uploaded and nothing
goes into the repo. tools/replay_lab.py replays the video through each
filter.
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import sys
import threading
import time

import cv2

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, ROOT)
from headmouse import storage  # noqa: E402

# (label, seconds, what the window says). "still" segments are where any
# cursor movement is an error; "move" segments are where it should follow.
PLAN = [
    ("ready", 6, "Get ready. Sit comfortably, look at this window."),
    ("still", 10, "HOLD STILL. Relax, look here."),
    ("move", 5, "Turn SLOWLY to your LEFT, then come back."),
    ("still", 4, "Hold still."),
    ("move", 5, "Turn SLOWLY to your RIGHT, then come back."),
    ("still", 4, "Hold still."),
    ("move", 4, "Look UP a little, then come back."),
    ("still", 4, "Hold still."),
    ("move", 4, "Look DOWN a little, then come back."),
    ("still", 4, "Hold still."),
    ("move", 5, "Quick: LEFT, centre, RIGHT, centre."),
    ("still", 6, "HOLD STILL. Almost done."),
]


def main(argv=None) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera", type=int, default=0)
    args = ap.parse_args(argv)

    cap = cv2.VideoCapture(args.camera, cv2.CAP_MSMF)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    cap.set(cv2.CAP_PROP_FPS, 30)
    ok, frame = cap.read()
    if not ok:
        print("camera did not open (is another app using it?)")
        return 1
    h, w = frame.shape[:2]

    folder = os.path.join(storage.data_dir(), "recordings",
                          time.strftime("%Y%m%d-%H%M%S"))
    os.makedirs(folder, exist_ok=True)
    video = cv2.VideoWriter(os.path.join(folder, "video.avi"),
                            cv2.VideoWriter_fourcc(*"MJPG"), 30.0, (w, h))
    times = open(os.path.join(folder, "frames.csv"), "w", encoding="utf-8")
    times.write("frame,t,segment,label\n")

    total = sum(s for _, s, _ in PLAN)
    state = {"i": 0, "t0": None, "done": False, "frames": 0, "last": None}

    def segment_at(el):
        acc = 0.0
        for i, (lab, secs, _) in enumerate(PLAN):
            if el < acc + secs:
                return i, lab, acc + secs - el
            acc += secs
        return None, None, 0.0

    def capture():
        state["t0"] = time.monotonic()
        while True:
            ok, f = cap.read()
            t = time.monotonic()
            if not ok:
                continue
            i, lab, _ = segment_at(t - state["t0"])
            if i is None:
                break
            video.write(f)
            state["last"] = f
            times.write(f"{state['frames']},{t:.6f},{i},{lab}\n")
            state["frames"] += 1
            state["i"] = i
        state["done"] = True

    th = threading.Thread(target=capture, daemon=True)
    th.start()

    import tkinter as tk
    root = tk.Tk()
    root.title("Recording")
    root.attributes("-topmost", True)
    root.configure(bg="#16130f")
    root.geometry("900x680+500+120")
    big = tk.Label(root, text="", font=("Arial Black", 24), bg="#16130f",
                   fg="#f6f1e7", wraplength=860, pady=14)
    big.pack(fill="x")
    # a live mirror view, so he can watch himself while he follows along
    view = tk.Label(root, bg="#16130f")
    view.pack()
    sub = tk.Label(root, text="", font=("Arial", 16), bg="#16130f",
                   fg="#c9bda8")
    sub.pack()
    foot = tk.Label(root, text="Recording to this PC only, for testing the "
                    "pointer filters.", font=("Arial", 10), bg="#16130f",
                    fg="#8d8272", pady=20)
    foot.pack()

    def tick():
        f = state["last"]
        if f is not None:
            small = cv2.flip(cv2.resize(f, (440, 330)), 1)
            ok, buf = cv2.imencode(".png", small)
            if ok:
                img = tk.PhotoImage(data=base64.b64encode(buf.tobytes()).decode())
                view.config(image=img)
                view.image = img
        if state["t0"] is not None:
            el = time.monotonic() - state["t0"]
            i, lab, left = segment_at(el)
            if i is not None:
                big.config(text=PLAN[i][2],
                           fg="#b8d44a" if lab == "move" else "#f6f1e7")
                sub.config(text=f"{left:.0f} s   ({total - el:.0f} s left in "
                                "total)")
        if state["done"]:
            root.destroy()
            return
        root.after(66, tick)

    tick()
    root.mainloop()
    th.join()
    cap.release()
    video.release()
    times.close()
    with open(os.path.join(folder, "plan.json"), "w", encoding="utf-8") as fh:
        json.dump({"plan": PLAN, "size": [w, h]}, fh, indent=2)
    print(f"recorded {state['frames']} frames in {total} s "
          f"({state['frames'] / total:.1f} fps) to {folder}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
