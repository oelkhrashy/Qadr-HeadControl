"""Windows cursor + global hotkeys, via ctypes only. No extra packages,
no admin rights, no hooks that could wedge the desktop.

Deliberate design decision: we use SetCursorPos, which SETS the pointer
position and nothing else. We never install a low-level mouse hook, never
call ClipCursor, never capture input. That means a helper can always grab
the physical mouse and take over, and if this process dies the pointer is
simply left where it was. Nothing to clean up, nothing to unwedge.
"""

from __future__ import annotations

import ctypes
from ctypes import wintypes

user32 = ctypes.WinDLL("user32", use_last_error=True)


class POINT(ctypes.Structure):
    _fields_ = [("x", wintypes.LONG), ("y", wintypes.LONG)]


def make_dpi_aware() -> str:
    """Without this, on a scaled display GetSystemMetrics lies and the
    cursor stops short of the real screen edge."""
    try:
        # per-monitor v2
        if user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4)):
            return "per-monitor-v2"
    except AttributeError:
        pass
    try:
        shcore = ctypes.WinDLL("shcore")
        if shcore.SetProcessDpiAwareness(2) == 0:
            return "per-monitor"
    except Exception:
        pass
    try:
        if user32.SetProcessDPIAware():
            return "system"
    except Exception:
        pass
    return "none"


def virtual_screen() -> tuple[int, int, int, int]:
    """(left, top, width, height) of the whole virtual desktop, so a
    multi-monitor setup is clamped to the union, not to monitor 1."""
    SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN = 76, 77
    SM_CXVIRTUALSCREEN, SM_CYVIRTUALSCREEN = 78, 79
    return (user32.GetSystemMetrics(SM_XVIRTUALSCREEN),
            user32.GetSystemMetrics(SM_YVIRTUALSCREEN),
            user32.GetSystemMetrics(SM_CXVIRTUALSCREEN),
            user32.GetSystemMetrics(SM_CYVIRTUALSCREEN))


def get_cursor() -> tuple[int, int]:
    p = POINT()
    user32.GetCursorPos(ctypes.byref(p))
    return p.x, p.y


def set_cursor(x: int, y: int) -> bool:
    return bool(user32.SetCursorPos(int(x), int(y)))


# --------------------------------------------------------------------------
# Hotkeys by polling. GetAsyncKeyState needs no window, no message pump and
# no hook, so the kill key still works even if the HUD thread is wedged.
# --------------------------------------------------------------------------
VK = {
    "F7": 0x76, "F8": 0x77, "F9": 0x78, "F10": 0x79, "F11": 0x7A,
    "F12": 0x7B, "ESC": 0x1B, "SPACE": 0x20,
    "CTRL": 0x11, "SHIFT": 0x10, "ALT": 0x12,
    "[": 0xDB, "]": 0xDD, "-": 0xBD, "=": 0xBB,
    "Q": 0x51, "PAUSE": 0x13,
}


class Hotkeys:
    """Edge-triggered polling. pressed('F8') is True exactly once per
    physical press."""

    def __init__(self, names=()):
        self._down = {n: False for n in names}

    def poll(self) -> set[str]:
        fired = set()
        for name, was in list(self._down.items()):
            vk = VK.get(name)
            if vk is None:
                continue
            now = bool(user32.GetAsyncKeyState(vk) & 0x8000)
            if now and not was:
                fired.add(name)
            self._down[name] = now
        return fired

    def held(self, name: str) -> bool:
        vk = VK.get(name)
        return bool(vk and (user32.GetAsyncKeyState(vk) & 0x8000))
