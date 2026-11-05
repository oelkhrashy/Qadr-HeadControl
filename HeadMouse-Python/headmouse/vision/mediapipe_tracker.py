"""MediaPipe Face Landmarker behind the FaceTracker contract.

Confirmed as the choice by the model agent's measurements (FACE-STACK.md):
blendshapes cost 0.7 ms a frame, so the gesture channels are effectively
free, and the model is one ~3.7 MB .task file that the installer can
bundle.

This file contains every MediaPipe-specific line in the project. Nothing
else imports mediapipe.
"""

from __future__ import annotations

import math
import os

from .contract import GESTURES, Capabilities, FaceSignal

import sys

# In a PyInstaller build the bundled files live under sys._MEIPASS; in a
# dev checkout they live in <project>/models. Never downloaded at runtime.
_ROOT = (getattr(sys, "_MEIPASS", None) if getattr(sys, "frozen", False)
         else os.path.dirname(os.path.dirname(os.path.dirname(
             os.path.abspath(__file__)))))
MODEL_DIR = os.path.join(_ROOT, "models")
DEFAULT_MODEL = os.path.join(MODEL_DIR, "face_landmarker.task")

# blendshape name -> our fixed vocabulary
_MAP = {
    "mouthSmileLeft": "smile_left",
    "mouthSmileRight": "smile_right",
    "browOuterUpLeft": "brow_left",
    "browOuterUpRight": "brow_right",
    "jawOpen": "jaw_open",
    "eyeBlinkLeft": "blink_left",
    "eyeBlinkRight": "blink_right",
    "mouthPucker": "mouth_pucker",
}
_NOSE_TIP = 1        # canonical face mesh index


class MediaPipeTracker:
    def __init__(self, model_path: str = DEFAULT_MODEL):
        self.model_path = model_path
        self._lm = None
        self._reset_pending = False

    # ---- contract -------------------------------------------------------
    def open(self) -> Capabilities:
        if not os.path.exists(self.model_path):
            raise FileNotFoundError(
                f"face model not found: {self.model_path}\n"
                "The installer ships it; in a dev checkout, fetch "
                "face_landmarker.task into models/.")
        from mediapipe.tasks import python as mp_python
        from mediapipe.tasks.python import vision as mp_vision

        self._mp_vision = mp_vision
        opts = mp_vision.FaceLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=self.model_path),
            running_mode=mp_vision.RunningMode.VIDEO,
            num_faces=1,
            output_face_blendshapes=True,
            output_facial_transformation_matrixes=True,
        )
        self._lm = mp_vision.FaceLandmarker.create_from_options(opts)
        return Capabilities(
            name="mediapipe",
            has_pose=True,
            gestures=frozenset(GESTURES),
            native_gestures=True,
            model_bytes=os.path.getsize(self.model_path),
        )

    def process(self, frame, t_capture: float):
        if self._lm is None or frame is None:
            return None
        import mediapipe as mp
        import numpy as np
        import cv2

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        image = mp.Image(image_format=mp.ImageFormat.SRGB,
                         data=np.ascontiguousarray(rgb))
        # VIDEO mode needs monotonically increasing integer ms
        ts = int(t_capture * 1000.0)
        if ts <= getattr(self, "_last_ts", -1):
            ts = getattr(self, "_last_ts", 0) + 1
        self._last_ts = ts
        res = self._lm.detect_for_video(image, ts)

        mats = getattr(res, "facial_transformation_matrixes", None)
        if not mats:
            return None

        yaw, pitch, roll = _euler_rad(mats[0])

        anchor = (0.5, 0.5)
        face_box = (0.0, 0.0, 0.0, 0.0)
        lms = getattr(res, "face_landmarks", None)
        if lms and len(lms[0]) > _NOSE_TIP:
            p = lms[0][_NOSE_TIP]
            anchor = (float(p.x), float(p.y))
            xs = [q.x for q in lms[0]]
            ys = [q.y for q in lms[0]]
            face_box = (float(min(xs)), float(min(ys)),
                        float(max(xs)), float(max(ys)))

        g: dict[str, float] = {}
        bs = getattr(res, "face_blendshapes", None)
        if bs:
            raw: dict[str, float] = {}
            for c in bs[0]:
                k = _MAP.get(c.category_name)
                if k:
                    raw[k] = float(c.score)
            # the fixed vocabulary, derived where a side pair exists
            if "smile_left" in raw or "smile_right" in raw:
                g["smile"] = max(raw.get("smile_left", 0.0),
                                 raw.get("smile_right", 0.0))
            if "brow_left" in raw or "brow_right" in raw:
                g["brow_raise"] = 0.5 * (raw.get("brow_left", 0.0)
                                         + raw.get("brow_right", 0.0))
            for k in ("jaw_open", "blink_left", "blink_right", "mouth_pucker"):
                if k in raw:
                    g[k] = raw[k]

        low_light = False
        try:
            low_light = bool(frame.mean() < 42.0)
        except Exception:
            pass

        return FaceSignal(t_capture=t_capture, yaw=yaw, pitch=pitch, roll=roll,
                          anchor=anchor, gestures=g, quality=1.0,
                          low_light=low_light, face_box=face_box)

    def reset(self) -> None:
        # VIDEO mode keeps temporal state keyed off the timestamp; the only
        # state we hold is that timestamp, so drop it.
        self._last_ts = -1

    def close(self) -> None:
        if self._lm is not None:
            self._lm.close()
            self._lm = None


def _euler_rad(m) -> tuple[float, float, float]:
    """4x4 facial transformation matrix -> yaw, pitch, roll in RADIANS,
    +yaw = turning to HIS right, +pitch = looking up, +roll = tilting
    clockwise as the camera sees it.

    MediaPipe's camera frame is x to image-right, y up, z toward the
    viewer, and a frontal face is close to the identity. So the head's
    three motions are rotations about three different camera axes:

        turning (yaw)     about y
        nodding (pitch)   about x
        tilting (roll)    about z, i.e. in the image plane

    The matrix is decomposed as R = Ry(a) Rx(b) Rz(c), yaw outermost,
    which keeps yaw and pitch independent of a resting tilt.

    An earlier version decomposed as Rz Ry Rx and so read an in-plane
    tilt as yaw and a turn as pitch: tilting the head moved the pointer
    sideways. The synthetic tracker emits angles directly, so no rig could
    see it; `selftest.t_pose_axes` now pins this against a real image.

    Signs, checked on bench/models/portrait.jpg: the face's forward vector
    (third column) has +x when the nose sits right of the face centre in
    the image. An unmirrored webcam shows HIS right on the image's left,
    so turning to his right gives -x, hence yaw = -a. Looking up gives +y,
    hence pitch = -b. A counter-clockwise image rotation gives +c, hence
    roll = -c for clockwise-positive.
    """
    try:
        r = [[float(m[i][j]) for j in range(3)] for i in range(3)]
    except TypeError:
        r = [[float(m[i, j]) for j in range(3)] for i in range(3)]
    sb = max(-1.0, min(1.0, -r[1][2]))
    b = math.asin(sb)
    if abs(sb) < 0.9999:
        a = math.atan2(r[0][2], r[2][2])
        c = math.atan2(r[1][0], r[1][1])
    else:                                   # gimbal lock, never at a desk
        a = math.atan2(-r[2][0], r[0][0])
        c = 0.0
    return -a, -b, -c
