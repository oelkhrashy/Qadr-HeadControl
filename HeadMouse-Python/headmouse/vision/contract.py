"""The whole vision boundary. Taken from FACE-STACK.md; do not widen it.

Four methods, two dataclasses, one fixed gesture vocabulary. The control
loop never learns which model is running.

Rules that make a model swap a swap:
  * process() returns None when no face is found. It never raises for a
    missing face and never blocks on I/O. None means "hold position, do
    not click".
  * Angles are radians, in the tracker's own frame. Sensitivity, dead
    zones, inversion, gain curves and screen mapping belong to the
    control loop. A new backend must never require retuning those.
  * t_capture is passed IN, not measured inside. The grab thread stamps
    the frame; the tracker carries the value through. That is what lets
    the control loop compensate for latency and drop stale frames.
  * gestures only contains keys listed in Capabilities.gestures, so a
    landmark-only backend degrades visibly instead of silently returning
    0.0 for ever.
  * No smoothing, no thresholds, no hysteresis, no click logic in here.
    Raw per-frame values only. Filtering is the control loop's job and
    its group delay is the control loop's budget to spend.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Protocol

GESTURES = ("smile", "brow_raise", "jaw_open", "blink_left", "blink_right",
            "mouth_pucker")


@dataclass(frozen=True)
class Capabilities:
    name: str
    has_pose: bool
    gestures: frozenset
    native_gestures: bool
    model_bytes: int


@dataclass(frozen=True)
class FaceSignal:
    t_capture: float                 # monotonic seconds, when the FRAME was taken
    yaw: float                       # radians, +right
    pitch: float                     # radians, +up
    roll: float                      # radians, +clockwise
    anchor: tuple                    # nose tip, normalised 0..1 image coords
    gestures: dict                   # 0..1 per key present in Capabilities
    quality: float                   # 0..1
    low_light: bool = False
    # Face bounding box from the landmarks, normalised 0..1 image coords
    # (x0, y0, x1, y1). Used only for placement guidance and for refusing
    # to learn a neutral pose from a clipped face; never for pointing.
    # (0, 0, 0, 0) means the backend does not know.
    face_box: tuple = (0.0, 0.0, 0.0, 0.0)


class FaceTracker(Protocol):
    def open(self) -> Capabilities: ...
    def process(self, frame, t_capture: float) -> "FaceSignal | None": ...
    def reset(self) -> None: ...
    def close(self) -> None: ...
