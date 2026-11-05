"""Vision layer. The factory is the only name the app needs."""

from .contract import GESTURES, Capabilities, FaceSignal, FaceTracker  # noqa: F401


def create_tracker(name: str, **kw):
    """Backend name lives in config, not in the control loop."""
    if name in ("mediapipe", "mp", "auto"):
        from .mediapipe_tracker import MediaPipeTracker
        return MediaPipeTracker(**kw)
    if name in ("synthetic", "fake", "none"):
        from .synthetic_tracker import SyntheticTracker
        return SyntheticTracker(**kw)
    raise ValueError(f"unknown face tracker {name!r}")
